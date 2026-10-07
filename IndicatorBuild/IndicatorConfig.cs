namespace IndicatorBuild;

public static class IndicatorConfig
{
    // Rendering
    public const string DefaultColor = "#808080";
    public const string BullColor = "#26A69A";
    public const string BearColor = "#EF5350";

    // Swings / structure
    public const int SwingPivotLookback = 5;

    // Sweeps
    public const double SweepMinWickFraction = 0.35;

    // FVG
    public const double FvgMinWidthAtrFactor = 1.0;

    // Shared calculators
    public const int AtrPeriod = 14;
    public const int VolumeAveragePeriod = 50;
}