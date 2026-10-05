using System;
using System.Collections.Generic;

namespace Trader.Model;

public class ReversalDetector
{
    public const string BearishReversalColor = "#ef5350"; // Red
    public const string BullishReversalColor = "#26a69a"; // Green
    public const string DefaultColor = "#808080";

    /// <summary>
    /// If true, bullish reversal is green and bearish is red.
    /// If false, both are highlighted red.
    /// </summary>
    public static bool UseDirectionalColors { get; set; } = true;

    private readonly double _minWickRatio;
    private readonly double _minAbsoluteRange;
    private readonly int _swingLookback;
    private readonly int _statsLookback;
    private readonly object _lock = new();

    private readonly List<CandleSnapshot> _history = new();

    public record struct CandleSnapshot(
        long TimeSeconds,
        double Open,
        double High,
        double Low,
        double Close,
        double Volume,
        double Delta
    );

    public ReversalDetector(
        double minWickRatio = 0.50,
        double minAbsoluteRange = 1.00,
        int swingLookback = 6,
        int statsLookback = 20)
    {
        _minWickRatio = minWickRatio;
        _minAbsoluteRange = minAbsoluteRange;
        _swingLookback = Math.Max(2, swingLookback);
        _statsLookback = Math.Max(5, statsLookback);
    }

    /// <summary>
    /// Processes a historical candle into history.
    /// Returns the color if it was a reversal candle.
    /// </summary>
    public string ProcessHistoricalCandle(long timeSeconds, double open, double high, double low, double close, double volume)
    {
        lock (_lock)
        {
            var range = high - low;
            double deltaProxy = 0;
            if (range > 0)
            {
                var upperWick = high - Math.Max(open, close);
                var lowerWick = Math.Min(open, close) - low;
                if (upperWick >= range * 0.50 && close <= low + range * 0.35)
                    deltaProxy = -Math.Max(1, volume * 0.5);
                else if (lowerWick >= range * 0.50 && close >= low + range * 0.65)
                    deltaProxy = Math.Max(1, volume * 0.5);
            }

            var color = EvaluateInternal(timeSeconds, open, high, low, close, volume, deltaProxy);
            OnCandleCompleted(timeSeconds, open, high, low, close, volume, deltaProxy);
            return color;
        }
    }

    /// <summary>
    /// Evaluates the currently forming real-time candle against historical context.
    /// </summary>
    public string EvaluateCandle(long timeSeconds, double open, double high, double low, double close, double volume, double delta)
    {
        lock (_lock)
        {
            return EvaluateInternal(timeSeconds, open, high, low, close, volume, delta);
        }
    }

    public void OnCandleCompleted(long timeSeconds, double open, double high, double low, double close, double volume, double delta)
    {
        lock (_lock)
        {
            if (_history.Count > 0 && _history[^1].TimeSeconds == timeSeconds)
            {
                _history[^1] = new CandleSnapshot(timeSeconds, open, high, low, close, volume, delta);
            }
            else
            {
                _history.Add(new CandleSnapshot(timeSeconds, open, high, low, close, volume, delta));
                int maxKeep = Math.Max(_statsLookback, _swingLookback) + 20;
                while (_history.Count > maxKeep)
                    _history.RemoveAt(0);
            }
        }
    }

    private string EvaluateInternal(long timeSeconds, double open, double high, double low, double close, double volume, double delta)
    {
        int count = _history.Count;
        if (count > 0 && _history[^1].TimeSeconds == timeSeconds)
            count--;

        if (count < _swingLookback) return DefaultColor;

        var range = high - low;
        if (range < _minAbsoluteRange) return DefaultColor;

        // Volatility & volume filters relative to recent bars
        int statsStart = Math.Max(0, count - _statsLookback);
        int statsCount = count - statsStart;
        double sumRange = 0;
        double sumVolume = 0;
        for (int i = statsStart; i < count; i++)
        {
            var h = _history[i];
            sumRange += (h.High - h.Low);
            sumVolume += h.Volume;
        }
        var avgRange = sumRange / statsCount;
        if (range < avgRange * 0.75) return DefaultColor;

        var avgVolume = sumVolume / statsCount;
        if (volume > 0 && avgVolume > 0 && volume < avgVolume * 0.70) return DefaultColor;

        // Swing extremum check over last _swingLookback bars
        int swingStart = Math.Max(0, count - _swingLookback);
        double maxPriorHigh = double.MinValue;
        double minPriorLow = double.MaxValue;
        for (int i = swingStart; i < count; i++)
        {
            var h = _history[i];
            if (h.High > maxPriorHigh) maxPriorHigh = h.High;
            if (h.Low < minPriorLow) minPriorLow = h.Low;
        }

        var upperWick = high - Math.Max(open, close);
        var lowerWick = Math.Min(open, close) - low;

        var upperWickRatio = upperWick / range;
        var lowerWickRatio = lowerWick / range;

        // Bearish Reversal:
        // 1. Tested or exceeded highest high of last N bars
        // 2. Strong upper rejection wick (>= 50% of range)
        // 3. Closed in lower 35% of the candle
        // 4. Delta Divergence: negative delta (sellers absorbed/dominated the high)
        bool isBearishReversal = (high >= maxPriorHigh - 0.25) &&
                                 (upperWickRatio >= _minWickRatio) &&
                                 (close <= low + range * 0.35) &&
                                 (delta < 0);

        if (isBearishReversal)
        {
            return BearishReversalColor;
        }

        // Bullish Reversal:
        // 1. Tested or broken lowest low of last N bars
        // 2. Strong lower rejection wick (>= 50% of range)
        // 3. Closed in upper 35% of the candle
        // 4. Delta Divergence: positive delta (buyers absorbed/dominated the low)
        bool isBullishReversal = (low <= minPriorLow + 0.25) &&
                                 (lowerWickRatio >= _minWickRatio) &&
                                 (close >= low + range * 0.65) &&
                                 (delta > 0);

        if (isBullishReversal)
        {
            return UseDirectionalColors ? BullishReversalColor : BearishReversalColor;
        }

        return DefaultColor;
    }
}
