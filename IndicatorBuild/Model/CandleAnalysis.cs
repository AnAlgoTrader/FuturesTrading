using System.Collections.Generic;

namespace IndicatorBuild.Model;

public class CandleAnalysis
{
    public Candle Candle { get; }
    public List<ConceptEvent> Events { get; } = new();

    public CandleAnalysis(Candle candle)
    {
        Candle = candle;
    }
}