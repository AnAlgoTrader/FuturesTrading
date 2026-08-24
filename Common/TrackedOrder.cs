using System;

namespace Common;

public enum TradeDirection
{
    Long,
    Short
}

public enum TrackedOrderStatus
{
    Submitted,
    Filled,
    Exiting,
    Closed,
    Cancelled
}

public class TrackedOrder
{
    public int ParentOrderId { get; set; }
    public int StopOrderId { get; set; }
    public int ExitOrderId { get; set; }
    public string Symbol { get; set; }
    public TradeDirection Direction { get; set; }
    public TrackedOrderStatus Status { get; set; }
    public double EntryLimitPrice { get; set; }
    public double StopPrice { get; set; }
    public double ActivationDistance { get; set; }
    public double TrailDistance { get; set; }
    public double BestPrice { get; set; }
    public bool TrailingActive { get; set; }
    public int BarsHeld { get; set; }
    public decimal Quantity { get; init; }
    public double StdevBps { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime FilledAt { get; set; }
    public DateTime ClosedAt { get; set; }
    public double AverageFillPrice { get; set; }
    public double ExitPrice { get; set; }
}
