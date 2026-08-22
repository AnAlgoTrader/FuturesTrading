using System;

namespace Trader.Models;

public enum TradeDirection
{
    Long,
    Short
}

public enum TrackedOrderStatus
{
    Submitted,
    Filled,
    Closed,
    Cancelled
}

public class TrackedOrder
{
    public int ParentOrderId { get; init; }
    public int StopOrderId { get; init; }
    public string Symbol { get; init; }
    public TradeDirection Direction { get; init; }
    public TrackedOrderStatus Status { get; set; }
    public double EntryLimitPrice { get; init; }
    public double StopPrice { get; set; }
    public double ActivationDistance { get; init; }
    public double TrailDistance { get; init; }
    public double BestPrice { get; set; }
    public bool TrailingActive { get; set; }
    public decimal Quantity { get; init; }
    public double StdevBps { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime FilledAt { get; set; }
    public DateTime ClosedAt { get; set; }
    public double AverageFillPrice { get; set; }
    public double ExitPrice { get; set; }
}
