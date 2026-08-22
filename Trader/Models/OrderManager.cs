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
    private const double TrailActivationPct = 0.5;
    private const double TrailDistancePct = 0.5;

    private readonly IbClient _client;
    private readonly OrderStore _store;
    private Contract _contract;

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

        _contract = contract;

        var isLong = signal.Direction == "LONG";
        var entryPrice = isLong ? lastPrice.AskPrice : lastPrice.BidPrice;
        var stopDistance = RoundToTick(StopStdevMultiple * signal.StdevBps / 10000.0 * entryPrice);
        var stopPrice = isLong ? RoundToTick(entryPrice - stopDistance) : RoundToTick(entryPrice + stopDistance);

        var action = isLong ? "BUY" : "SELL";
        var closeAction = isLong ? "SELL" : "BUY";

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
            Transmit = true
        };

        var tracked = new TrackedOrder
        {
            ParentOrderId = parent.OrderId,
            StopOrderId = stop.OrderId,
            Symbol = contract.Symbol,
            Direction = isLong ? TradeDirection.Long : TradeDirection.Short,
            Status = TrackedOrderStatus.Submitted,
            EntryLimitPrice = entryPrice,
            StopPrice = stopPrice,
            ActivationDistance = RoundToTick(TargetStdevMultiple * TrailActivationPct * signal.StdevBps / 10000.0 * entryPrice),
            TrailDistance = RoundToTick(stopDistance * TrailDistancePct),
            Quantity = Quantity,
            StdevBps = signal.StdevBps,
            CreatedAt = DateTime.Now
        };

        _client.PlaceOrder(contract, parent);
        _client.PlaceOrder(contract, stop);
        _store.Save(tracked);

        _client.NotifySubscriber(
            $"BRACKET {tracked.Direction} {Quantity} {contract.Symbol} | entry LMT {entryPrice:F2} | stop {stopPrice:F2} | ids {parent.OrderId}/{stop.OrderId}");
    }

    public void OnCandleClosed(Candle candle)
    {
        var tracked = _store.Load();
        if (tracked == null || tracked.Status != TrackedOrderStatus.Filled)
            return;

        if (tracked.Direction == TradeDirection.Long)
        {
            if (candle.High > tracked.BestPrice)
                tracked.BestPrice = candle.High;
        }
        else
        {
            if (candle.Low < tracked.BestPrice)
                tracked.BestPrice = candle.Low;
        }

        var profitMade = tracked.Direction == TradeDirection.Long
            ? tracked.BestPrice - tracked.AverageFillPrice
            : tracked.AverageFillPrice - tracked.BestPrice;

        if (!tracked.TrailingActive && profitMade >= tracked.ActivationDistance)
        {
            tracked.TrailingActive = true;
            _client.NotifySubscriber(
                $"TRAIL ACTIVATED - best {tracked.BestPrice:F2} | profit {profitMade:F2} >= activation {tracked.ActivationDistance:F2}");
        }

        if (!tracked.TrailingActive)
        {
            _store.Save(tracked);
            return;
        }

        var newStop = tracked.Direction == TradeDirection.Long
            ? RoundToTick(tracked.BestPrice - tracked.TrailDistance)
            : RoundToTick(tracked.BestPrice + tracked.TrailDistance);

        var improves = tracked.Direction == TradeDirection.Long
            ? newStop > tracked.StopPrice
            : newStop < tracked.StopPrice;

        if (!improves)
        {
            _store.Save(tracked);
            return;
        }

        var previousStop = tracked.StopPrice;
        tracked.StopPrice = newStop;
        _store.Save(tracked);

        var modify = new Order
        {
            OrderId = tracked.StopOrderId,
            Action = tracked.Direction == TradeDirection.Long ? "SELL" : "BUY",
            TotalQuantity = tracked.Quantity,
            OrderType = "STP",
            AuxPrice = newStop,
            ParentId = tracked.ParentOrderId,
            Tif = "DAY",
            Transmit = true
        };
        _client.PlaceOrder(_contract, modify);

        _client.NotifySubscriber(
            $"TRAIL STOP {previousStop:F2} -> {newStop:F2} | best {tracked.BestPrice:F2}");
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
            tracked.BestPrice = status.AvgFillPrice;
            tracked.FilledAt = DateTime.Now;
            _store.Save(tracked);
            _client.NotifySubscriber($"ENTRY FILLED - order {status.OrderId} @ {status.AvgFillPrice:F2}");
        }
        else if (status.OrderId == tracked.StopOrderId &&
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
