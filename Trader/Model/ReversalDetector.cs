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
    private readonly double _minRange;
    private readonly int _lookBack;
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

    public ReversalDetector(double minWickRatio = 0.35, double minRange = 0.50, int lookBack = 20)
    {
        _minWickRatio = minWickRatio;
        _minRange = minRange;
        _lookBack = Math.Max(2, lookBack);
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
                if (upperWick > lowerWick && close <= low + range * 0.5)
                    deltaProxy = -Math.Max(1, volume * 0.5);
                else if (lowerWick > upperWick && close >= low + range * 0.5)
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
                while (_history.Count > _lookBack + 5)
                    _history.RemoveAt(0);
            }
        }
    }

    private string EvaluateInternal(long timeSeconds, double open, double high, double low, double close, double volume, double delta)
    {
        if (_history.Count == 0) return DefaultColor;

        var prev = _history[^1];
        if (prev.TimeSeconds == timeSeconds)
        {
            if (_history.Count < 2) return DefaultColor;
            prev = _history[^2];
        }

        var range = high - low;
        if (range < _minRange) return DefaultColor;

        var upperWick = high - Math.Max(open, close);
        var lowerWick = Math.Min(open, close) - low;

        var upperWickRatio = upperWick / range;
        var lowerWickRatio = lowerWick / range;

        // Bearish Reversal:
        // 1. Tested or exceeded previous high
        // 2. Significant upper rejection wick (>= 35% of total bar range)
        // 3. Closed in lower half of the candle
        // 4. Delta Divergence: negative delta (sellers dominated/absorbed at the top)
        bool isBearishReversal = (high >= prev.High - 0.25) &&
                                 (upperWickRatio >= _minWickRatio) &&
                                 (close <= low + range * 0.50) &&
                                 (delta < 0);

        if (isBearishReversal)
        {
            return BearishReversalColor;
        }

        // Bullish Reversal:
        // 1. Tested or dropped below previous low
        // 2. Significant lower rejection wick (>= 35% of total bar range)
        // 3. Closed in upper half of the candle
        // 4. Delta Divergence: positive delta (buyers dominated/absorbed at the bottom)
        bool isBullishReversal = (low <= prev.Low + 0.25) &&
                                 (lowerWickRatio >= _minWickRatio) &&
                                 (close >= low + range * 0.50) &&
                                 (delta > 0);

        if (isBullishReversal)
        {
            return UseDirectionalColors ? BullishReversalColor : BearishReversalColor;
        }

        return DefaultColor;
    }
}
