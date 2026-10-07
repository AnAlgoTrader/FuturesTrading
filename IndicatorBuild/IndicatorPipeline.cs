using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;

namespace IndicatorBuild;

public class IndicatorPipeline
{
    private readonly SwingDetector _swingDetector;

    public IndicatorPipeline(SwingDetector swingDetector)
    {
        _swingDetector = swingDetector;
    }

    public IReadOnlyList<SwingPoint> Swings => _swingDetector.Swings;
    public IReadOnlyList<ConceptEvent> Events { get; private set; } = new List<ConceptEvent>();

    public List<CandleAnalysis> Run(IEnumerable<Candle> candles)
    {
        var analyses = new List<CandleAnalysis>();

        foreach (var candle in candles)
        {
            _swingDetector.ProcessBar(candle);
            analyses.Add(new CandleAnalysis(candle));
        }

        return analyses;
    }
}