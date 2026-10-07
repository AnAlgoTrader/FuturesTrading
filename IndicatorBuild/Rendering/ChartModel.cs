using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Rendering;

public record ChartCandle(long Time, double Open, double High, double Low, double Close, string? Color);
public record ChartMarker(long Time, string Position, string Shape, string Color, string Text);
public record ChartZone(long Start, long End, double Top, double Bottom, string Color);

public sealed class ChartModel
{
    public IReadOnlyList<ChartCandle> Candles { get; }
    public IReadOnlyList<VwapPoint> Vwap { get; }
    public IReadOnlyList<ChartMarker> Markers { get; }
    public IReadOnlyList<ChartZone> Zones { get; }

    public ChartModel(
        IReadOnlyList<ChartCandle> candles,
        IReadOnlyList<VwapPoint> vwap,
        IReadOnlyList<ChartMarker> markers,
        IReadOnlyList<ChartZone> zones)
    {
        Candles = candles;
        Vwap = vwap;
        Markers = markers;
        Zones = zones;
    }
}