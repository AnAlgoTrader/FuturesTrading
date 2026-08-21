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
    public int ParentOrderId { get; set; }
    public int StopOrderId { get; set; }
    public int TargetOrderId { get; set; }
    public string Symbol { get; set; }
    public TradeDirection Direction { get; set; }
    public TrackedOrderStatus Status { get; set; }
    public double EntryLimitPrice { get; set; }
    public double StopPrice { get; set; }
    public double TargetPrice { get; set; }
    public decimal Quantity { get; set; }
    public double StdevBps { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime FilledAt { get; set; }
    public DateTime ClosedAt { get; set; }
    public double AverageFillPrice { get; set; }
    public double ExitPrice { get; set; }
}
