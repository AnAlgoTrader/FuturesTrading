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

    // Shared calculators
    public const int AtrPeriod = 14;
    public const int VolumeAveragePeriod = 50;
}