using System;
using System.Collections.Generic;
using FootPrintTrader.Configuration;

namespace FootPrintTrader.Domain.Footprint;

public enum FootprintSignalType
{
    SellAbsorption,      // SAB - Bullish
    BuyAbsorption,       // BAB - Bearish
    CvdDivergence,       // CDIV - Bearish
    BreakoutConfirmation,// BRK - Bullish
    Slowdown,            // SLW - Phase
    CvdBullishDivergence // CDIV▲ - Bullish (mirror of CDIV). Appended last so existing numeric values are unchanged.
}

public class DetectedFootprintSignal
{
    public FootprintSignalType Type { get; init; }
    public string Acronym { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tone { get; init; } = "neutral"; // "bullish", "bearish", "phase"
    public bool IsFast { get; init; }
    public long BarStartTime { get; init; }
    public long BarId { get; init; }
}

/// <summary>
/// Extra tunables introduced with the signal-logic revision. They live here (with defaults) so the
/// existing <c>SignalSettings</c> class does not have to change; move them there whenever convenient.
/// To reproduce the previous behaviour exactly: AbsorptionProximityTicks = int.MaxValue,
/// MinCloseLocation = 0, MinDeltaPctAbsorption = 0, EnableBullishCvdDivergence = false,
/// ExcludeBottomRowImbalance = false.
/// </summary>
public class SignalTuning
{
    /// <summary>SAB/BAB: the bar's extreme must be within this many ticks of the swing extreme
    /// (it may also overshoot it by SignalSettings.ToleranceTicks). Fixes the old one-sided check.</summary>
    public int AbsorptionProximityTicks { get; set; } = 4;

    /// <summary>SAB/BAB price response. SAB: (close-low)/range must be >= this; BAB: (high-close)/range.
    /// 0.5 = the close must not finish in the half of the bar in the direction of the aggressive flow.</summary>
    public double MinCloseLocation { get; set; } = 0.5;

    /// <summary>SAB/BAB: |delta| / volume must be >= this (volume-normalised, in addition to the absolute minimum).</summary>
    public double MinDeltaPctAbsorption { get; set; } = 0.08;

    /// <summary>Emit the mirror of CDIV (CVD makes a new low, price does not).</summary>
    public bool EnableBullishCvdDivergence { get; set; } = true;

    /// <summary>Do not count the lowest row of a bar as an imbalance (there is no row below it to compare with).</summary>
    public bool ExcludeBottomRowImbalance { get; set; } = true;
}

public static class FootprintSignalDetector
{
    private const double Eps = 1e-9;

