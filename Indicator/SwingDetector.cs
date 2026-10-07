using System.Collections.Generic;

namespace Indicator;

/// <summary>
/// Confirmed N-bar pivot highs/lows (spec module C). A swing is confirmed <c>PivotRight</c> bars
/// after the pivot bar and stays active until a later bar trades through it.
/// </summary>
internal sealed class SwingDetector
{
    private readonly int _left;
    private readonly int _right;
    private readonly List<SwingPoint> _swings = new();

    public SwingDetector(int left, int right)
    {
        _left = System.Math.Max(1, left);
        _right = System.Math.Max(1, right);
    }

    public IReadOnlyList<SwingPoint> Swings => _swings;

    public void Rebuild(IReadOnlyList<Candle> bars)
    {
        _swings.Clear();
        var count = bars.Count;

        for (var i = _left; i + _right < count; i++)
        {
            var pivot = bars[i];
            var isHigh = true;
            var isLow = true;

            for (var j = i - _left; j <= i + _right && (isHigh || isLow); j++)
            {
                if (j == i) continue;
                if (bars[j].High >= pivot.High) isHigh = false;
                if (bars[j].Low <= pivot.Low) isLow = false;
            }

            if (isHigh) _swings.Add(new SwingPoint(pivot.Time, pivot.High, true));
            if (isLow) _swings.Add(new SwingPoint(pivot.Time, pivot.Low, false));
        }

        _swings.RemoveAll(s => IsBroken(s, bars));
    }

    private static bool IsBroken(SwingPoint swing, IReadOnlyList<Candle> bars)
    {
        for (var j = 0; j < bars.Count; j++)
        {
            if (bars[j].Time <= swing.Time) continue;
            if (swing.IsHigh ? bars[j].High > swing.Price : bars[j].Low < swing.Price)
                return true;
        }

        return false;
    }
}
