using System.Collections.Generic;

namespace Indicator;

/// <summary>
/// Candle coloring (spec module B). Priority: Sweep &gt; Absorption &gt; Volume Expansion &gt; neutral.
/// </summary>
internal static class SignalDetector
{
    private const double WickRatio = 0.5;

    public static SignalType Evaluate(
        Candle candle,
        double volumeSma,
        double averageSpread,
        double sessionHigh,
        double sessionLow,
        SessionLevels levels,
        IReadOnlyList<SwingPoint> swings,
        IndicatorOptions options)
    {
        if (volumeSma <= 0 || averageSpread <= 0) return SignalType.None;

        if (IsBullSweep(candle, levels, swings)) return SignalType.BullSweep;
        if (IsBearSweep(candle, levels, swings)) return SignalType.BearSweep;

        var range = candle.High - candle.Low;
        var bodyTop = System.Math.Max(candle.Open, candle.Close);
        var bodyBottom = System.Math.Min(candle.Open, candle.Close);
        var upperWick = candle.High - bodyTop;
        var lowerWick = bodyBottom - candle.Low;
        var upperRatio = range > 0 ? upperWick / range : 0;
        var lowerRatio = range > 0 ? lowerWick / range : 0;

        var highVolume = candle.Volume >= options.HighVolumeMultiplier * volumeSma;
        var narrowSpread = range <= options.NarrowSpreadMultiplier * averageSpread;

        var buffer = options.NearExtremeBuffer * averageSpread;
        var nearSessionLow = candle.Low <= sessionLow + buffer;
        var nearSessionHigh = candle.High >= sessionHigh - buffer;

        var bullAbsorption = highVolume && narrowSpread && (nearSessionLow || lowerRatio >= WickRatio);
        var bearAbsorption = highVolume && narrowSpread && (nearSessionHigh || upperRatio >= WickRatio);

        if (bullAbsorption && bearAbsorption)
            return lowerRatio >= upperRatio ? SignalType.BullAbsorption : SignalType.BearAbsorption;
        if (bullAbsorption) return SignalType.BullAbsorption;
        if (bearAbsorption) return SignalType.BearAbsorption;

        var expansion = candle.Volume >= options.UltraHighVolumeMultiplier * volumeSma
                        && range >= options.ExpansionSpreadMultiplier * averageSpread;
        if (expansion) return SignalType.VolumeExpansion;

        return SignalType.None;
    }

    private static bool IsBullSweep(Candle candle, SessionLevels levels, IReadOnlyList<SwingPoint> swings)
    {
        if (levels.PreviousDayLow is double pdl && !levels.PreviousDayLowBroken
            && candle.Low < pdl && candle.Close > pdl)
            return true;

        if (levels.OvernightLow is double onl && !levels.OvernightLowBroken
            && candle.Low < onl && candle.Close > onl)
            return true;

        foreach (var swing in swings)
        {
            if (swing.IsHigh) continue;
            if (candle.Low < swing.Price && candle.Close > swing.Price)
                return true;
        }

        return false;
    }

    private static bool IsBearSweep(Candle candle, SessionLevels levels, IReadOnlyList<SwingPoint> swings)
    {
        if (levels.PreviousDayHigh is double pdh && !levels.PreviousDayHighBroken
            && candle.High > pdh && candle.Close < pdh)
            return true;

        if (levels.OvernightHigh is double onh && !levels.OvernightHighBroken
            && candle.High > onh && candle.Close < onh)
            return true;

        foreach (var swing in swings)
        {
            if (!swing.IsHigh) continue;
            if (candle.High > swing.Price && candle.Close < swing.Price)
                return true;
        }

        return false;
    }
}
