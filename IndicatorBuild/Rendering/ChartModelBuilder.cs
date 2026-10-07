using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Model;

namespace IndicatorBuild.Rendering;

public sealed class ChartModelBuilder
{
    public ChartModel Build(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<VwapPoint> vwap,
        IReadOnlyList<CandleAnalysis> analyses,
        IReadOnlyList<Zone> zones)
    {
        var chartCandles = new List<ChartCandle>(candles.Count);
        var markers = new List<ChartMarker>();

        for (var i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            chartCandles.Add(new ChartCandle(
                candle.Time, candle.Open, candle.High, candle.Low, candle.Close,
                ColorResolver.Resolve(analyses[i])));

            foreach (var evt in analyses[i].Events)
                AddMarker(markers, evt);
        }

        var chartZones = zones
            .Where(z => z.Kind == ZoneKind.FairValueGap)
            .OrderBy(z => z.StartTime)
            .TakeLast(ZoneCap)
            .Select(z => new ChartZone(
                z.StartTime, z.EndTime, z.Top, z.Bottom,
                z.Direction == "Bearish"
                    ? "rgba(239,83,80,0.16)"
                    : "rgba(38,166,154,0.16)"))
            .ToList();

        return new ChartModel(chartCandles, vwap, markers.TakeLast(MarkerCap).ToList(), chartZones);
    }

    private const int MarkerCap = 3000;
    private const int ZoneCap = 2000;

    private static void AddMarker(List<ChartMarker> markers, ConceptEvent evt)
    {
        switch (evt.Type)
        {
            case Concept.ChoCh:
            case Concept.BOS:
            {
                var bull = evt.Direction == "Bullish";
                markers.Add(new ChartMarker(
                    evt.Time,
                    bull ? "aboveBar" : "belowBar",
                    bull ? "arrowUp" : "arrowDown",
                    bull ? IndicatorConfig.BullColor : IndicatorConfig.BearColor,
                    evt.Type.ToString()));
                break;
            }
            case Concept.LiquiditySweep when evt.Detail == "Detected":
            {
                var bull = evt.Direction == "Bullish";
                markers.Add(new ChartMarker(
                    evt.Time,
                    bull ? "aboveBar" : "belowBar",
                    "circle",
                    bull ? IndicatorConfig.BullColor : IndicatorConfig.BearColor,
                    "Sweep"));
                break;
            }
            case Concept.FairValueGap when evt.Detail == "Created":
            {
                var bull = evt.Direction == "Bullish";
                markers.Add(new ChartMarker(
                    evt.Time,
                    bull ? "aboveBar" : "belowBar",
                    "square",
                    bull ? IndicatorConfig.BullColor : IndicatorConfig.BearColor,
                    "FVG"));
                break;
            }
        }
    }
}