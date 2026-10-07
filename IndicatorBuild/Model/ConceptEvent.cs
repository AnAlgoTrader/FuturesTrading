namespace IndicatorBuild.Model;

public class ConceptEvent
{
    public long Time { get; init; }
    public Concept Type { get; init; }
    public string Direction { get; init; } = "";
    public string Detail { get; init; } = "";
}