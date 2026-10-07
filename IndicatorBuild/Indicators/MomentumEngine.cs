using System;
using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

/// <summary>
/// Momentum engine: bar-level exhaustion, RSI/price divergence, TTM squeeze compression
/// and volatility expansion. Compression/expansions fire on state transitions only to
/// avoid flooding a 20k-bar chart with per-bar squeeze events.
/// </summary>
public class MomentumEngine : BarEngine
{
    private readonly double _exhaustWickBody;
    private readonly double _exhaustVolumeFactor;
    private readonly double _exhaustRsiLow;
    private readonly double _exhaustRsiHigh;
    private readonly double _divMinSwingFrac;
    private readonly double _divMinRsiDelta;
    private readonly int _bbPeriod;
    private readonly double _bbMultiplier;
    private readonly double _squeezeAtrFactor;
    private readonly double _expansionAtrRatio;

    private readonly RsiCalculator _rsi;
    private readonly SmaCalculator _atrSma;
    private readonly Queue<double> _closes = new();
    private readonly Dictionary<long, double> _rsiAt = new();
    private readonly Queue<long> _rsiTimeOrder = new();
    private (long Time, double Price, double Rsi)? _lastLow;
    private (long Time, double Price, double Rsi)? _lastHigh;

    private bool _squeezeOn;
    private double _prevAtrRatio;

    public override string Name => "Momentum";

    public MomentumEngine(
        double exhaustWickBody = IndicatorConfig.MomentumExhaustWickBody,
        double exhaustVolumeFactor = IndicatorConfig.MomentumExhaustVolumeFactor,
        double divMinSwingFrac = IndicatorConfig.MomentumDivMinSwingFrac,
        double expansionAtrRatio = IndicatorConfig.MomentumExpansionAtrRatio,
        int rsiPeriod = IndicatorConfig.RsiPeriod,
        double exhaustRsiLow = IndicatorConfig.MomentumExhaustRsiLow,
        double exhaustRsiHigh = IndicatorConfig.MomentumExhaustRsiHigh,
        double divMinRsiDelta = IndicatorConfig.MomentumDivMinRsiDelta)
    {
        _exhaustWickBody = exhaustWickBody;
        _exhaustVolumeFactor = exhaustVolumeFactor;
        _divMinSwingFrac = divMinSwingFrac;
        _expansionAtrRatio = expansionAtrRatio;
        _exhaustRsiLow = exhaustRsiLow;
        _exhaustRsiHigh = exhaustRsiHigh;
        _divMinRsiDelta = divMinRsiDelta;
        _rsi = new RsiCalculator(rsiPeriod);
        _bbPeriod = IndicatorConfig.MomentumBbPeriod;
        _bbMultiplier = IndicatorConfig.MomentumBbMultiplier;
        _squeezeAtrFactor = IndicatorConfig.MomentumSqueezeAtrFactor;
        _atrSma = new SmaCalculator(50);
    }

    public override void OnBar(BarContext context)
    {
        var candle = context.Candle;
        var atr = context.Atr;
        var avgVolume = context.AvgVolume;

        _rsi.Next(candle);
        var rsiNow = _rsi.Value;
        if (rsiNow is { } r)
        {
            _rsiAt[candle.Time] = r;
            _rsiTimeOrder.Enqueue(candle.Time);
            if (_rsiTimeOrder.Count > 600)
            {
                _rsiAt.Remove(_rsiTimeOrder.Dequeue());
            }
        }

        DetectExhaustion(candle, rsiNow, avgVolume);
        DetectSqueeze(candle, atr);
        DetectAtrExpansion(candle, atr);
        DetectDivergences(context);
    }

    private void DetectExhaustion(Candle candle, double? rsi, double? avgVolume)
    {
        if (rsi is null || avgVolume is null || avgVolume <= 0) return;

        var body = Math.Abs(candle.Close - candle.Open);
        var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
        var upperWick = candle.High - Math.Max(candle.Open, candle.Close);

        var volOk = candle.Volume > _exhaustVolumeFactor * avgVolume;
        if (!volOk) return;

        if (lowerWick > _exhaustWickBody * body && rsi < _exhaustRsiLow)
            Emit(Concept.Exhaustion, "Bullish", candle.Time, "Sell");
        else if (upperWick > _exhaustWickBody * body && rsi > _exhaustRsiHigh)
            Emit(Concept.Exhaustion, "Bearish", candle.Time, "Buy");
    }

