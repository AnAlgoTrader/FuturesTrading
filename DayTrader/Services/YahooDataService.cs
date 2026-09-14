using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DayTrader.Models;

namespace DayTrader.Services;

public class YahooDataService
{
    private readonly HttpClient _httpClient;

    private const double CashStaleAfterMinutes = 15.0;
    private const double MaxAlignmentGapMinutes = 10.0;
    private const double MaxBasisFractionOfSpot = 0.03;

    public YahooDataService()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
    }

    /// <summary>VIX/VVIX levels plus IV rank/percentile, VRP, 5d realized vol, the VRP
    /// z-score, and VVIX's own percentile -- used only by Tracker's regime panel, not by
    /// DataGatherer.</summary>
    public Task<(double Vix, double Vvix, double IvRank, double IvPercentile, double Vrp, double Rv5, double VrpZScore, double VvixPercentile)> GetMarketRegimeAsync()
        => GetMarketRegimeAsync("^VIX", "^GSPC");

    /// <summary>Vol index/VVIX levels plus IV rank/percentile, VRP, 5d realized vol, the VRP
    /// z-score, and VVIX's own percentile for the specified underlying index and volatility index.</summary>
    public async Task<(double Vix, double Vvix, double IvRank, double IvPercentile, double Vrp, double Rv5, double VrpZScore, double VvixPercentile)> GetMarketRegimeAsync(
        string volTicker, string cashTicker)
    {
        var volTask  = GetIndexHistory(volTicker);
        var vvixTask = GetIndexHistory("^VVIX");
        var cashTask = GetIndexHistory(cashTicker);

        await Task.WhenAll(volTask, vvixTask, cashTask);

        var volBars  = volTask.Result;
        var vvixBars = vvixTask.Result;
        var cashBars = cashTask.Result;

        MarketDataException.RequireCount($"{volTicker} history", volBars.Count, 30);
        MarketDataException.RequireCount("VVIX history", vvixBars.Count, 30);
        MarketDataException.RequireCount($"{cashTicker} history", cashBars.Count, 31);

        var vix  = MarketDataException.Require(volTicker,  volBars[^1],  1.0, 200.0);
        var vvix = MarketDataException.Require("VVIX", vvixBars[^1], 10.0, 500.0);

        var min = volBars.Min();
        var max = volBars.Max();
        if (min >= max)
            throw new MarketDataException($"{volTicker} history", "series has no range; cannot compute IV rank");

        var ivRank = Math.Clamp(Math.Round((vix - min) / (max - min) * 100, 2), 0, 100);
        var below = volBars.Count(v => v < vix);
        var ivPercentile = Math.Round((double)below / volBars.Count * 100, 2);

        // Same percentile-rank pattern as ivPercentile above, but ranking VVIX against its own
        // 1-year history instead of dividing by VIX -- the raw VVIX/VIX ratio conflates VVIX's
        // own behavior with VIX's level (VVIX sits in a narrow, structurally elevated range
        // regardless of regime, so dividing by VIX mostly just detects "VIX is low"). Backtested
        // across 2018-2026 (including the 2020 and 2022 stress periods): this percentile's
        // Pass/Warn/Block buckets separate forward 5-day realized SPX vol monotonically
        // (median ~9.8/12.3/16.9), where the old ratio's buckets ran backwards.
        var belowVvix = vvixBars.Count(v => v < vvix);
        var vvixPercentile = Math.Round((double)belowVvix / vvixBars.Count * 100, 2);

        var rv5         = CalculateRealizedVol($"{cashTicker} 5d realized vol",  cashBars[^6..]);
        var realizedVol = CalculateRealizedVol($"{cashTicker} 30d realized vol", cashBars[^31..]);
        var vrp         = Math.Round(vix - realizedVol, 2);
        var vrpZScore   = ComputeVrpZScore(volBars, cashBars, vrp);

        return (vix, vvix, ivRank, ivPercentile, vrp, Math.Round(rv5, 2), vrpZScore, vvixPercentile);
    }

    public Task<double> GetSpxSpotPrice() => GetSpotPrice("^GSPC", "ES=F");

    /// <summary>
    /// Spot price for strike selection. During regular hours this is the cash index print.
    /// Outside them the cash index is frozen at the close, so the price is re-derived from
    /// futures as <c>Futures_now - basis</c>.
    /// </summary>
    public async Task<double> GetSpotPrice(string cashTicker, string futuresTicker)
    {
        var cashSymbolEscaped = Uri.EscapeDataString(cashTicker);
        var cashSeries = await GetIntradaySeries(cashSymbolEscaped);
        MarketDataException.RequireCount($"{cashTicker} intraday series", cashSeries.Count, 1);

        var (cashTime, cashPrice) = cashSeries[^1];
        MarketDataException.Require($"{cashTicker} cash index", cashPrice, 100.0, 100_000.0);

        var cashAgeMinutes = (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(cashTime)).TotalMinutes;
        if (cashAgeMinutes <= CashStaleAfterMinutes)
            return cashPrice;

        var futuresSymbolEscaped = Uri.EscapeDataString(futuresTicker);
        var futuresSeries = await GetIntradaySeries(futuresSymbolEscaped);
        MarketDataException.RequireCount($"{futuresTicker} futures intraday series", futuresSeries.Count, 1);

        // Futures carry a cost-of-carry premium over the cash index, so cannot be used as a
        // spot price directly. Measuring the basis at the last cash print cancels that premium.
        // It is re-measured every cycle, so the quarterly roll needs no special handling.
        var alignedIdx = futuresSeries.FindLastIndex(p => p.Time <= cashTime);
        if (alignedIdx < 0)
            throw new MarketDataException($"{futuresTicker} futures intraday series",
                "no bar at or before the last cash index print; cannot measure the basis");

        var aligned = futuresSeries[alignedIdx];
        var alignmentGapMinutes = (cashTime - aligned.Time) / 60.0;
        if (alignmentGapMinutes > MaxAlignmentGapMinutes)
            throw new MarketDataException($"{futuresTicker} futures intraday series",
                $"closest bar to the last cash print is {alignmentGapMinutes:F0} minutes early; " +
                "the basis would be unreliable");

        var basis = aligned.Close - cashPrice;
        if (Math.Abs(basis) > cashPrice * MaxBasisFractionOfSpot)
            throw new MarketDataException($"{futuresTicker}/{cashTicker} basis",
                $"basis of {basis:F2} against a cash index of {cashPrice:F2} is not credible");

        var futuresNow = futuresSeries[^1];
        var estimate = futuresNow.Close - basis;

        Console.WriteLine(
            $"{cashTicker} cash is {cashAgeMinutes:F0} min stale ({cashPrice:F2}); " +
            $"{futuresTicker} {futuresNow.Close:F2} - basis {basis:F2} = {estimate:F2}");

        return MarketDataException.Require($"{cashTicker} spot derived from {futuresTicker}", estimate, 100.0, 100_000.0);
    }

    private async Task<List<(long Time, double Close)>> GetIntradaySeries(string symbol)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range=5d&interval=1m";
            var json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);

            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var timestamps = result.GetProperty("timestamp");
            var closes = result.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");

            var series = new List<(long Time, double Close)>();
            using var timeEnum = timestamps.EnumerateArray();
            using var closeEnum = closes.EnumerateArray();
            while (timeEnum.MoveNext() && closeEnum.MoveNext())
            {
                if (closeEnum.Current.ValueKind != JsonValueKind.Number) continue;
                var close = closeEnum.Current.GetDouble();
                if (close > 0) series.Add((timeEnum.Current.GetInt64(), close));
            }

            return series;
        }
        catch (Exception ex)
        {
            throw new MarketDataException($"{symbol} intraday series", "fetch or parse failed", ex);
        }
    }

    public async Task<double> GetInterestRate()
    {
        const string url = "https://query1.finance.yahoo.com/v8/finance/chart/%5EIRX?range=5d&interval=1d";

        JsonElement closes;
        try
        {
            var json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            closes = doc.RootElement
                .GetProperty("chart").GetProperty("result")[0]
                .GetProperty("indicators").GetProperty("quote")[0]
                .GetProperty("close");
        }
        catch (Exception ex)
        {
            throw new MarketDataException("13-week T-bill rate (^IRX)", "fetch or parse failed", ex);
        }

        double? lastRate = null;
        foreach (var bar in closes.EnumerateArray())
            if (bar.ValueKind == JsonValueKind.Number)
                lastRate = bar.GetDouble();

        if (lastRate is null)
            throw new MarketDataException("13-week T-bill rate (^IRX)", "no numeric close in the series");

        return MarketDataException.Require("13-week T-bill rate (^IRX)", lastRate.Value / 100.0, 0.0, 0.25);
    }

    /// <summary>Daily closes for the last year, oldest first. Public for RealizedMoveCalculator's
    /// 20d/60d mean-absolute-return input, not just the internal VIX/VVIX/SPX regime calcs.</summary>
    public async Task<List<double>> GetIndexHistory(string symbol)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range=1y&interval=1d";
            var json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);

            var closes = doc.RootElement
                .GetProperty("chart").GetProperty("result")[0]
                .GetProperty("indicators").GetProperty("quote")[0]
                .GetProperty("close");

            var result = new List<double>();
            foreach (var bar in closes.EnumerateArray())
                if (bar.ValueKind == JsonValueKind.Number)
                    result.Add(bar.GetDouble());
            return result;
        }
        catch (Exception ex)
        {
            throw new MarketDataException($"{symbol} history", "fetch or parse failed", ex);
        }
    }

    /// <summary>Daily open/high/low/close bars for a symbol, up to the requested range, oldest
    /// first. The VRP model's backfill path: ^GSPC with range=10y fills the whole realized-vol
    /// history in one call (the free endpoint's cap for daily bars). Non-trading bars are skipped
    /// by requiring all four quotes plus a nonzero volume (indices like VIX report sparse or zero
    /// volume -- pass requireVolume: false for those). The observed timestamp is UTC midnight of
    /// the session, so its date equals the Eastern close date.</summary>
    public async Task<List<SpxDailyRecord>> GetDailyOhlc(string symbol, string range = "10y", bool requireVolume = true)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range={range}&interval=1d";
            var json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);

            var resultEl = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var quote = resultEl.GetProperty("indicators").GetProperty("quote")[0];

            var timestamps = resultEl.GetProperty("timestamp");
            var opens = quote.GetProperty("open");
            var highs = quote.GetProperty("high");
            var lows = quote.GetProperty("low");
            var closes = quote.GetProperty("close");
            var volumes = quote.GetProperty("volume");

            var result = new List<SpxDailyRecord>();
            using var t = timestamps.EnumerateArray();
            using var o = opens.EnumerateArray();
            using var h = highs.EnumerateArray();
            using var l = lows.EnumerateArray();
            using var c = closes.EnumerateArray();
            using var v = volumes.EnumerateArray();
            while (t.MoveNext() && o.MoveNext() && h.MoveNext() && l.MoveNext() && c.MoveNext() && v.MoveNext())
            {
                if (o.Current.ValueKind != JsonValueKind.Number) continue;
                if (h.Current.ValueKind != JsonValueKind.Number) continue;
                if (l.Current.ValueKind != JsonValueKind.Number) continue;
                if (c.Current.ValueKind != JsonValueKind.Number) continue;
                var volume = v.Current.ValueKind == JsonValueKind.Number ? v.Current.GetDouble() : 0;
                if (requireVolume && volume <= 0) continue;

                var open = o.Current.GetDouble();
                var high = h.Current.GetDouble();
                var low = l.Current.GetDouble();
                var close = c.Current.GetDouble();
                if (open <= 0 || close <= 0 || high < low || high <= 0 || low <= 0) continue;

                var date = DateOnly.FromDateTime(
                    DateTimeOffset.FromUnixTimeSeconds(t.Current.GetInt64()).UtcDateTime)
                    .ToString("yyyyMMdd");

                result.Add(new SpxDailyRecord
                {
                    CollectionDay = date,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                });
            }

            MarketDataException.RequireCount($"{symbol} daily OHLC", result.Count, 2);
            return result;
        }
        catch (Exception ex)
        {
            throw new MarketDataException($"{symbol} daily OHLC", "fetch or parse failed", ex);
        }
    }

    private static double CalculateRealizedVol(string source, IReadOnlyList<double> bars)
    {
        MarketDataException.RequireCount(source, bars.Count, 2);

        var sumSq = 0.0;
        for (var i = 1; i < bars.Count; i++)
        {
            if (bars[i] <= 0 || bars[i - 1] <= 0)
                throw new MarketDataException(source, $"non-positive close in the series ({bars[i - 1]}, {bars[i]})");
            var r = Math.Log(bars[i] / bars[i - 1]);
            sumSq += r * r;
        }
        return Math.Sqrt(sumSq / (bars.Count - 1) * 252) * 100;
    }

    private static double ComputeVrpZScore(List<double> vixBars, List<double> spxBars, double currentVrp)
    {
        MarketDataException.RequireCount("VRP z-score (VIX history)", vixBars.Count, 30);
        MarketDataException.RequireCount("VRP z-score (SPX history)", spxBars.Count, 31);

        var series = new List<double>();
        for (var i = 30; i < Math.Min(vixBars.Count, spxBars.Count); i++)
        {
            var rvSlice = spxBars.GetRange(i - 30, 31);
            var rv = CalculateRealizedVol("VRP z-score (rolling RV)", rvSlice);
            series.Add(vixBars[i] - rv);
        }

        MarketDataException.RequireCount("VRP z-score series", series.Count, 20);

        var mean = series.Average();
        var std  = Math.Sqrt(series.Average(v => (v - mean) * (v - mean)));
        if (std <= 0)
            throw new MarketDataException("VRP z-score series", "zero standard deviation; cannot normalise");

        return Math.Round((currentVrp - mean) / std, 2);
    }
}
