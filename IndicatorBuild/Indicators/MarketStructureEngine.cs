using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class MarketStructureEngine : BarEngine
{
    private SwingPoint? _lastHigh;
    private SwingPoint? _lastLow;
    private bool _upBroken;
    private bool _downBroken;

    public override string Name => "MarketStructure";

    public TrendState Trend { get; private set; } = TrendState.None;

    public override void OnBar(BarContext context)
    {
        var candle = context.Candle;

        foreach (var swing in context.NewSwings)
        {
            if (swing.IsHigh)
            {
                _lastHigh = swing;
                _upBroken = false;
            }
            else
            {
                _lastLow = swing;
                _downBroken = false;
            }
        }

        if (_lastHigh is null || _lastLow is null)
            return;

        if (Trend == TrendState.None)
        {
            Trend = _lastHigh.Index > _lastLow.Index ? TrendState.Bullish : TrendState.Bearish;
            return;
        }

        if (Trend == TrendState.Bullish)
        {
            if (!_upBroken && candle.Close > _lastHigh.Price)
            {
                _upBroken = true;
                Emit(Concept.BOS, "Bullish", candle.Time, $"Above {_lastHigh.Price:F2}");
            }
            else if (!_downBroken && candle.Close < _lastLow.Price)
            {
                _downBroken = true;
                Trend = TrendState.Bearish;
                Emit(Concept.ChoCh, "Bearish", candle.Time, $"Below {_lastLow.Price:F2}");
            }
        }
        else
        {
            if (!_downBroken && candle.Close < _lastLow.Price)
            {
                _downBroken = true;
                Emit(Concept.BOS, "Bearish", candle.Time, $"Below {_lastLow.Price:F2}");
            }
            else if (!_upBroken && candle.Close > _lastHigh.Price)
            {
                _upBroken = true;
                Trend = TrendState.Bullish;
                Emit(Concept.ChoCh, "Bullish", candle.Time, $"Above {_lastHigh.Price:F2}");
            }
        }
    }
}