    private void DetectSqueeze(Candle candle, double? atr)
    {
        _closes.Enqueue(candle.Close);
        if (_closes.Count > _bbPeriod) _closes.Dequeue();
        if (_closes.Count < _bbPeriod || atr is null) return;

        var mid = 0.0;
        foreach (var c in _closes) mid += c;
        mid /= _closes.Count;

        var m2 = 0.0;
        foreach (var c in _closes) m2 += c * c;
        var stdev = Math.Sqrt(Math.Max(0, m2 / _closes.Count - mid * mid));

        var bbUp = mid + _bbMultiplier * stdev;
        var bbLo = mid - _bbMultiplier * stdev;
        var kcUp = mid + _squeezeAtrFactor * atr;
        var kcLo = mid - _squeezeAtrFactor * atr;

        var on = bbLo >= kcLo && bbUp <= kcUp;
        var isUp = candle.Close > mid;

        if (on && !_squeezeOn)
        {
            var tier = stdev <= 0.5 * atr ? "tight" : stdev <= 0.75 * atr ? "medium" : "loose";
            Emit(Concept.Compression, "", candle.Time, tier);
        }
        else if (!on && _squeezeOn)
        {
            Emit(Concept.Expansion, isUp ? "Bullish" : "Bearish", candle.Time, "release");
        }

        _squeezeOn = on;
    }

    private void DetectAtrExpansion(Candle candle, double? atr)
    {
        if (atr is null) return;

        var avg = _atrSma.Next(atr.Value);
        if (avg is null || avg <= 0) { _prevAtrRatio = 1; return; }

        var ratio = atr.Value / avg.Value;
        if (ratio >= _expansionAtrRatio && _prevAtrRatio < _expansionAtrRatio)
            Emit(Concept.Expansion, candle.Close >= candle.Open ? "Bullish" : "Bearish", candle.Time, "volatility");
        _prevAtrRatio = ratio;
    }

    private void DetectDivergences(BarContext context)
    {
        foreach (var swing in context.NewSwings)
        {
            if (!_rsiAt.TryGetValue(swing.Time, out var rsiNow)) continue;

            if (swing.IsHigh)
            {
                if (_lastHigh is { } prev)
                {
                    var priceDelta = Math.Abs(swing.Price - prev.Price) / Math.Max(1, prev.Price);
                    var rsiDelta = rsiNow - prev.Rsi;
                    if (priceDelta >= _divMinSwingFrac)
                    {
                        if (swing.Price > prev.Price && rsiDelta <= -_divMinRsiDelta)
                            Emit(Concept.Divergence, "Bearish", swing.Time, "Regular");
                        else if (swing.Price < prev.Price && rsiDelta >= _divMinRsiDelta)
                            Emit(Concept.Divergence, "Bearish", swing.Time, "Hidden");
                    }
                }
                _lastHigh = (swing.Time, swing.Price, rsiNow);
            }
            else
            {
                if (_lastLow is { } prev)
                {
                    var priceDelta = Math.Abs(swing.Price - prev.Price) / Math.Max(1, prev.Price);
                    var rsiDelta = rsiNow - prev.Rsi;
                    if (priceDelta >= _divMinSwingFrac)
                    {
                        if (swing.Price < prev.Price && rsiDelta >= _divMinRsiDelta)
                            Emit(Concept.Divergence, "Bullish", swing.Time, "Regular");
                        else if (swing.Price > prev.Price && rsiDelta <= -_divMinRsiDelta)
                            Emit(Concept.Divergence, "Bullish", swing.Time, "Hidden");
                    }
                }
                _lastLow = (swing.Time, swing.Price, rsiNow);
            }
        }
    }
}