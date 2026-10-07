using System;
using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class LiquiditySweepDetector : BarEngine
{
    private readonly double _minWickFraction;
    private SwingPoint? _lastSwingHigh;
    private SwingPoint? _lastSwingLow;
    private SweepBar? _activeSweep;

    private sealed record SweepBar(long Time, bool IsBullish, double High, double Low);

    public override string Name => "LiquiditySweep";

    public LiquiditySweepDetector(double minWickFraction)
    {
        _minWickFraction = Math.Clamp(minWickFraction, 0, 1);
    }

    public override void OnBar(BarContext context)
    {
        var candle = context.Candle;

        foreach (var swing in context.NewSwings)
        {
            if (swing.IsHigh) _lastSwingHigh = swing;
            else _lastSwingLow = swing;
        }

        if (_activeSweep is not null)
            UpdateLifecycle(_activeSweep, candle);

        if (_activeSweep is null)
        {
            var detected = Detect(candle);
            if (detected is not null)
            {
                _activeSweep = detected;
                Emit(Concept.LiquiditySweep, detected.IsBullish ? "Bullish" : "Bearish", candle.Time, "Detected");
            }
        }
    }

    private SweepBar? Detect(Candle candle)
    {
        var range = candle.High - candle.Low;
        if (range <= 0) return null;

        if (_lastSwingLow is not null &&
            candle.Low < _lastSwingLow.Price &&
            candle.Close > _lastSwingLow.Price)
        {
            var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
            if (lowerWick / range >= _minWickFraction)
                return new SweepBar(candle.Time, true, candle.High, candle.Low);
        }

        if (_lastSwingHigh is not null &&
            candle.High > _lastSwingHigh.Price &&
            candle.Close < _lastSwingHigh.Price)
        {
            var upperWick = candle.High - Math.Max(candle.Open, candle.Close);
            if (upperWick / range >= _minWickFraction)
                return new SweepBar(candle.Time, false, candle.High, candle.Low);
        }

        return null;
    }

    private void UpdateLifecycle(SweepBar sweep, Candle candle)
    {
        if (sweep.IsBullish)
        {
            if (candle.Close > sweep.High)
            {
                _activeSweep = null;
                Emit(Concept.LiquiditySweep, "Bullish", candle.Time, "Confirmed");
            }
            else if (candle.Close < sweep.Low)
            {
                _activeSweep = null;
                Emit(Concept.LiquiditySweep, "Bullish", candle.Time, "Faded");
            }
        }
        else
        {
            if (candle.Close < sweep.Low)
            {
                _activeSweep = null;
                Emit(Concept.LiquiditySweep, "Bearish", candle.Time, "Confirmed");
            }
            else if (candle.Close > sweep.High)
            {
                _activeSweep = null;
                Emit(Concept.LiquiditySweep, "Bearish", candle.Time, "Faded");
            }
        }
    }
}