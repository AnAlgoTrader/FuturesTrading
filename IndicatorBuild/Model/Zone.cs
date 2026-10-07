namespace IndicatorBuild.Model;

public enum ZoneKind
{
    FairValueGap,
    Consolidation,
    AccumulationRange,
    DistributionRange,
    OrderBlock
}

public enum ZoneState
{
    Active,
    Mitigated
}

public class Zone
{
    public long StartTime { get; set; }
    public long EndTime { get; set; }
    public double Top { get; set; }
    public double Bottom { get; set; }
    public ZoneKind Kind { get; set; } = ZoneKind.FairValueGap;
    public ZoneState State { get; set; } = ZoneState.Active;
    public string Direction { get; set; } = "";
    public long MitigatedTime { get; set; }
}