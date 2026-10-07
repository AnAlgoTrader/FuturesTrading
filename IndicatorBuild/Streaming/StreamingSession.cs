using System;
using System.Collections.Generic;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;

namespace IndicatorBuild.Streaming;

/// <summary>
/// M8 event-driven consumer. Drives the exact same <see cref="IndicatorPipeline"/> the backtest
/// uses, but one closed bar at a time from an <see cref="IBarSource"/>, and raises callbacks the
/// moment a bar closes / a swing confirms / an event or zone is produced. All emissions happen on
/// the bar that confirmed them — a live chart/Trader consumer must not act on an event before
/// this callback fires, even though the event's own <see cref="ConceptEvent.Time"/> may point at
/// an earlier anchor (e.g. a swing's historical pivot) purely for rendering.
/// </summary>
public sealed class StreamingSession
{
    private readonly IndicatorPipeline _pipeline;
    private readonly StreamingConfluence _confluence;

    public StreamingSession(IndicatorPipeline pipeline, int gate = IndicatorConfig.ConfluenceGate)
    {
        _pipeline = pipeline;
        _confluence = new StreamingConfluence(gate);
    }

    /// <summary>Raised for every closed bar (after scoring), with the 0-based bar index.</summary>
    public event Action<CandleAnalysis, int>? BarClosed;

    /// <summary>Raised immediately after a bar closes for each event confirmed on it.</summary>
    public event Action<ConceptEvent, int>? EventRaised;

    /// <summary>Raised immediately after a bar closes for each zone that formed on it.</summary>
    public event Action<Zone>? ZoneCreated;

    /// <summary>Raised for each newly confirmed swing (time-aware, non-repainting).</summary>
    public event Action<SwingPoint>? SwingConfirmed;

    public int BarsProcessed { get; private set; }

    public IndicatorPipeline Pipeline => _pipeline;

    public int Run(IBarSource source)
    {
        var eventsCountBefore = _pipeline.Events.Count;
        var zonesCountBefore = _pipeline.Zones.Count;
        var swingsCountBefore = _pipeline.Swings.Count;

        while (source.Next() is { } candle)
        {
            var analysis = _pipeline.ProcessBar(candle);

            var newSwings = Slice(_pipeline.Swings, swingsCountBefore);
            swingsCountBefore = _pipeline.Swings.Count;

            var newZones = Slice(_pipeline.Zones, zonesCountBefore);
            zonesCountBefore = _pipeline.Zones.Count;

            _confluence.ProcessBar(candle, analysis, newZones, newSwings, BarsProcessed);

            foreach (var swing in newSwings)
                SwingConfirmed?.Invoke(swing);

            foreach (var zone in newZones)
                ZoneCreated?.Invoke(zone);

            BarClosed?.Invoke(analysis, BarsProcessed);

            for (var i = eventsCountBefore; i < _pipeline.Events.Count; i++)
            {
                EventRaised?.Invoke(_pipeline.Events[i], BarsProcessed);
                eventsCountBefore = _pipeline.Events.Count;
            }

            BarsProcessed++;
        }

        return BarsProcessed;
    }

    private static List<T> Slice<T>(IReadOnlyList<T> source, int from)
    {
        var result = new List<T>(source.Count - from);
        for (var i = from; i < source.Count; i++)
            result.Add(source[i]);
        return result;
    }
}