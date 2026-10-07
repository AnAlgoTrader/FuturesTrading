using IndicatorBuild.Model;

namespace IndicatorBuild.Rendering;

public static class ColorResolver
{
    public static string Resolve(CandleAnalysis analysis)
    {
        // Milestone 2+: evaluate events by priority; the default stays gray.
        return IndicatorConfig.DefaultColor;
    }
}