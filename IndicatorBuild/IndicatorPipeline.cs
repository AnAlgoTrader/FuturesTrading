using System.Collections.Generic;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;

namespace IndicatorBuild;

public class IndicatorPipeline
{
    private readonly SwingDetector _swingDetector;
    private readonly IReadOnlyList<IBarEngine> _engines;
    private readonly AtrCalculator _atr;
    private readonly SmaCalculator _volumeSma;
    private readonly List<ConceptEvent> _events = new();
    private readonly List<Zone> _zones = new();

    public IndicatorPipeline(SwingDetector swingDetector, params IBarEngine[] engines)
    {
        _swingDetector = swingDetector;
        _engines = engines;
        _atr = new AtrCalculator(IndicatorConfig.AtrPeriod);
        _volumeSma = new SmaCalculator(IndicatorConfig.VolumeAveragePeriod);
    }

    public IReadOnlyList<SwingPoint> Swings => _swingDetector.Swings;
    public IReadOnlyList<ConceptEvent> Events => _events;
    public IReadOnlyList<Zone> Zones => _zones;

    /// <summary>Processes one freshly-closed candle. The M8 streaming path: identical per-bar
    /// logic whether driven by a historical loop or a live feed. Any event/zone emitted here is
    /// fully confirmed by this closed bar — there is no lookahead.</summary>
    public CandleAnalysis ProcessBar(Candle candle)
    {
        var atr = _atr.Next(candle);
        var avgVolume = _volumeSma.Next(candle.Volume);

        _swingDetector.ProcessBar(candle, out var newSwings);

        var analysis = new CandleAnalysis(candle);
        var context = new BarContext(candle, atr, avgVolume, newSwings);

        foreach (var engine in _engines)
        {
            var eventsBefore = engine.Events.Count;
            var zonesBefore = engine.Zones.Count;
            engine.OnBar(context);

            for (var i = eventsBefore; i < engine.Events.Count; i++)
            {
                var evt = engine.Events[i];
                _events.Add(evt);
                analysis.Events.Add(evt);
            }

            for (var i = zonesBefore; i < engine.Zones.Count; i++)
                _zones.Add(engine.Zones[i]);
        }

        return analysis;
    }

    public List<CandleAnalysis> Run(IEnumerable<Candle> candles)
    {
        var analyses = new List<CandleAnalysis>();
        foreach (var candle in candles)
            analyses.Add(ProcessBar(candle));
        return analyses;
    }
}