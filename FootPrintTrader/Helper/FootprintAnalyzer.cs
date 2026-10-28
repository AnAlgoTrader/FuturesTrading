using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using FootPrintTrader.Model;

namespace FootPrintTrader.Helper;

public class OrderFlowContext
{
    // Live Bar Metrics
    public double PocPrice { get; init; }
    public double CurrentDelta { get; init; }
    public double DeltaPercent { get; init; }
    public decimal CurrentVolume { get; init; }
    public string PocPosition { get; init; } = "-";
    public string PocMigration { get; init; } = "-";

    // Multi-bar / Session Structure
    public double CumulativeDelta { get; init; }
    public string DeltaDivergence { get; init; } = "None";
}

public static class FootprintAnalyzer
{
    // ============================================================
    // STATE
    // ============================================================
    private static readonly Lock _syncLock = new();
    private static readonly HashSet<long> _processedBars = new();
    private static OrderFlowContext _lastContext;
    private static decimal _sessionCumulativeDelta;

    // Rolling window of completed bars (price extremes + cumulative delta at close) for divergence.
    private static readonly List<DivergencePoint> _divergenceWindow = new();
    private static string _divergence = "None";
    private static double _divergencePrice;
    private static long _divergenceBarStart;

    private readonly record struct DivergencePoint(double High, double Low, decimal Cvd, decimal Delta, long Start);

    public static OrderFlowContext AnalyzeContext(FootprintSnapshot snapshot, double tickSize, double currentPrice, MarketProfile settings)
    {
        if (snapshot == null)
            return _lastContext ?? new OrderFlowContext();

        var completed = snapshot.Completed ?? Array.Empty<FootprintBarData>();
        var current = snapshot.Current;

        // Use live bar for real-time live metrics (delta, POC, volume), fallback to last completed
        var bar = current ?? completed.LastOrDefault();
        FootprintBarData prevBar = current != null
            ? completed.LastOrDefault()
            : (completed.Length > 1 ? completed[^2] : null);

        if (bar == null)
            return _lastContext ?? new OrderFlowContext();

        // POC Calculation & Zone Placement for current bar
        var levels = bar.Levels ?? Array.Empty<FootprintLevel>();
        var pocPrice = bar.Close;

        if (levels.Length > 0)
        {
            var pocLevel = levels.OrderByDescending(l => l.Total).First();
            pocPrice = pocLevel.Price;
        }

        // Live Bar Metrics
        var currentDelta = (double)bar.Delta;
        var currentVolume = bar.Volume;
        var deltaPercent = currentVolume > 0 ? currentDelta / (double)currentVolume * 100.0 : 0.0;

        var barRange = bar.High - bar.Low;
        string pocPosition;
        if (barRange > 1e-9)
        {
            var pos = (pocPrice - bar.Low) / barRange;
            pocPosition = pos >= 0.66 ? "High" : pos <= 0.33 ? "Low" : "Middle";
        }
        else pocPosition = "-";

        var pocMigration = "-";
        if (prevBar != null)
        {
            var prevPoc = prevBar.Close;
            var prevLevels = prevBar.Levels ?? Array.Empty<FootprintLevel>();
            if (prevLevels.Length > 0)
                prevPoc = prevLevels.OrderByDescending(l => l.Total).First().Price;
            if (pocPrice > prevPoc + tickSize * 0.5) pocMigration = "▲ Up";
            else if (pocPrice < prevPoc - tickSize * 0.5) pocMigration = "▼ Down";
            else pocMigration = "→ Flat";
        }

        // Multi-bar / Session Structure
        var liveDelta = current?.Delta ?? 0m;
        var cumulativeDelta = (double)(_sessionCumulativeDelta + liveDelta);

        lock (_syncLock)
        {
            // Register newly completed bars
            foreach (var compBar in completed)
            {
                if (_processedBars.Add(compBar.StartTime))
                {
                    _sessionCumulativeDelta += compBar.Delta;

                    // Maintain the divergence window (price extremes + CVD at bar close).
                    _divergenceWindow.Add(new DivergencePoint(compBar.High, compBar.Low, _sessionCumulativeDelta, compBar.Delta, compBar.StartTime));
                    while (_divergenceWindow.Count > settings.DivergenceWindowBars)
                        _divergenceWindow.RemoveAt(0);
                }
            }

            // Recompute CVD-vs-price divergence from the confirmed completed-bar window.
            ComputeDivergence(tickSize, settings);

            if (completed.Length > 0)
            {
                var oldestInWindow = completed.Min(c => c.StartTime);
                _processedBars.RemoveWhere(t => t < oldestInWindow);
            }
        }

        var result = new OrderFlowContext
        {
            PocPrice = pocPrice,
            CurrentDelta = currentDelta,
            DeltaPercent = deltaPercent,
            CurrentVolume = currentVolume,
            PocPosition = pocPosition,
            PocMigration = pocMigration,
            CumulativeDelta = cumulativeDelta,
            DeltaDivergence = _divergence
        };

        _lastContext = result;
        return result;
    }

    // ============================================================
    // DELTA DIVERGENCE (windowed CVD vs price, confirmed at bar close)
    // Bearish: new price high on a lower cumulative-delta high.
    // Bullish: new price low on a higher cumulative-delta low.
    // The CVD move must be >= settings.DivergenceMinCvdGapRatio of the prior extreme's
    // |CVD| — direction alone (a 1-contract shortfall) is noise.
    // ============================================================
    private static void ComputeDivergence(double tickSize, MarketProfile settings)
    {
        _divergence = "None";
        _divergencePrice = 0;
        _divergenceBarStart = 0;

        var n = _divergenceWindow.Count;
        if (n < 3)
            return;

        var latest = _divergenceWindow[^1];

        var priorHigh = double.MinValue;
        var priorHighCvd = 0m;
        var priorLow = double.MaxValue;
        var priorLowCvd = 0m;

        for (var i = 0; i < n - 1; i++)
        {
            var p = _divergenceWindow[i];
            if (p.High >= priorHigh) { priorHigh = p.High; priorHighCvd = p.Cvd; }
            if (p.Low <= priorLow) { priorLow = p.Low; priorLowCvd = p.Cvd; }
        }

        var bearGap = Math.Abs(priorHighCvd) * (decimal)settings.DivergenceMinCvdGapRatio;
        var bullGap = Math.Abs(priorLowCvd) * (decimal)settings.DivergenceMinCvdGapRatio;

        if (latest.High > priorHigh + tickSize * 0.5 &&
            latest.Cvd < priorHighCvd - bearGap &&
            latest.Delta <= 0)
        {
            _divergence = "Bearish";
            _divergencePrice = latest.High;
            _divergenceBarStart = latest.Start;
        }
        else if (latest.Low < priorLow - tickSize * 0.5 &&
                 latest.Cvd > priorLowCvd + bullGap &&
                 latest.Delta >= 0)
        {
            _divergence = "Bullish";
            _divergencePrice = latest.Low;
            _divergenceBarStart = latest.Start;
        }
    }

    public static int Decimals(double tickSize)
    {
        var s = tickSize.ToString(CultureInfo.InvariantCulture);
        var i = s.IndexOf('.');
        return i < 0 ? 0 : s.Length - i - 1;
    }
}
