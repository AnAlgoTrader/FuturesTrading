using IndicatorBuild.Model;

namespace IndicatorBuild.Rendering;

public static class ColorResolver
{
    public static string Resolve(CandleAnalysis analysis)
    {
        foreach (var evt in analysis.Events)
        {
            if (evt.Type == Concept.ChoCh && evt.Qualified)
                return evt.Direction == "Bearish" ? IndicatorConfig.BearColor : IndicatorConfig.BullColor;
        }

        foreach (var evt in analysis.Events)
        {
            if (evt.Type == Concept.LiquiditySweep && evt.Detail == "Detected" && evt.Qualified)
                return evt.Direction == "Bearish" ? IndicatorConfig.BearColor : IndicatorConfig.BullColor;
        }

        foreach (var evt in analysis.Events)
        {
            if (evt.Type == Concept.Breakout && evt.Detail == "" && evt.Qualified)
                return evt.Direction == "Bearish" ? IndicatorConfig.BearColor : IndicatorConfig.BullColor;
        }

        foreach (var evt in analysis.Events)
        {
            if (evt.Type is Concept.Exhaustion or Concept.Divergence && evt.Qualified)
                return "#FFB74D";
        }

        foreach (var evt in analysis.Events)
        {
            if (evt.Type is Concept.SignOfStrength or Concept.SignOfWeakness
                or Concept.AccumulationBreakout or Concept.DistributionBreakout && evt.Qualified)
                return evt.Direction == "Bearish" ? IndicatorConfig.BearColor : IndicatorConfig.BullColor;
        }

        foreach (var evt in analysis.Events)
        {
            if (evt.Type == Concept.Compression)
                return "#7E57C2";
        }

        foreach (var evt in analysis.Events)
        {
            if (evt.Type == Concept.Expansion && evt.Qualified)
                return evt.Direction == "Bearish" ? IndicatorConfig.BearColor : IndicatorConfig.BullColor;
        }

        return IndicatorConfig.DefaultColor;
    }
}