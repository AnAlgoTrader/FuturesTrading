namespace FootPrintTrader.Model;

public class SignalSettings
{
    /// <summary>Minimum absolute delta for sell/buy absorption (default: 250).</summary>
    public decimal MinDeltaAbsorption { get; set; } = 250m;

    /// <summary>Lookback in bars for swing high / low (default: 10).</summary>
    public int SwingLookback { get; set; } = 10;

    /// <summary>Tolerance past the swing level in ticks (default: 1).</summary>
    public int ToleranceTicks { get; set; } = 1;

    /// <summary>Lookback in bars for CVD divergence (default: 15).</summary>
    public int CvdDivergenceLookback { get; set; } = 15;

    /// <summary>Lookback in bars for breakout confirmation (default: 10).</summary>
    public int BreakoutLookback { get; set; } = 10;

    /// <summary>Minimum delta for breakout confirmation (default: 100).</summary>
    public decimal MinDeltaBreakout { get; set; } = 100m;

    /// <summary>Minimum buy imbalances for breakout confirmation (default: 3).</summary>
    public int MinBuyImbalancesBreakout { get; set; } = 3;

    /// <summary>Imbalance ratio for diagonal comparison (default: 3.0m = 3 to 1).</summary>
    public decimal ImbalanceRatio { get; set; } = 3.0m;

    /// <summary>Minimum ask contract volume to qualify as buy imbalance (default: 10).</summary>
    public decimal ImbalanceMinVolume { get; set; } = 10m;

    /// <summary>FAST breakout threshold ratio versus median seconds (default: 0.70 = under 70%).</summary>
    public double FastThresholdRatio { get; set; } = 0.70;

    /// <summary>Slowdown threshold ratio versus median seconds for last 3 bars (default: 1.50 = over 150%).</summary>
    public double SlowdownThresholdRatio { get; set; } = 1.50;

    /// <summary>Window size for rolling speed median in bars (default: 20).</summary>
    public int SpeedMedianWindow { get; set; } = 20;

    /// <summary>Minimum prior bars required before computing speed median (default: 8).</summary>
    public int SpeedMinBars { get; set; } = 8;

    /// <summary>Minimum bar spacing before the same signal can fire again (default: 3 bars).</summary>
    public int RepeatControlBars { get; set; } = 3;
}
