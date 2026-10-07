using System.Collections.Generic;

namespace Indicator;

/// <summary>
/// Everything computed for one candle. <see cref="Color"/> is the palette color for the candle
/// (neutral gray unless <see cref="Signal"/> fired).
/// </summary>
public sealed record IndicatorResult(
    SignalType Signal,
    string Color,
    double Vwap,
    double? UpperBand1,
    double? UpperBand2,
    double? LowerBand1,
    double? LowerBand2,
    SessionLevels Levels,
    IReadOnlyList<SwingPoint> ActiveSwings);
