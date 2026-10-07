using IndicatorBuild.Model;

namespace IndicatorBuild.Rendering;

public static class ColorResolver
{
    public static string Resolve(CandleAnalysis analysis)
    {
        foreach (var evt in analysis.Events)
        {
            if (evt.Type == Concept.ChoCh)
                return evt.Direction == "Bearish" ? IndicatorConfig.BearColor : IndicatorConfig.BullColor;
        }

        foreach (var evt in analysis.Events)
        {
            if (evt.Type == Concept.LiquiditySweep && evt.Detail == "Detected")
                return evt.Direction == "Bearish" ? IndicatorConfig.BearColor : IndicatorConfig.BullColor;
        }

        return IndicatorConfig.DefaultColor;
    }
}