using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class FvgDetector : BarEngine
{
    private readonly double _minWidthAtrFactor;
    private readonly List<Candle> _lastThree = new();
    private readonly List<Zone> _zones = new();

    public override string Name => "FvgDetector";
    public override IReadOnlyList<Zone> Zones => _zones;

    public FvgDetector(double minWidthAtrFactor)
    {
        _minWidthAtrFactor = minWidthAtrFactor;
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

            var touched = zone.Direction == "Bullish"
                ? candle.Low <= zone.Top
                : candle.High >= zone.Bottom;

            if (touched)
            {
                zone.State = ZoneState.Mitigated;
                Emit(Concept.FairValueGap, zone.Direction, candle.Time, "Mitigated");
            }
        }
    }

    private void DetectGap(BarContext context, Candle gapBase, Candle gapForm)
    {
        var bullish = gapForm.Low > gapBase.High;
        var bearish = gapForm.High < gapBase.Low;
        if (bullish == bearish) return;

        var top = bullish ? gapForm.Low : gapBase.Low;
        var bottom = bullish ? gapBase.High : gapForm.High;
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