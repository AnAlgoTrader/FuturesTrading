using System;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class PullbackDetector : BarEngine
{
    private readonly double _legMinAtr;
    private readonly double _healthyMin;
    private readonly double _healthyMax;

    private SwingPoint? _lastHigh;
    private SwingPoint? _lastLow;
    private Leg? _leg;
    private PendingCounter? _pending;
    private double _resumeLevel;

    private sealed record Leg(bool IsUp, double Top, double Bottom, long Time);

    private sealed record PendingCounter(long Time, bool LegUp, double Depth);

    public override string Name => "Pullback";

    public PullbackDetector(double legMinAtr = 1.0, double healthyMin = 0.382, double healthyMax = 0.618)
    {
        _legMinAtr = legMinAtr;
        _healthyMin = healthyMin;
        _healthyMax = healthyMax;
    }

    public override void OnBar(BarContext context)
    {
        var candle = context.Candle;
        var atr = context.Atr ?? 0;

        if (_pending is not null)
            TrackResumption(candle);

        foreach (var swing in context.NewSwings)
        {
            if (swing.IsHigh)
                ProcessHigh(swing, atr, candle);
            else
                ProcessLow(swing, atr, candle);
        }
    }

    private void TrackResumption(Candle candle)
    {
        var pending = _pending;
        if (pending is null) return;

        if (pending.LegUp)
        {
            if (candle.Close > _resumeLevel)
                ConfirmPullback(candle.Time, true, pending.Depth);
        }
        else
        {
            if (candle.Close < _resumeLevel)
                ConfirmPullback(candle.Time, false, pending.Depth);
        }
    }

    private void ProcessHigh(SwingPoint s, double atr, Candle candle)
    {
        if (_lastLow is not null)
        {
            if (_leg is null)
            {
                if (IsMeaningful(_lastLow.Price, s.Price, atr))
                    _leg = new Leg(true, s.Price, _lastLow.Price, s.Time);
            }
            else if (_leg.IsUp)
            {
                _leg = _leg with { Top = Math.Max(_leg.Top, s.Price), Bottom = _lastLow.Price };
            }
            else
            {
                // Counter-move against a down impulse
                var depth = RetraceDepthIntoDownLeg(s.Price);
                if (depth > 0 && depth < 1)
                {
                    Emit(Concept.Retracement, "Bearish", s.Time, $"retrace {depth:0%}");
                    if (InHealth(depth))
                        SetPending(s.Time, false, depth, candle.Low);
                }
                else
                {
                    _pending = null; // full retrace => trend flip
                }
            }
        }

        _lastHigh = s;
    }

    private void ProcessLow(SwingPoint s, double atr, Candle candle)
    {
        if (_lastHigh is not null)
        {
            if (_leg is null)
            {
                if (IsMeaningful(s.Price, _lastHigh.Price, atr))
                    _leg = new Leg(false, _lastHigh.Price, s.Price, s.Time);
            }
            else if (!_leg.IsUp)
            {
                _leg = _leg with { Bottom = Math.Min(_leg.Bottom, s.Price), Top = _lastHigh.Price };
            }
            else
            {
                // Counter-move against an up impulse
                var depth = RetraceDepthIntoUpLeg(s.Price);
                if (depth > 0 && depth < 1)
                {
                    Emit(Concept.Retracement, "Bullish", s.Time, $"retrace {depth:0%}");
                    if (InHealth(depth))
                        SetPending(s.Time, true, depth, candle.High);
                }
                else
                {
                    _pending = null; // full retrace => trend flip
                }
            }
        }

        _lastLow = s;
    }

    private void SetPending(long time, bool legUp, double depth, double resumeLevel)
    {
        _pending = new PendingCounter(time, legUp, depth);
        _resumeLevel = resumeLevel;
    }

    private void ConfirmPullback(long resumeTime, bool legUp, double depth)
    {
        if (_pending is null) return;
        Emit(Concept.Pullback, legUp ? "Bullish" : "Bearish", _pending.Time, $"retrace {depth:0%}");
        _pending = null;
    }

    private double RetraceDepthIntoUpLeg(double price) =>
        _leg!.Top - _leg.Bottom <= 0 ? 1 : (_leg.Top - price) / (_leg.Top - _leg.Bottom);

    private double RetraceDepthIntoDownLeg(double price) =>
        _leg!.Top - _leg.Bottom <= 0 ? 1 : (price - _leg.Bottom) / (_leg.Top - _leg.Bottom);

    private bool IsMeaningful(double priceA, double priceB, double atr) =>
        atr > 0 && Math.Abs(priceB - priceA) >= _legMinAtr * atr;

    private bool InHealth(double depth) => depth >= _healthyMin && depth <= _healthyMax;
}