namespace FootPrintTrader.Configuration;

public class SignalSettings
{
    // Structure / lookbacks
    public int SwingLookback { get; set; } = 10;
    public int ToleranceTicks { get; set; } = 1;
    public int CvdDivergenceLookback { get; set; } = 15;
    public int BreakoutLookback { get; set; } = 10;

    // Rolling baselines. The detector uses these instead of absolute size limits so
    // thresholds follow the volume/volatility regime of the current session.
    public int SpeedMedianWindow { get; set; } = 20;
    public int SpeedMinBars { get; set; } = 8;

    /// <summary>Minimum bar spacing before the same signal can fire again (default: 3 bars).</summary>
    public int RepeatControlBars { get; set; } = 3;

    /// <summary>Delta floor as a multiple of the rolling median |delta| (default: 2.0).</summary>
    public double DeltaFloorRatio { get; set; } = 2.0;

    /// <summary>Absorption rejection wick as a fraction of the bar's range (default: 0.20).</summary>
    public double AbsorptionWickRatio { get; set; } = 0.20;

    /// <summary>Aggressive volume at the extreme as a fraction of bar volume (default: 0.001).</summary>
    public double ExtremeVolumeRatio { get; set; } = 0.001;

    /// <summary>Imbalance ratio at the extreme price (default: 3.0 = 3 to 1).</summary>
    public decimal ImbalanceRatio { get; set; } = 3.0m;

    /// <summary>Exhaustion: current volume as a multiple of the previous bar's volume.</summary>
    public double ExhaustionVolumeRatio { get; set; } = 1.2;

    /// <summary>Exhaustion: bar range as a multiple of the rolling median range.</summary>
    public double ExhaustionRangeRatio { get; set; } = 0.8;

    /// <summary>Breakout: minimum volume as a multiple of the rolling median volume.</summary>
    public double BreakoutMinVolumeRatio { get; set; } = 0.8;

    /// <summary>Bars after a breakout in which a re-entry into the prior range is a fade.</summary>
    public int FadeWindowBars { get; set; } = 3;

    // Reserved for the slowdown / speed phase logic.
    public double FastThresholdRatio { get; set; } = 0.70;
    public double SlowdownThresholdRatio { get; set; } = 1.50;
    public int MinBuyImbalancesBreakout { get; set; } = 3;
}
