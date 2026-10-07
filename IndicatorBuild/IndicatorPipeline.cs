using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;

namespace IndicatorBuild;

public class IndicatorPipeline
{
    private readonly SwingDetector _swingDetector;
    private readonly IReadOnlyList<IBarEngine> _engines;
    private readonly AtrCalculator _atr;
    private readonly SmaCalculator _volumeSma;

    public IndicatorPipeline(SwingDetector swingDetector, params IBarEngine[] engines)
    {
        _swingDetector = swingDetector;
        _engines = engines;
        _atr = new AtrCalculator(IndicatorConfig.AtrPeriod);
        _volumeSma = new SmaCalculator(IndicatorConfig.VolumeAveragePeriod);
    }

    public IReadOnlyList<SwingPoint> Swings => _swingDetector.Swings;

    public IReadOnlyList<ConceptEvent> Events { get; private set; } = new List<ConceptEvent>();
    public IReadOnlyList<Zone> Zones { get; private set; } = new List<Zone>();

    public List<CandleAnalysis> Run(IEnumerable<Candle> candles)
    {
        var analyses = new List<CandleAnalysis>();
        var allEvents = new List<ConceptEvent>();
        var allZones = new List<Zone>();

        foreach (var candle in candles)
        {
            var atr = _atr.Next(candle);
            var avgVolume = _volumeSma.Next(candle.Volume);

            _swingDetector.ProcessBar(candle, out var newSwings);

            var analysis = new CandleAnalysis(candle);
            analyses.Add(analysis);

            var context = new BarContext(candle, atr, avgVolume, newSwings);

            foreach (var engine in _engines)
            {
                var eventsBefore = engine.Events.Count;
                var zonesBefore = engine.Zones.Count;
                engine.OnBar(context);

                for (var i = eventsBefore; i < engine.Events.Count; i++)
                {
                    var evt = engine.Events[i];
                    allEvents.Add(evt);
                    analysis.Events.Add(evt);
                }

                for (var i = zonesBefore; i < engine.Zones.Count; i++)
                    allZones.Add(engine.Zones[i]);
            }
        }

        Events = allEvents;
        Zones = allZones;
        return analyses;
    }
}