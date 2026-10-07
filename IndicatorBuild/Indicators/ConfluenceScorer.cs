using System;
using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

/// <summary>
/// Confluence scorer (M7). Each actionable event on the bar that released it is scored
/// against 4 symmetric, binary factors (1 point each, no factor can be unfair to reversal
/// signals like exhaustion/divergence):
///   +1 trend alignment   (close on the event's side of the 50-EMA)
///   +1 volume            (bar volume >= 1.5 x 50-bar average)
///   +1 displacement      (bar range >= 0.7 x ATR — real participation)
///   +1 level significance(close within 0.35 x ATR of an active zone boundary
///                         or a confirmed swing extreme from the last 200 bars)
/// An event is Qualified when its score reaches the gate, otherwise it renders gray.
/// "Significant level" is computed time-aware: zone boundaries count only inside the
/// zone's active window, swings only after they have confirmed — no future repainting.
/// </summary>
public static class ConfluenceScorer
{
    /// <summary>Actionable/tradeable events: colored only when Qualified.</summary>
    public static readonly HashSet<Concept> ColoredGated = new()
    {
        Concept.ChoCh,
        Concept.LiquiditySweep,
        Concept.Breakout,
        Concept.Pullback,
        Concept.Exhaustion,
        Concept.Divergence,
        Concept.Expansion,
        Concept.SignOfStrength,
        Concept.SignOfWeakness,
        Concept.AccumulationBreakout,
        Concept.DistributionBreakout
    };

    /// <summary>Events whose markers are also suppressed when below the gate.</summary>
    public static readonly HashSet<Concept> MarkerGated = new()
    {
        Concept.LiquiditySweep,
        Concept.Breakout,
        Concept.Pullback,
        Concept.Exhaustion,
        Concept.Divergence,
        Concept.Expansion,
        Concept.SignOfStrength,
        Concept.SignOfWeakness,
        Concept.AccumulationBreakout,
        Concept.DistributionBreakout
    };

    public static void Score(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<CandleAnalysis> analyses,
        IReadOnlyList<Zone> zones,
        IReadOnlyList<SwingPoint> swings,
        int gate = IndicatorConfig.ConfluenceGate)
    {
        var completedZones = zones
            .Where(z => z.EndTime >= 0 && z.StartTime >= 0 && z.Top > z.Bottom)
            .OrderBy(z => z.StartTime)
            .ToArray();
        var sortedSwings = swings.OrderBy(s => s.Time).ToArray();

        var ema = new EmaCalculator(IndicatorConfig.ConfluenceEmaPeriod);
        var atr = new AtrCalculator(IndicatorConfig.AtrPeriod);
        var avgVol = new SmaCalculator(IndicatorConfig.VolumeAveragePeriod);

        var zoneCursor = 0;
        var activeZoneSet = new List<(long End, double Top, double Bottom)>();
        var swingStart = 0;
        int barCount = candles.Count;

        for (var i = 0; i < barCount && i < analyses.Count; i++)
        {
            var candle = candles[i];
            var barTime = candle.Time;
            var emaValue = ema.Next(candle.Close);
            var atrValue = atr.Next(candle);
            var avg = avgVol.Next(candle.Volume);

            while (zoneCursor < completedZones.Length && completedZones[zoneCursor].StartTime <= barTime)
            {
                var z = completedZones[zoneCursor++];
                activeZoneSet.Add((z.EndTime, z.Top, z.Bottom));
            }
            activeZoneSet.RemoveAll(z => z.End < barTime);

            // bar-index window: resolution-independent (unlike a time window on gapped/daily bars)
            while (swingStart < sortedSwings.Length && sortedSwings[swingStart].Index < i - IndicatorConfig.ConfluenceSwingWindowBars)
                swingStart++;

            var tol = atrValue is { } av && av > 0 ? IndicatorConfig.ConfluenceLevelToleranceAtr * av : 0;

            foreach (var evt in analyses[i].Events)
            {
                evt.Score = 0;
                if (!ColoredGated.Contains(evt.Type)) continue;
                if (evt.Direction != "Bullish" && evt.Direction != "Bearish")
                {
                    evt.Qualified = false;
                    continue;
                }

                var dir = evt.Direction == "Bullish" ? 1 : -1;
                var score = 0;

                if (emaValue is { } e && dir * (candle.Close - e) > 0) score++;

                if (avg is { } a && a > 0 && candle.Volume >= IndicatorConfig.ConfluenceVolumeFactor * a) score++;

                if (atrValue is { } at && at > 0 && (candle.High - candle.Low) >= IndicatorConfig.ConfluenceDisplaceAtr * at) score++;

                if (tol > 0 && NearLevel(i, candle, tol, activeZoneSet, sortedSwings, swingStart)) score++;

                evt.Score = score;
                evt.Qualified = score >= gate;
            }
        }
    }

    private static bool NearLevel(
        int barIndex,
        Candle candle,
        double tol,
        List<(long End, double Top, double Bottom)> activeZones,
        SwingPoint[] sortedSwings,
        int swingStart)
    {
        foreach (var z in activeZones)
        {
            if (Math.Abs(candle.Close - z.Top) <= tol || Math.Abs(candle.Close - z.Bottom) <= tol)
                return true;
        }

        for (var k = swingStart; k < sortedSwings.Length && sortedSwings[k].Index <= barIndex; k++)
        {
            if (Math.Abs(candle.Close - sortedSwings[k].Price) <= tol)
                return true;
        }
        return false;
    }
}