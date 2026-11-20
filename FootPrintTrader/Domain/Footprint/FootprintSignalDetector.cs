using System;
using System.Collections.Generic;
using System.Linq;
using FootPrintTrader.Configuration;

namespace FootPrintTrader.Domain.Footprint;

public enum FootprintSignalType
{
    SellAbsorption,        // SAB - bullish: aggressive selling at the low rejected
    BuyAbsorption,         // BAB - bearish: aggressive buying at the high rejected
    BuyExhaustion,         // BXH - bearish: buying climax near the high
    SellExhaustion,        // SXH - bullish: selling climax near the low
    BreakoutConfirmation,  // BRK - bullish: close above prior range on delta
    BreakdownConfirmation, // BRD - bearish: close below prior range on delta
    FailedBreakout,        // FBO - bearish: up breakout re-enters the prior range
    FailedBreakdown        // FBD - bullish: down breakout re-enters the prior range
}

public class DetectedFootprintSignal
{
    public string Acronym { get; init; } = "";
    public string Tone { get; init; } = "neutral"; // "bullish" or "bearish"
}

/// <summary>
/// Classifies completed footprint bars into order-flow events. Every rule is evaluated
/// from the bar data alone, so this is a pure function: the host calls it once per second
/// with the whole completed history and the result must not depend on call count.
///
/// Absorption uses the footprint-specific part of the data (aggressive volume at the
/// rejected extreme); exhaustion, breakout and fade use OHLC/delta/volume.
/// </summary>
public static class FootprintSignalDetector
{
    public static Dictionary<long, List<DetectedFootprintSignal>> DetectSignals(
        FootprintBarData[] completed,
        double tickSize,
        SignalSettings settings)
    {
        var result = new Dictionary<long, List<DetectedFootprintSignal>>();
        if (completed == null || completed.Length == 0 || tickSize <= 0 || settings == null)
            return result;

        var n = completed.Length;
        var medAbsDelta = RollingMedian(completed, settings.SpeedMedianWindow, b => Math.Abs(b.Delta));
        var medVolume = RollingMedian(completed, settings.SpeedMedianWindow, b => b.Volume);
        var medRange = RollingMedian(completed, settings.SpeedMedianWindow, b => (decimal)(b.High - b.Low));
        var warmup = Math.Max(1, settings.SpeedMinBars);

        // Breakouts are evaluated first because fades reference them.
        var breakoutDirection = new int[n];  // +1 up, -1 down, 0 none
        var breakoutLevel = new double[n];   // prior range boundary that was crossed
        for (var i = 0; i < n; i++)
        {
            // No history yet -> no adaptive baseline to compare against.
            if (i < warmup || medAbsDelta[i] <= 0) continue;
            breakoutDirection[i] = ClassifyBreakout(
                completed, i, settings, medAbsDelta[i], medVolume[i], out breakoutLevel[i]);
        }

        // A fade is only tagged on the FIRST close back inside the prior range.
        var fadeSource = new int[n];
        var fadeDirection = new int[n];
        Array.Fill(fadeSource, -1);
        for (var j = 0; j < n; j++)
        {
            if (breakoutDirection[j] == 0) continue;

            for (var k = 1; k <= Math.Max(1, settings.FadeWindowBars) && j + k < n; k++)
            {
                var bar = completed[j + k];
                var reentered = breakoutDirection[j] > 0
                    ? bar.Close < breakoutLevel[j]
                    : bar.Close > breakoutLevel[j];
                if (!reentered) continue;

                fadeSource[j + k] = j;
                fadeDirection[j + k] = breakoutDirection[j];
                break;
            }
        }

        var lastEmitted = new Dictionary<FootprintSignalType, int>();

        void Emit(int i, FootprintSignalType type, string acronym, string tone)
        {
            if (lastEmitted.TryGetValue(type, out var last) &&
                i - last < Math.Max(1, settings.RepeatControlBars))
                return;

            var bar = completed[i];
            if (!result.TryGetValue(bar.Id, out var list))
                result[bar.Id] = list = new List<DetectedFootprintSignal>();

            list.Add(new DetectedFootprintSignal
            {
                Acronym = acronym,
                Tone = tone
            });
            lastEmitted[type] = i;
        }

        for (var i = 0; i < n; i++)
        {
            if (i < warmup || medAbsDelta[i] <= 0) continue;

            var bar = completed[i];
            var deltaFloor = (decimal)settings.DeltaFloorRatio * medAbsDelta[i];
            var range = bar.High - bar.Low;
            var lowerWickRatio = range > 0
                ? (Math.Min(bar.Open, bar.Close) - bar.Low) / range : 0;
            var upperWickRatio = range > 0
                ? (bar.High - Math.Max(bar.Open, bar.Close)) / range : 0;

            if (bar.Delta <= -deltaFloor &&
                lowerWickRatio >= settings.AbsorptionWickRatio &&
                HasExtremeImbalance(bar, bar.Low, tickSize, settings, aggressiveOnBid: true))
            {
                Emit(i, FootprintSignalType.SellAbsorption, "SAB", "bullish");
            }

            if (bar.Delta >= deltaFloor &&
                upperWickRatio >= settings.AbsorptionWickRatio &&
                HasExtremeImbalance(bar, bar.High, tickSize, settings, aggressiveOnBid: false))
            {
                Emit(i, FootprintSignalType.BuyAbsorption, "BAB", "bearish");
            }

            if (IsExhaustion(completed, i, settings, medRange[i], bullish: true))
                Emit(i, FootprintSignalType.SellExhaustion, "SXH", "bullish");
            if (IsExhaustion(completed, i, settings, medRange[i], bullish: false))
                Emit(i, FootprintSignalType.BuyExhaustion, "BXH", "bearish");

            if (breakoutDirection[i] > 0)
                Emit(i, FootprintSignalType.BreakoutConfirmation, "BRK", "bullish");
            else if (breakoutDirection[i] < 0)
                Emit(i, FootprintSignalType.BreakdownConfirmation, "BRD", "bearish");

            if (fadeSource[i] >= 0)
            {
                if (fadeDirection[i] > 0)
                    Emit(i, FootprintSignalType.FailedBreakout, "FBO", "bearish");
                else
                    Emit(i, FootprintSignalType.FailedBreakdown, "FBD", "bullish");
            }
        }

        return result;
    }