    /// <summary>
    /// Scans completed footprint bars in chronological order and detects the signals
    /// specified in the Footprint Signal Detector specification.
    /// Returns a dictionary mapping FootprintBarData.Id to the list of detected signals for that bar.
    /// </summary>
    public static Dictionary<long, List<DetectedFootprintSignal>> DetectSignals(
        FootprintBarData[] completed,
        double tickSize,
        SignalSettings settings,
        SignalTuning tuning = null)
    {
        var result = new Dictionary<long, List<DetectedFootprintSignal>>();
        if (completed == null || completed.Length == 0) return result;

        settings ??= new SignalSettings();
        tuning ??= new SignalTuning();
        var n = completed.Length;

        // Precompute per-bar values once (Delta / Volume are LINQ sums over the levels).
        var cvdArray = new decimal[n];
        var deltas = new decimal[n];
        var volumes = new decimal[n];
        var durations = new double[n];
        var buyImbalances = new int[n];

        decimal runningCvd = 0m;
        for (var i = 0; i < n; i++)
        {
            var b = completed[i];
            deltas[i] = b.Delta;
            volumes[i] = b.Volume;
            runningCvd += deltas[i];
            cvdArray[i] = runningCvd;
            durations[i] = b.DurationSeconds > 0
                ? Math.Max(0.001, b.DurationSeconds)            // local arrival clock, sub-ms
                : Math.Max(1.0, b.EndTime - b.StartTime);       // fallback: IBKR 1-second timestamps
            buyImbalances[i] = CountBuyImbalances(b, tickSize, settings.ImbalanceMinVolume, settings.ImbalanceRatio,
                                                  tuning.ExcludeBottomRowImbalance);
        }

        // Repeat control tracking: maps signal type to last bar index it fired on
        var lastFired = new Dictionary<FootprintSignalType, int>();

        bool CanFire(FootprintSignalType t, int idx) =>
            !lastFired.TryGetValue(t, out var last) || (idx - last > settings.RepeatControlBars);

        for (var i = 0; i < n; i++)
        {
            var bar = completed[i];
            var delta = deltas[i];
            var signals = new List<DetectedFootprintSignal>();

            DetectedFootprintSignal Make(FootprintSignalType type, string acronym, string name, string tone, bool fast = false)
            {
                lastFired[type] = i;
                return new DetectedFootprintSignal
                {
                    Type = type, Acronym = acronym, Name = name, Tone = tone,
                    IsFast = fast, BarStartTime = bar.StartTime, BarId = bar.Id
                };
            }

            // 1. Swing High & Swing Low (previous N bars, not including current bar)
            if (i >= settings.SwingLookback)
            {
                var swingLow = double.MaxValue;
                var swingHigh = double.MinValue;
                for (var k = i - settings.SwingLookback; k < i; k++)
                {
                    if (completed[k].Low < swingLow) swingLow = completed[k].Low;
                    if (completed[k].High > swingHigh) swingHigh = completed[k].High;
                }

                var tolerance = settings.ToleranceTicks * tickSize + Eps;
                var proximity = tuning.AbsorptionProximityTicks >= int.MaxValue / 2
                    ? double.MaxValue
                    : tuning.AbsorptionProximityTicks * tickSize + Eps;

                var range = bar.High - bar.Low;
                var vol = volumes[i];
                var absDeltaPct = vol > 0 ? (double)(Math.Abs(delta) / vol) : 0.0;
                var deltaPctOk = absDeltaPct >= tuning.MinDeltaPctAbsorption;

                // Signal 1: Sell absorption (bullish) - SAB
                // Heavy net selling, the low sits AT the swing low (within proximity above it, or no more than
                // tolerance below it), and price held: the bar did not close in its lower part.
                if (delta <= -settings.MinDeltaAbsorption && deltaPctOk &&
                    bar.Low >= swingLow - tolerance &&
                    bar.Low <= swingLow + proximity)
                {
                    var closeLoc = range > Eps ? (bar.Close - bar.Low) / range : 1.0;
                    if (closeLoc >= tuning.MinCloseLocation && CanFire(FootprintSignalType.SellAbsorption, i))
                        signals.Add(Make(FootprintSignalType.SellAbsorption, "SAB", "Sell Absorption", "bullish"));
                }

                // Signal 2: Buy absorption (bearish) - BAB (mirror image)
                if (delta >= settings.MinDeltaAbsorption && deltaPctOk &&
                    bar.High <= swingHigh + tolerance &&
                    bar.High >= swingHigh - proximity)
                {
                    var closeLoc = range > Eps ? (bar.High - bar.Close) / range : 1.0;
                    if (closeLoc >= tuning.MinCloseLocation && CanFire(FootprintSignalType.BuyAbsorption, i))
                        signals.Add(Make(FootprintSignalType.BuyAbsorption, "BAB", "Buy Absorption", "bearish"));
                }
            }

            // 2. CVD Divergence (previous N bars)
            if (i >= settings.CvdDivergenceLookback)
            {
                var priorMaxCvd = decimal.MinValue;
                var priorMinCvd = decimal.MaxValue;
                var priorMaxHigh = double.MinValue;
                var priorMinLow = double.MaxValue;
                for (var k = i - settings.CvdDivergenceLookback; k < i; k++)
                {
                    if (cvdArray[k] > priorMaxCvd) priorMaxCvd = cvdArray[k];
                    if (cvdArray[k] < priorMinCvd) priorMinCvd = cvdArray[k];
                    if (completed[k].High > priorMaxHigh) priorMaxHigh = completed[k].High;
                    if (completed[k].Low < priorMinLow) priorMinLow = completed[k].Low;
                }

                // Bearish CDIV: CVD above every prior bar's CVD, but the high is below the highest prior high.
                if (cvdArray[i] > priorMaxCvd && bar.High < priorMaxHigh &&
                    CanFire(FootprintSignalType.CvdDivergence, i))
                {
                    signals.Add(Make(FootprintSignalType.CvdDivergence, "CDIV", "CVD Divergence", "bearish"));
                }

                // Bullish mirror: CVD below every prior bar's CVD, but the low is above the lowest prior low.
                if (tuning.EnableBullishCvdDivergence &&
                    cvdArray[i] < priorMinCvd && bar.Low > priorMinLow &&
                    CanFire(FootprintSignalType.CvdBullishDivergence, i))
                {
                    signals.Add(Make(FootprintSignalType.CvdBullishDivergence, "CDIV▲", "CVD Bullish Divergence", "bullish"));
                }
            }

            // 3. Speed median (needs SpeedMinBars earlier bars, up to SpeedMedianWindow lookback)
            double? medianSeconds = null;
            if (i >= settings.SpeedMinBars)
            {
                var win = Math.Min(settings.SpeedMedianWindow, i);
                var speedSample = new double[win];
                for (var k = 0; k < win; k++)
                    speedSample[k] = durations[i - win + k];
                Array.Sort(speedSample);
                medianSeconds = (win % 2 == 1)
                    ? speedSample[win / 2]
                    : (speedSample[win / 2 - 1] + speedSample[win / 2]) / 2.0;
            }

            // 4. Breakout Confirmation (bullish) - BRK
            if (i >= settings.BreakoutLookback)
            {
                var priorMaxHigh = double.MinValue;
                for (var k = i - settings.BreakoutLookback; k < i; k++)
                {
                    if (completed[k].High > priorMaxHigh) priorMaxHigh = completed[k].High;
                }

                if (bar.Close > priorMaxHigh &&
                    delta >= settings.MinDeltaBreakout &&
                    buyImbalances[i] >= settings.MinBuyImbalancesBreakout &&
                    CanFire(FootprintSignalType.BreakoutConfirmation, i))
                {
                    var isFast = medianSeconds.HasValue && durations[i] < settings.FastThresholdRatio * medianSeconds.Value;
                    signals.Add(Make(FootprintSignalType.BreakoutConfirmation,
                        isFast ? "BRK ⚡F" : "BRK",
                        isFast ? "Breakout Confirmation (FAST)" : "Breakout Confirmation",
                        "bullish", isFast));
                }
            }

            // 5. Slowdown (phase indicator) - SLW
            // Average seconds of last 3 bars > ratio * median of previous bars
            if (i >= Math.Max(2, settings.SpeedMinBars) && medianSeconds.HasValue)
            {
                var avg3 = (durations[i] + durations[i - 1] + durations[i - 2]) / 3.0;
                if (avg3 > settings.SlowdownThresholdRatio * medianSeconds.Value &&
                    CanFire(FootprintSignalType.Slowdown, i))
                {
                    signals.Add(Make(FootprintSignalType.Slowdown, "SLW", "Slowdown", "phase"));
                }
            }

            if (signals.Count > 0)
                result[bar.Id] = signals;
        }

        return result;
    }

