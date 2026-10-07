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
            .Where(z => z.Kind is ZoneKind.FairValueGap or ZoneKind.Consolidation
                or ZoneKind.AccumulationRange or ZoneKind.DistributionRange)
            .OrderBy(z => z.StartTime)
            .TakeLast(ZoneCap)
            .Select(z => z.Kind switch
            {
                ZoneKind.Consolidation => new ChartZone(z.StartTime, z.EndTime, z.Top, z.Bottom, "rgba(126,87,194,0.14)"),
                ZoneKind.AccumulationRange => new ChartZone(z.StartTime, z.EndTime, z.Top, z.Bottom, "rgba(77,182,172,0.12)"),
                ZoneKind.DistributionRange => new ChartZone(z.StartTime, z.EndTime, z.Top, z.Bottom, "rgba(240,98,146,0.12)"),
                _ => new ChartZone(z.StartTime, z.EndTime, z.Top, z.Bottom,
                    z.Direction == "Bearish" ? "rgba(239,83,80,0.16)" : "rgba(38,166,154,0.16)")
            })
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
            case Concept.Breakout when evt.Detail == "":
            {
                var bull = evt.Direction == "Bullish";
                markers.Add(new ChartMarker(
                    evt.Time,
                    bull ? "aboveBar" : "belowBar",
                    bull ? "arrowUp" : "arrowDown",
                    bull ? IndicatorConfig.BullColor : IndicatorConfig.BearColor,
                    "BO"));
                break;
            }
            case Concept.Pullback:
            {
                var bull = evt.Direction == "Bullish";
                markers.Add(new ChartMarker(
                    evt.Time,
                    bull ? "aboveBar" : "belowBar",
                    "circle",
                    "#7E57C2",
                    "PB"));
                break;
            }
            case Concept.SellingClimax:
            case Concept.BuyingClimax:
            {
                markers.Add(new ChartMarker(
                    evt.Time,
                    "belowBar",
                    "diamond",
                    "#FFB74D",
                    evt.Type == Concept.SellingClimax ? "SC" : "BC"));
                break;
            }
            case Concept.AutomaticRally:
            {
                var bull = evt.Direction == "Bullish";
                markers.Add(new ChartMarker(
                    evt.Time,
                    bull ? "aboveBar" : "belowBar",
                    "diamond",
                    bull ? IndicatorConfig.BullColor : IndicatorConfig.BearColor,
                    "AR"));
                break;
            }
            case Concept.SecondaryTest:
            {
                var bull = evt.Direction == "Bullish";
                markers.Add(new ChartMarker(
                    evt.Time,
                    bull ? "aboveBar" : "belowBar",
                    "diamond",
                    bull ? IndicatorConfig.BullColor : IndicatorConfig.BearColor,
                    "ST"));
                break;
            }
            case Concept.Spring:
            case Concept.UpthrustAfterDistribution:
            {
                markers.Add(new ChartMarker(
                    evt.Time,
                    "belowBar",
                    "circle",
                    "#FFB74D",
                    evt.Type == Concept.Spring ? "Spring" : "UTAD"));
                break;
            }
            case Concept.SignOfStrength:
            {
                markers.Add(new ChartMarker(
                    evt.Time,
                    "aboveBar",
                    "diamond",
                    IndicatorConfig.BullColor,
                    "SOS"));
                break;
            }
            case Concept.SignOfWeakness:
            {
                markers.Add(new ChartMarker(
                    evt.Time,
                    "belowBar",
                    "diamond",
                    IndicatorConfig.BearColor,
                    "SOW"));
                break;
            }
            case Concept.LastPointOfSupport:
            {
                markers.Add(new ChartMarker(
                    evt.Time,
                    "aboveBar",
                    "diamond",
                    IndicatorConfig.BullColor,
                    "LPS"));
                break;
            }
            case Concept.LastPointOfSupply:
            {
                markers.Add(new ChartMarker(
                    evt.Time,
                    "belowBar",
                    "diamond",
                    IndicatorConfig.BearColor,
                    "LPSY"));
                break;
            }
            case Concept.AccumulationBreakout:
            case Concept.DistributionBreakout:
            {
                var bull = evt.Type == Concept.AccumulationBreakout;
                markers.Add(new ChartMarker(
                    evt.Time,
                    bull ? "aboveBar" : "belowBar",
                    bull ? "arrowUp" : "arrowDown",
                    bull ? IndicatorConfig.BullColor : IndicatorConfig.BearColor,
                    "PhaseE"));
                break;
            }
        }
    }
}