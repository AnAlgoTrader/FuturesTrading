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
    Slowdown             // SLW - Phase
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

public static class FootprintSignalDetector
{
    /// <summary>
    /// Scans completed footprint bars in chronological order and detects the 5 signals
    /// specified in the Footprint Signal Detector specification.
    /// Returns a dictionary mapping FootprintBarData.Id to the list of detected signals for that bar.
    /// </summary>
    public static Dictionary<long, List<DetectedFootprintSignal>> DetectSignals(
        FootprintBarData[] completed,
        double tickSize,
        SignalSettings settings)
    {
        var result = new Dictionary<long, List<DetectedFootprintSignal>>();
        if (completed == null || completed.Length == 0) return result;

        settings ??= new SignalSettings();
        var n = completed.Length;

        // Precompute CVD, durations, and buy imbalances for every completed bar
        var cvdArray = new decimal[n];
        var durations = new double[n];
        var buyImbalances = new int[n];

        decimal runningCvd = 0m;
        for (var i = 0; i < n; i++)
        {
            var b = completed[i];
            runningCvd += b.Delta;
            cvdArray[i] = runningCvd;
            durations[i] = b.DurationSeconds > 0
                ? Math.Max(0.001, b.DurationSeconds)            // local arrival clock, sub-ms
                : Math.Max(1.0, b.EndTime - b.StartTime);       // fallback: IBKR 1-second timestamps
            buyImbalances[i] = CountBuyImbalances(b, tickSize, settings.ImbalanceMinVolume, settings.ImbalanceRatio);
        }

        // Repeat control tracking: maps signal type to last bar index it fired on
        var lastFired = new Dictionary<FootprintSignalType, int>();

        for (var i = 0; i < n; i++)
        {
            var bar = completed[i];
            var signals = new List<DetectedFootprintSignal>();

            // 1. Swing High & Swing Low (previous 10 bars, not including current bar)
            if (i >= settings.SwingLookback)
            {
                var swingLow = double.MaxValue;
                var swingHigh = double.MinValue;
                for (var k = i - settings.SwingLookback; k < i; k++)
                {
                    if (completed[k].Low < swingLow) swingLow = completed[k].Low;
                    if (completed[k].High > swingHigh) swingHigh = completed[k].High;
                }

                var tolerance = settings.ToleranceTicks * tickSize + 1e-9;

                // Signal 1: Sell absorption (bullish) - SAB
                // Fires when delta is -250 or lower, and low did not break swing low by more than 1 tick.
                if (bar.Delta <= -settings.MinDeltaAbsorption && bar.Low >= swingLow - tolerance)
                {
                    if (!lastFired.TryGetValue(FootprintSignalType.SellAbsorption, out var lastIdx) || (i - lastIdx > settings.RepeatControlBars))
                    {
                        signals.Add(new DetectedFootprintSignal
                        {
                            Type = FootprintSignalType.SellAbsorption,
                            Acronym = "SAB",
                            Name = "Sell Absorption",
                            Tone = "bullish",
                            BarStartTime = bar.StartTime,
                            BarId = bar.Id
                        });
                        lastFired[FootprintSignalType.SellAbsorption] = i;
                    }
                }

                // Signal 2: Buy absorption (bearish) - BAB
                // Fires when delta is +250 or higher, and high did not exceed swing high by more than 1 tick.
                if (bar.Delta >= settings.MinDeltaAbsorption && bar.High <= swingHigh + tolerance)
                {
                    if (!lastFired.TryGetValue(FootprintSignalType.BuyAbsorption, out var lastIdx) || (i - lastIdx > settings.RepeatControlBars))
                    {
                        signals.Add(new DetectedFootprintSignal
                        {
                            Type = FootprintSignalType.BuyAbsorption,
                            Acronym = "BAB",
                            Name = "Buy Absorption",
                            Tone = "bearish",
                            BarStartTime = bar.StartTime,
                            BarId = bar.Id
                        });
                        lastFired[FootprintSignalType.BuyAbsorption] = i;
                    }
                }
            }

            // 2. CVD Divergence (bearish) - CDIV (previous 15 bars)
            if (i >= settings.CvdDivergenceLookback)
            {
                var priorMaxCvd = decimal.MinValue;
                var priorMaxHigh = double.MinValue;
                for (var k = i - settings.CvdDivergenceLookback; k < i; k++)
                {
                    if (cvdArray[k] > priorMaxCvd) priorMaxCvd = cvdArray[k];
                    if (completed[k].High > priorMaxHigh) priorMaxHigh = completed[k].High;
                }

                // Bar's CVD is above every one of prior 15 bars, but bar's high is below highest high of those 15 bars
                if (cvdArray[i] > priorMaxCvd && bar.High < priorMaxHigh)
                {
                    if (!lastFired.TryGetValue(FootprintSignalType.CvdDivergence, out var lastIdx) || (i - lastIdx > settings.RepeatControlBars))
                    {
                        signals.Add(new DetectedFootprintSignal
                        {
                            Type = FootprintSignalType.CvdDivergence,
                            Acronym = "CDIV",
                            Name = "CVD Divergence",
                            Tone = "bearish",
                            BarStartTime = bar.StartTime,
                            BarId = bar.Id
                        });
                        lastFired[FootprintSignalType.CvdDivergence] = i;
                    }
                }
            }

            // 3. Speed Median Calculation (requires at least 8 earlier bars, up to 20 bars lookback)
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

            // 4. Breakout Confirmation (bullish) - BRK (previous 10 bars)
            if (i >= settings.BreakoutLookback)
            {
                var priorMaxHigh = double.MinValue;
                for (var k = i - settings.BreakoutLookback; k < i; k++)
                {
                    if (completed[k].High > priorMaxHigh) priorMaxHigh = completed[k].High;
                }

                // Close above highest high of prior 10 bars, delta >= 100, buy imbalances >= 3
                if (bar.Close > priorMaxHigh &&
                    bar.Delta >= settings.MinDeltaBreakout &&
                    buyImbalances[i] >= settings.MinBuyImbalancesBreakout)
                {
                    if (!lastFired.TryGetValue(FootprintSignalType.BreakoutConfirmation, out var lastIdx) || (i - lastIdx > settings.RepeatControlBars))
                    {
                        var isFast = medianSeconds.HasValue && durations[i] < settings.FastThresholdRatio * medianSeconds.Value;
                        signals.Add(new DetectedFootprintSignal
                        {
                            Type = FootprintSignalType.BreakoutConfirmation,
                            Acronym = isFast ? "BRK ⚡F" : "BRK",
                            Name = isFast ? "Breakout Confirmation (FAST)" : "Breakout Confirmation",
                            Tone = "bullish",
                            IsFast = isFast,
                            BarStartTime = bar.StartTime,
                            BarId = bar.Id
                        });
                        lastFired[FootprintSignalType.BreakoutConfirmation] = i;
                    }
                }
            }

            // 5. Slowdown (phase indicator) - SLW
            // Average seconds of last 3 bars > 1.5 * median of previous 20 bars
            if (i >= Math.Max(2, settings.SpeedMinBars) && medianSeconds.HasValue)
            {
                var avg3 = (durations[i] + durations[i - 1] + durations[i - 2]) / 3.0;
                if (avg3 > settings.SlowdownThresholdRatio * medianSeconds.Value)
                {
                    if (!lastFired.TryGetValue(FootprintSignalType.Slowdown, out var lastIdx) || (i - lastIdx > settings.RepeatControlBars))
                    {
                        signals.Add(new DetectedFootprintSignal
                        {
                            Type = FootprintSignalType.Slowdown,
                            Acronym = "SLW",
                            Name = "Slowdown",
                            Tone = "phase",
                            BarStartTime = bar.StartTime,
                            BarId = bar.Id
                        });
                        lastFired[FootprintSignalType.Slowdown] = i;
                    }
                }
            }

            if (signals.Count > 0)
            {
                result[bar.Id] = signals;
            }
        }

        return result;
    }

    /// <summary>
    /// Counts diagonal buy imbalances for a bar:
    /// For each price row, ask volume >= minVolume and ask volume >= ratio * bid volume one tick lower.
    /// </summary>
    public static int CountBuyImbalances(FootprintBarData bar, double tickSize, decimal minVolume, decimal ratio)
    {
        var levels = bar.Levels;
        if (levels == null || levels.Length < 2) return 0;

        var dict = new Dictionary<long, FootprintLevel>(levels.Length);
        foreach (var lvl in levels)
        {
            var key = (long)Math.Round(lvl.Price / tickSize, MidpointRounding.AwayFromZero);
            dict[key] = lvl;
        }

        var count = 0;
        foreach (var lvl in levels)
        {
            var ask = lvl.AskVolume;
            if (ask < minVolume) continue;

            var lowerKey = (long)Math.Round(lvl.Price / tickSize, MidpointRounding.AwayFromZero) - 1;
            var bidLower = dict.TryGetValue(lowerKey, out var lowerLvl) ? lowerLvl.BidVolume : 0m;

            if (bidLower == 0m || ask >= bidLower * ratio)
            {
                count++;
            }
        }

        return count;
    }
}
