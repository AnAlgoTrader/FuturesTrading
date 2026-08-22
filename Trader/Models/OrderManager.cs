using System;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace Trader.Models;

public class OrderManager
{
    private const decimal Quantity = 1m;
    private const double TickSize = 0.25;
    private const double TargetStdevMultiple = 1.5;
    private const double StopStdevMultiple = 2.0;

    private readonly IbClient _client;
    private readonly OrderStore _store;

    public OrderManager(IbClient client, OrderStore store)
    {
        _client = client;
        _store = store;
    }

    public void OnSignal(Signal signal, Contract contract, PriceUpdate lastPrice)
    {
        var active = _store.Load();
        if (active != null &&
            (active.Status == TrackedOrderStatus.Submitted || active.Status == TrackedOrderStatus.Filled))
        {
            _client.NotifySubscriber(
                $"Signal ignored - order already {active.Status} ({active.Direction} @ {active.EntryLimitPrice:F2}).");
            return;
        }

        var isLong = signal.Direction == "LONG";
        var entryPrice = isLong ? lastPrice.AskPrice : lastPrice.BidPrice;
        var targetDistance = RoundToTick(TargetStdevMultiple * signal.StdevBps / 10000.0 * entryPrice);
        var stopDistance = RoundToTick(StopStdevMultiple * signal.StdevBps / 10000.0 * entryPrice);

        var action = isLong ? "BUY" : "SELL";
        var closeAction = isLong ? "SELL" : "BUY";
        var stopPrice = isLong ? RoundToTick(entryPrice - stopDistance) : RoundToTick(entryPrice + stopDistance);
        var targetPrice = isLong ? RoundToTick(entryPrice + targetDistance) : RoundToTick(entryPrice - targetDistance);

        var parent = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = action,
            TotalQuantity = Quantity,
            OrderType = "LMT",
            LmtPrice = entryPrice,
            Tif = "DAY",
            Transmit = false
        };
        var stop = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = closeAction,
            TotalQuantity = Quantity,
            OrderType = "STP",
            AuxPrice = stopPrice,
            ParentId = parent.OrderId,
            Transmit = false
        };
        var target = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = closeAction,
            TotalQuantity = Quantity,
            OrderType = "LMT",
            LmtPrice = targetPrice,
            ParentId = parent.OrderId,
            Transmit = true
        };

        var tracked = new TrackedOrder
        {
            ParentOrderId = parent.OrderId,
            StopOrderId = stop.OrderId,
            TargetOrderId = target.OrderId,
            Symbol = contract.Symbol,
            Direction = isLong ? TradeDirection.Long : TradeDirection.Short,
            Status = TrackedOrderStatus.Submitted,
            EntryLimitPrice = entryPrice,
            StopPrice = stopPrice,
            TargetPrice = targetPrice,
            Quantity = Quantity,
            StdevBps = signal.StdevBps,
            CreatedAt = DateTime.Now
        };

        _client.PlaceOrder(contract, parent);
        _client.PlaceOrder(contract, stop);
        _client.PlaceOrder(contract, target);
        _store.Save(tracked);

        _client.NotifySubscriber(
            $"BRACKET {tracked.Direction} {Quantity} {contract.Symbol} | entry LMT {entryPrice:F2} | stop {stopPrice:F2} | target {targetPrice:F2} | ids {parent.OrderId}/{stop.OrderId}/{target.OrderId}");
    }

    public void OnOrderStatus(OrderStatus status)
    {
        var tracked = _store.Load();
        if (tracked == null)
            return;

        if (status.OrderId == tracked.ParentOrderId && status.Status == "Filled" &&
            tracked.Status == TrackedOrderStatus.Submitted)
        {
            tracked.Status = TrackedOrderStatus.Filled;
            tracked.AverageFillPrice = status.AvgFillPrice;
            tracked.FilledAt = DateTime.Now;
            _store.Save(tracked);
            _client.NotifySubscriber($"ENTRY FILLED - order {status.OrderId} @ {status.AvgFillPrice:F2}");
        }
        else if ((status.OrderId == tracked.StopOrderId || status.OrderId == tracked.TargetOrderId) &&
                 status.Status == "Filled")
        {
            tracked.Status = TrackedOrderStatus.Closed;
            tracked.ExitPrice = status.AvgFillPrice;
            tracked.ClosedAt = DateTime.Now;
            _store.Save(tracked);
            _client.NotifySubscriber($"EXIT FILLED - order {status.OrderId} @ {status.AvgFillPrice:F2} | position closed");
            _store.Clear();
        }
        else if (status.OrderId == tracked.ParentOrderId &&
                 (status.Status == "Cancelled" || status.Status == "Inactive") &&
                 tracked.Status == TrackedOrderStatus.Submitted)
        {
            tracked.Status = TrackedOrderStatus.Cancelled;
            _store.Save(tracked);
            _client.NotifySubscriber($"Entry order {status.OrderId} {status.Status} - bracket dead.");
            _store.Clear();
        }
        else if (status.Status == "Cancelled" && status.OrderId != tracked.ParentOrderId)
        {
            _client.NotifySubscriber($"Order {status.OrderId} cancelled.");
        }
    }

    private static double RoundToTick(double price)
    {
        return Math.Round(Math.Round(price / TickSize) * TickSize, 2);
    }
}