    /// <summary>
    /// Counts diagonal buy imbalances for a bar:
    /// For each price row, ask volume >= minVolume and ask volume >= ratio * bid volume one tick lower.
    /// The lowest row has no row below it inside the bar, so (optionally) it is not counted.
    /// </summary>
    public static int CountBuyImbalances(FootprintBarData bar, double tickSize, decimal minVolume, decimal ratio,
                                         bool excludeBottomRow = true)
    {
        var levels = bar.Levels;
        if (levels == null || levels.Length < 2) return 0;

        var dict = new Dictionary<long, FootprintLevel>(levels.Length);
        var minKey = long.MaxValue;
        foreach (var lvl in levels)
        {
            var key = (long)Math.Round(lvl.Price / tickSize, MidpointRounding.AwayFromZero);
            dict[key] = lvl;
            if (key < minKey) minKey = key;
        }

        var count = 0;
        foreach (var lvl in levels)
        {
            var ask = lvl.AskVolume;
            if (ask < minVolume) continue;

            var lowerKey = (long)Math.Round(lvl.Price / tickSize, MidpointRounding.AwayFromZero) - 1;
            if (excludeBottomRow && lowerKey < minKey) continue;

            var bidLower = dict.TryGetValue(lowerKey, out var lowerLvl) ? lowerLvl.BidVolume : 0m;

            if (bidLower == 0m || ask >= bidLower * ratio)
                count++;
        }

        return count;
    }
}
