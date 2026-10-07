using System;
using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class FvgDetector : BarEngine
{
    private readonly double _minWidthAtrFactor;
    private readonly bool _bodiesOnly;
    private readonly double _fillFraction;
    private readonly List<Candle> _lastThree = new();
    private readonly List<Zone> _zones = new();

    public override string Name => "FvgDetector";
    public override IReadOnlyList<Zone> Zones => _zones;

    public FvgDetector(double minWidthAtrFactor, bool bodiesOnly = false, double fillFraction = 0)
    {
        _minWidthAtrFactor = minWidthAtrFactor;
        _bodiesOnly = bodiesOnly;
        _fillFraction = Math.Clamp(fillFraction, 0, 1);
    }

    public override void OnBar(BarContext context)
    {
        var candle = context.Candle;

        _lastThree.Add(candle);
        if (_lastThree.Count > 3)
            _lastThree.RemoveAt(0);

        if (_lastThree.Count == 3)
            DetectGap(context, _lastThree[0], _lastThree[2]);

        foreach (var zone in _zones)
        {
            if (zone.State != ZoneState.Active || candle.Time <= zone.EndTime)
                continue;

            var depth = zone.Top - zone.Bottom;
            var target = zone.Direction == "Bullish"
                ? zone.Top - _fillFraction * depth
                : zone.Bottom + _fillFraction * depth;

            var touched = zone.Direction == "Bullish"
                ? candle.Low <= target
                : candle.High >= target;

            if (touched)
            {
                zone.State = ZoneState.Mitigated;
                zone.MitigatedTime = candle.Time;
                Emit(Concept.FairValueGap, zone.Direction, candle.Time, "Mitigated");
            }
        }
    }

    private void DetectGap(BarContext context, Candle gapBase, Candle gapForm)
    {
        var bullish = _bodiesOnly
            ? Math.Min(gapForm.Open, gapForm.Close) > Math.Max(gapBase.Open, gapBase.Close)
            : gapForm.Low > gapBase.High;
        var bearish = _bodiesOnly
            ? Math.Max(gapForm.Open, gapForm.Close) < Math.Min(gapBase.Open, gapBase.Close)
            : gapForm.High < gapBase.Low;
        if (bullish == bearish) return;

        var top = bullish
            ? (_bodiesOnly ? Math.Min(gapForm.Open, gapForm.Close) : gapForm.Low)
            : (_bodiesOnly ? Math.Min(gapBase.Open, gapBase.Close) : gapBase.Low);
        var bottom = bullish
            ? (_bodiesOnly ? Math.Max(gapBase.Open, gapBase.Close) : gapBase.High)
            : (_bodiesOnly ? Math.Max(gapForm.Open, gapForm.Close) : gapForm.High);
        var width = top - bottom;
        if (width <= 0) return;

        if (context.Atr is double atr && width < _minWidthAtrFactor * atr)
            return;

        _zones.Add(new Zone
        {
            StartTime = gapBase.Time,
            EndTime = gapForm.Time,
            Top = top,
            Bottom = bottom,
            Kind = ZoneKind.FairValueGap,
            Direction = bullish ? "Bullish" : "Bearish"
        });

        Emit(Concept.FairValueGap, bullish ? "Bullish" : "Bearish", gapForm.Time, "Created");
    }
}