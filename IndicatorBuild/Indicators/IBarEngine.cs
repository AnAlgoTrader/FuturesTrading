using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public interface IBarEngine
{
    string Name { get; }
    IReadOnlyList<ConceptEvent> Events { get; }
    IReadOnlyList<Zone> Zones { get; }
    void OnBar(BarContext context);
}