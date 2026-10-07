using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

/// <summary>
/// Confluence scorer (M7). Scores each actionable event on the bar that released it and,
/// if it falls below the gate, marks it unqualified so rendering keeps it gray / unmarked.
/// Factors (max 4, additive, all aligned to the event's predicted direction):
///   +1 trend alignment   (close on the event side of the 50-EMA)
///   +1 RSI momentum      (RSI on the event side of 50)
///   +1 displacement body (directional body >= 0.35 x ATR)
///   +1 volume            (bar volume >= 1.5 x 50-bar average)
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

    /// <summary>Events whose markers are also suppressed when below the gate
    /// (structure markers — BOS/CHoCH — and confirmed-only Wyckoff labels stay visible).</summary>
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
        int gate = IndicatorConfig.ConfluenceGate)
    {
        var ema = new EmaCalculator(IndicatorConfig.ConfluenceEmaPeriod);
        var rsi = new RsiCalculator(IndicatorConfig.RsiPeriod);
        var atr = new AtrCalculator(IndicatorConfig.AtrPeriod);
        var avgVol = new SmaCalculator(IndicatorConfig.VolumeAveragePeriod);

        for (var i = 0; i < candles.Count && i < analyses.Count; i++)
        {
            var candle = candles[i];
            var emaValue = ema.Next(candle.Close);
            var rsiValue = rsi.Next(candle);
            var atrValue = atr.Next(candle);
            var avg = avgVol.Next(candle.Volume);

            foreach (var evt in analyses[i].Events)
            {
                if (!ColoredGated.Contains(evt.Type)) continue;
                if (evt.Direction != "Bullish" && evt.Direction != "Bearish")
                {
                    evt.Qualified = false;
                    continue;
                }

                var dir = evt.Direction == "Bullish" ? 1 : -1;
                var score = 0;

                if (emaValue is { } e && dir * (candle.Close - e) > 0) score++;
                if (rsiValue is { } r && dir * (r - 50) > 0) score++;
                if (atrValue is { } a && a > 0 && dir * (candle.Close - candle.Open) >= IndicatorConfig.ConfluenceBodyAtr * a) score++;
                if (avg is { } av && av > 0 && candle.Volume >= IndicatorConfig.ConfluenceVolumeFactor * av) score++;

                evt.Score = score;
                evt.Qualified = score >= gate;
            }
        }
    }
}