namespace IndicatorBuild;

public static class IndicatorConfig
{
    // Rendering
    public const string DefaultColor = "#808080";
    public const string BullColor = "#26A69A";
    public const string BearColor = "#EF5350";

    // Swings / structure
    public const int SwingPivotLookback = 7;

    // Sweeps
    public const double SweepMinWickFraction = 0.5;

    // FVG (full-candle gaps; width in ATR units; fill = fraction of zone depth required to mitigate)
    public const double FvgMinWidthAtrFactor = 3.0;
    public const double FvgFillFraction = 0.5;

    // Consolidation / breakout
    public const int ConsolidationMinBars = 8;
    public const double ConsolidationRangeAtrFactor = 1.5;
    public const int BreakoutConfirmBars = 3;

    // Pullback / retracement
    public const double PullbackLegMinAtr = 1.0;
    public const double PullbackHealthyMin = 0.236;
    public const double PullbackHealthyMax = 0.786;

    // Wyckoff ranges (typed by prior-trend context)
    public const int WyckoffRangeMinSwings = 5;
    public const int WyckoffRangeMinBars = 40;
    public const double WyckoffRangeMinHeightAtr = 1.0;
    public const double WyckoffRangeMaxHeightAtr = 4.0;
    public const double WyckoffPriorTrendAtr = 0.5;
    public const double WyckoffSosVolumeFactor = 1.2;
    public const double WyckoffSosDisplacementAtr = 1.0;

    // Shared calculators
    public const int AtrPeriod = 14;
    public const int RsiPeriod = 14;
    public const int VolumeAveragePeriod = 50;

    // Momentum — exhaustion (wick/body rejection + volume spike + extreme RSI)
    public const double MomentumExhaustWickBody = 2.5;
    public const double MomentumExhaustVolumeFactor = 1.8;
    public const double MomentumExhaustRsiLow = 35;
    public const double MomentumExhaustRsiHigh = 65;

    // Momentum — RSI/price divergence (swing-paired; min price swing and RSI move)
    public const double MomentumDivMinSwingFrac = 0.003;
    public const double MomentumDivMinRsiDelta = 3.0;

    // Momentum — TTM squeeze (BB inside KC) + expansion (release + ATR-ratio crossing)
    public const int MomentumBbPeriod = 20;
    public const double MomentumBbMultiplier = 2.0;
    public const double MomentumSqueezeAtrFactor = 1.5;
    public const double MomentumExpansionAtrRatio = 2.0;
}