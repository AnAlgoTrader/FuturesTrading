namespace Indicator;

/// <summary>
/// Actionable signal detected for a candle. Priority: Sweep &gt; Absorption &gt; Volume Expansion.
/// </summary>
public enum SignalType
{
    None,
    BullAbsorption,
    BearAbsorption,
    BullSweep,
    BearSweep,
    VolumeExpansion
}