    private static int ClassifyBreakout(
        FootprintBarData[] bars,
        int i,
        SignalSettings settings,
        decimal medAbsDelta,
        decimal medVolume,
        out double level)
    {
        level = 0;
        var lookback = Math.Max(1, settings.BreakoutLookback);
        if (i < lookback) return 0;

        var bar = bars[i];
        var floor = (decimal)settings.DeltaFloorRatio * medAbsDelta;
        if (medVolume > 0 && bar.Volume < (decimal)settings.BreakoutMinVolumeRatio * medVolume)
            return 0;

        var priorHigh = double.MinValue;
        var priorLow = double.MaxValue;
        for (var j = i - lookback; j < i; j++)
        {
            if (bars[j].High > priorHigh) priorHigh = bars[j].High;
            if (bars[j].Low < priorLow) priorLow = bars[j].Low;
        }

        if (bar.Close > priorHigh && bar.Delta >= floor)
        {
            level = priorHigh;
            return 1;
        }
        if (bar.Close < priorLow && bar.Delta <= -floor)
        {
            level = priorLow;
            return -1;
        }
        return 0;
    }

    /// <summary>
    /// A buying/selling climax: the prior bar already had delta in that direction, and the
    /// current bar expands volume and range but closes near the extreme (effort without
    /// further progress). Tone is against the climax direction.
    /// </summary>
    private static bool IsExhaustion(
        FootprintBarData[] bars, int i, SignalSettings settings, decimal medRange, bool bullish)
    {
        if (i < 1 || medRange <= 0) return false;

        var bar = bars[i];
        var prev = bars[i - 1];
        var range = bar.High - bar.Low;
        if (range <= 0 || (decimal)range < (decimal)settings.ExhaustionRangeRatio * medRange)
            return false;
        if (bar.Volume < (decimal)settings.ExhaustionVolumeRatio * prev.Volume) return false;

        if (bullish)
        {
            // Selling climax near the low -> bullish exhaustion.
            if (prev.Delta >= 0 || bar.Delta >= 0 || bar.Close >= bar.Open) return false;
            var closeFromLow = (bar.Close - bar.Low) / range;
            return closeFromLow <= 0.4;
        }
        else
        {
            // Buying climax near the high -> bearish exhaustion.
            if (prev.Delta <= 0 || bar.Delta <= 0 || bar.Close <= bar.Open) return false;
            var closeFromLow = (bar.Close - bar.Low) / range;
            return closeFromLow >= 0.6;
        }
    }

    /// <summary>
    /// Confirms absorption at the rejected extreme: aggressive volume on the side the
    /// market tried to trade through must dominate the opposite side at that price.
    /// BidVolume is aggressive selling (trades hitting the bid), AskVolume aggressive buying.
    /// </summary>
    private static bool HasExtremeImbalance(
        FootprintBarData bar, double extremePrice, double tickSize, SignalSettings settings, bool aggressiveOnBid)
    {
        var levels = bar.Levels;
        if (levels == null || levels.Length == 0) return false;

        FootprintLevel? best = null;
        var bestDistance = double.MaxValue;
        foreach (var level in levels)
        {
            var distance = Math.Abs(level.Price - extremePrice);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = level;
        }
        if (best == null || bestDistance > tickSize * 0.5 + 1e-9) return false;

        var lvl = best.Value;
        var aggressive = aggressiveOnBid ? lvl.BidVolume : lvl.AskVolume;
        var passive = aggressiveOnBid ? lvl.AskVolume : lvl.BidVolume;
        // No fixed 1-contract floor here: it would break scale invariance on quiet sessions.
        var minVolume = (decimal)settings.ExtremeVolumeRatio * bar.Volume;
        return aggressive >= minVolume &&
               aggressive >= (decimal)settings.ImbalanceRatio * passive;
    }

    private static decimal[] RollingMedian(
        FootprintBarData[] bars, int window, Func<FootprintBarData, decimal> selector)
    {
        var result = new decimal[bars.Length];
        var size = Math.Max(1, window);
        var values = new List<decimal>(size);

        for (var i = 0; i < bars.Length; i++)
        {
            values.Clear();
            var from = Math.Max(0, i - size);
            for (var j = from; j < i; j++) values.Add(selector(bars[j]));
            result[i] = values.Count == 0 ? 0m : Median(values);
        }
        return result;
    }

    private static decimal Median(List<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2m;
    }
}
