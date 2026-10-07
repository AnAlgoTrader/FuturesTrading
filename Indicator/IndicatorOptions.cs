using System;

namespace Indicator;

/// <summary>
/// User-configurable parameters (spec §3). Instances are read by <see cref="IndicatorEngine"/>;
/// treat them as immutable once the engine is created.
/// </summary>
public sealed class IndicatorOptions
{
    /// <summary>
    /// Time zone used to resolve all session boundaries. Defaults to America/New_York (ET, DST aware).
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>Regular Trading Hours start (ET). Default 09:30.</summary>
    public TimeOnly RthStart { get; set; } = new(9, 30);

    /// <summary>Regular Trading Hours end (ET). Default 16:15.</summary>
    public TimeOnly RthEnd { get; set; } = new(16, 15);

    /// <summary>Extended / overnight session start (ET). Default 18:00.</summary>
    public TimeOnly EthStart { get; set; } = new(18, 0);

    /// <summary>Extended / overnight session end (ET). Default 09:30.</summary>
    public TimeOnly EthEnd { get; set; } = new(9, 30);

    /// <summary>Initial Balance window length from RTH start. Default 60 minutes.</summary>
    public TimeSpan InitialBalanceDuration { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>Volume SMA / average spread lookback. Default 20 bars.</summary>
    public int VolumeSmaLength { get; set; } = 20;

    /// <summary>Volume multiplier defining "high volume" for absorption. Default 1.5.</summary>
    public double HighVolumeMultiplier { get; set; } = 1.5;

    /// <summary>Volume multiplier defining ultra-high volume for expansion. Default 2.0.</summary>
    public double UltraHighVolumeMultiplier { get; set; } = 2.0;

    /// <summary>Spread multiplier defining a "narrow spread" for absorption. Default 0.5.</summary>
    public double NarrowSpreadMultiplier { get; set; } = 0.5;

    /// <summary>Spread multiplier defining an expanding bar for the volume-expansion signal. Default 1.5.</summary>
    public double ExpansionSpreadMultiplier { get; set; } = 1.5;

    /// <summary>Pivot bars to the left of a swing point. Default 3.</summary>
    public int PivotLeft { get; set; } = 3;

    /// <summary>Pivot bars to the right of a swing point. Default 3.</summary>
    public int PivotRight { get; set; } = 3;

    /// <summary>
    /// "Near the session extreme" distance, expressed in multiples of the 20-bar average spread.
    /// Default 1.0.
    /// </summary>
    public double NearExtremeBuffer { get; set; } = 1.0;

    /// <summary>Minimum bars of history required before any signal is reported (equals <see cref="VolumeSmaLength"/>).</summary>
    public int MinHistoryBars => Math.Max(5, VolumeSmaLength);

    // Palette (spec §2 / §4). Neutral gray candles, signal colors only on actionable bars.
    public string NeutralColor { get; set; } = "#808080";
    public string BullAbsorptionColor { get; set; } = "#00C853";
    public string BearAbsorptionColor { get; set; } = "#FF1744";
    public string BullSweepColor { get; set; } = "#00E5FF";
    public string BearSweepColor { get; set; } = "#FF9100";
    public string VolumeExpansionColor { get; set; } = "#FFEA00";
}
