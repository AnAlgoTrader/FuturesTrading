using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FootPrintTrader.Configuration;
using FootPrintTrader.Domain.Footprint;
using FootPrintTrader.UI.Models;

namespace FootPrintTrader.UI.Rendering;

public static class FootprintRenderBuilder
{
    public const string UpColor = "#2fcf93";
    public const string DownColor = "#f0647b";
    public const string WarnColor = "#f2b705";
    public const string PocBorder = "#f2b705";
    public const string PriceBorder = "#ff8c1a";
    public const string AskFill = "rgba(47,207,147,0.42)";
    public const string BidFill = "rgba(240,100,123,0.42)";

    private const decimal ImbalanceRatio = 0.6m;
    private const decimal MinVolumeRatio = 0.1m;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static FootprintRenderSnapshot Build(
        FootprintSnapshot snapshot,
        double tickSize,
        double currentPrice,
        MarketProfile market,
        FootprintAnalyzer analyzer = null,
        IReadOnlyDictionary<TimeOnly, double> averageVolumeByTime = null)
    {
        var result = new FootprintRenderSnapshot
        {
            RowSize = tickSize,
            PriceDecimals = market.PriceDecimals,
            BarIntervalSeconds = market.BarIntervalSeconds,
            Colors = new Dictionary<string, string>
            {
                ["background"] = "#0f131a",
                ["text"] = "#e4e8f0",
                ["muted"] = "#8a93a6",
                ["grid"] = "#262d3b",
                ["up"] = UpColor,
                ["down"] = DownColor,
                ["bar"] = "rgba(130,150,210,0.18)"
            }
        };

        var signalsMap = FootprintSignalDetector.DetectSignals(snapshot.Completed, tickSize, market.Signals);

        foreach (var bar in snapshot.Completed)
        {
            var renderBar = ConvertBar(bar, market.BarIntervalSeconds, averageVolumeByTime);
            if (signalsMap.TryGetValue(bar.Id, out var signals))
            {
                var tag = CreateTag(signals);
                renderBar.Header = tag;
                renderBar.CandleFooter = tag;
            }
            result.Bars.Add(renderBar);
        }

        if (snapshot.Current != null)
            result.Bars.Add(ConvertBar(snapshot.Current, market.BarIntervalSeconds, averageVolumeByTime));

        if (currentPrice > 0 && result.Bars.Count > 0)
            MarkCurrentPrice(result.Bars[^1], currentPrice, tickSize);

        if (analyzer != null)
        {
            result.Context = analyzer.AnalyzeContext(snapshot, tickSize, currentPrice, market);

            var contextBar = snapshot.Current ?? snapshot.Completed.LastOrDefault();
            if (contextBar != null)
            {
                var rvol = RelativeVolumeFooter(contextBar, market.BarIntervalSeconds, averageVolumeByTime);
                result.Context.RelativeVolume = rvol.Text;
                result.Context.RelativeVolumeHigh = rvol.Color == WarnColor;
            }
        }

        return result;
    }

    public static FootprintBarTag CreateTag(IReadOnlyList<DetectedFootprintSignal> signals)
    {
        if (signals == null || signals.Count == 0) return null;

        var text = string.Join(" · ", signals.Select(s => s.Acronym));

        var hasBullish = signals.Any(s => s.Tone == "bullish");
        var hasBearish = signals.Any(s => s.Tone == "bearish");
        var hasPhase = signals.Any(s => s.Tone == "phase");

        string color;
        string bg;
        string border;

        if (hasBullish && !hasBearish)
        {
            color = UpColor;
            bg = "rgba(47, 207, 147, 0.22)";
            border = "rgba(47, 207, 147, 0.75)";
        }
        else if (hasBearish && !hasBullish)
        {
            color = DownColor;
            bg = "rgba(240, 100, 123, 0.22)";
            border = "rgba(240, 100, 123, 0.75)";
        }
        else if (hasPhase && !hasBullish && !hasBearish)
        {
            color = WarnColor;
            bg = "rgba(242, 183, 5, 0.22)";
            border = "rgba(242, 183, 5, 0.75)";
        }
        else
        {
            color = "#e4e8f0";
            bg = "rgba(138, 147, 166, 0.22)";
            border = "rgba(138, 147, 166, 0.70)";
        }

        return new FootprintBarTag(text, color, bg, border);
    }

