using System;
using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public abstract class BarEngine : IBarEngine
{
    private readonly List<ConceptEvent> _events = new();

    public abstract string Name { get; }
    public abstract void OnBar(BarContext context);

    public IReadOnlyList<ConceptEvent> Events => _events;
    public virtual IReadOnlyList<Zone> Zones => Array.Empty<Zone>();

    protected void Emit(Concept type, string direction, long time, string detail = "")
    {
        _events.Add(new ConceptEvent { Time = time, Type = type, Direction = direction, Detail = detail });
    }
}