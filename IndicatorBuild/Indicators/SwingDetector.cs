using System;
using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class SwingDetector
{
    private readonly int _left;
    private readonly int _right;
    private readonly int _window;
    private readonly List<CandleBar> _buffer = new();
    private readonly List<SwingPoint> _swings = new();
    private int _index;

    private sealed record CandleBar(Candle Candle, int Index);

    public SwingDetector(int left = 5, int right = 5)
    {
        _left = Math.Max(1, left);
        _right = Math.Max(1, right);
        _window = _left + _right + 1;
    }

    public IReadOnlyList<SwingPoint> Swings => _swings;

    public void ProcessBar(Candle candle) => ProcessBar(candle, _index++);

    public void ProcessBar(Candle candle, int index)
    {
        _buffer.Add(new CandleBar(candle, index));

        while (_buffer.Count >= _window)
        {
            var pivot = _buffer[_left];

            var isHigh = true;
            var isLow = true;
            foreach (var bar in _buffer)
            {
                if (bar.Index == pivot.Index) continue;
                if (bar.Candle.High >= pivot.Candle.High) isHigh = false;
                if (bar.Candle.Low <= pivot.Candle.Low) isLow = false;
            }

            if (isHigh) _swings.Add(new SwingPoint(pivot.Candle.Time, pivot.Candle.High, true, pivot.Index));
            if (isLow) _swings.Add(new SwingPoint(pivot.Candle.Time, pivot.Candle.Low, false, pivot.Index));

            _buffer.RemoveAt(0);
        }
    }
}