    private static void MarkCurrentPrice(FootprintRenderBar bar, double price, double tickSize)
    {
        var rowPrice = Math.Round(price / tickSize) * tickSize;
        var tolerance = tickSize * 0.5 + 1e-9;

        foreach (var row in bar.Rows)
        {
            if (Math.Abs(row.Price - rowPrice) > tolerance) continue;
            row.PriceBorder = PriceBorder;
            return;
        }
    }

    private static FootprintRenderBar ConvertBar(
        FootprintBarData bar,
        int barIntervalSeconds,
        IReadOnlyDictionary<TimeOnly, double> averageVolumeByTime)
    {
        var levels = bar.Levels;
        var maxSide = levels.Length == 0
            ? 0m
            : levels.Max(l => Math.Max(l.BidVolume, l.AskVolume));
        var maxTotal = levels.Length == 0
            ? 0m
            : levels.Max(l => l.Total);
        var volumeFloor = maxTotal * MinVolumeRatio;

        var pocIndex = -1;
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i].Total != maxTotal) continue;
            pocIndex = i;
            break;
        }

        var rows = new List<FootprintRenderRow>(levels.Length);
        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            var row = new FootprintRenderRow
            {
                Price = Math.Round(level.Price, 10),
                Bid = level.BidVolume,
                Ask = level.AskVolume,
                BidBar = maxSide > 0 ? (double)(level.BidVolume / maxSide) : 0,
                AskBar = maxSide > 0 ? (double)(level.AskVolume / maxSide) : 0
            };

            if (i == pocIndex)
                row.Border = PocBorder;

            if (level.Total > 0 && level.Total >= volumeFloor &&
                Math.Abs(level.Delta) / level.Total >= ImbalanceRatio)
            {
                if (level.AskVolume > level.BidVolume) row.AskFill = AskFill;
                else if (level.BidVolume > level.AskVolume) row.BidFill = BidFill;
            }

            rows.Add(row);
        }

        var delta = bar.Delta;
        return new FootprintRenderBar
        {
            // Must match the key used by FootprintSignalDetector (the classifier's unique bar id).
            // StartTime is not unique: two bars can start in the same IBKR second.
            Id = bar.Id.ToString(CultureInfo.InvariantCulture),
            Label = FormatTime(bar.StartTime),
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Color = bar.Close < bar.Open ? DownColor : UpColor,
            Rows = rows,
            Footer = new List<FootprintRenderFooter>
            {
                new()
                {
                    Text = delta.ToString(CultureInfo.InvariantCulture),
                    Color = delta > 0 ? UpColor : delta < 0 ? DownColor : null
                },
                new() { Text = bar.Volume.ToString(CultureInfo.InvariantCulture) },
                RelativeVolumeFooter(bar, barIntervalSeconds, averageVolumeByTime)
            }
        };
    }

    /// <summary>
    /// Relative volume: this bar's volume divided by the average volume for the same
    /// minute(s) of the day. Returns "-" when the baseline does not cover the bar.
    /// </summary>
    private static FootprintRenderFooter RelativeVolumeFooter(
        FootprintBarData bar,
        int barIntervalSeconds,
        IReadOnlyDictionary<TimeOnly, double> averageVolumeByTime)
    {
        if (averageVolumeByTime == null || averageVolumeByTime.Count == 0 || bar.StartTime <= 0)
            return new FootprintRenderFooter { Text = "-" };

        // Bar times are shown in the machine's local (Madrid) time and the baseline is keyed by
        // Madrid wall-clock minute, so no timezone conversion is needed.
        var start = DateTimeOffset.FromUnixTimeSeconds(bar.StartTime).ToLocalTime().DateTime;
        var minutes = Math.Max(1, (int)Math.Ceiling(Math.Max(1, barIntervalSeconds) / 60.0));

        double expectedVolume = 0;
        for (var i = 0; i < minutes; i++)
        {
            var timeOfDay = TimeOnly.FromDateTime(start.AddMinutes(i));
            if (!averageVolumeByTime.TryGetValue(timeOfDay, out var average) || average <= 0)
                return new FootprintRenderFooter { Text = "-" };

            expectedVolume += average;
        }

        var ratio = (double)bar.Volume / expectedVolume;
        return new FootprintRenderFooter
        {
            Text = ratio.ToString("0.0", CultureInfo.InvariantCulture) + "x",
            Color = ratio >= 1.5 ? WarnColor : null
        };
    }

    public static string FormatTime(long unixSeconds)
    {
        if (unixSeconds <= 0 || unixSeconds > 4102444800) return "";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            .ToLocalTime()
            .ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
