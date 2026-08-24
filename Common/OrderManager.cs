using System;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace Common;

public class OrderManager
{
    private const decimal Quantity = 1m;
    private const double TickSize = 0.25;
    private const double InitialStopPoints = 10.0;
    private const double TrailDistancePoints = 5.0;
    private const int MaxHoldBars = 50;
    private const double SessionGapMinutes = 60;

    private readonly IbClient _client;
    private readonly OrderStore _store;
    private readonly TradeJournal _journal;
    private Contract _contract;
    private DateTime _previousCandleTimestamp;
    private bool _isNewSession = true;

    public OrderManager(IbClient client, OrderStore store, TradeJournal journal = null)
    {
        _client = client;
        _store = store;
        _journal = journal;
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

        if (_isNewSession)
        {
            _client.NotifySubscriber("Signal ignored - first bar of a new session.");
            return;
        }

        _contract = contract;

        var isLong = signal.Direction == "LONG";
        var entryPrice = isLong ? lastPrice.AskPrice : lastPrice.BidPrice;
        var stopDistance = InitialStopPoints;
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
            Tif = "DAY",
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
            TrailDistance = TrailDistancePoints,
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

    public void OnCandleClosed(Candle candle, PriceUpdate lastPrice)
    {
        if (_previousCandleTimestamp != default &&
            (candle.Timestamp - _previousCandleTimestamp).TotalMinutes > SessionGapMinutes)
        {
            _isNewSession = true;
            ForceExitIfFilled(lastPrice, "SESSION_END");
        }
        else
        {
            _isNewSession = false;
        }
        _previousCandleTimestamp = candle.Timestamp;

        var tracked = _store.Load();
        if (tracked == null || tracked.Status != TrackedOrderStatus.Filled)
            return;

        if (candle.Timestamp > tracked.FilledAt)
            tracked.BarsHeld++;

        if (tracked.BarsHeld >= MaxHoldBars)
        {
            BeginExit(tracked, lastPrice, $"MAX_HOLD ({MaxHoldBars} bars)");
            return;
        }

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

        if (!tracked.TrailingActive && profitMade > 0)
        {
            tracked.TrailingActive = true;
            _client.NotifySubscriber(
                $"TRAIL ACTIVATED - best {tracked.BestPrice:F2} | profit {profitMade:F2} | trail {tracked.TrailDistance:F2}");
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

        if (tracked.Status == TrackedOrderStatus.Exiting)
        {
            HandleExitingStatus(tracked, status);
            return;
        }

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
            _journal?.Append(tracked);
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

    public void OnOrderRejected(int orderId, int errorCode, string message)
    {
        var tracked = _store.Load();
        if (tracked == null)
            return;

        var belongsToBracket = orderId == tracked.ParentOrderId ||
                               orderId == tracked.StopOrderId ||
                               (tracked.ExitOrderId != 0 && orderId == tracked.ExitOrderId);
        if (!belongsToBracket)
            return;

        if (tracked.Status == TrackedOrderStatus.Submitted)
        {
            _store.Clear();
            _client.NotifySubscriber(
                $"BRACKET REJECTED - order {orderId} code {errorCode}: {message} | state cleared, engine resumed.");
        }
        else if (tracked.Status == TrackedOrderStatus.Exiting && orderId == tracked.ExitOrderId)
        {
            _client.NotifySubscriber(
                $"EXIT ORDER REJECTED - order {orderId} code {errorCode}: {message}. Position may still be open, verify and flatten manually.");
        }
        else if (tracked.Status == TrackedOrderStatus.Filled)
        {
            _client.NotifySubscriber(
                $"ORDER REJECTED while position open - order {orderId} code {errorCode}: {message}. Verify protective stop is working.");
        }
    }

    private void ForceExitIfFilled(PriceUpdate lastPrice, string reason)
    {
        var tracked = _store.Load();
        if (tracked != null && tracked.Status == TrackedOrderStatus.Filled)
            BeginExit(tracked, lastPrice, reason);
    }

    private void BeginExit(TrackedOrder tracked, PriceUpdate lastPrice, string reason)
    {
        var isLong = tracked.Direction == TradeDirection.Long;
        var exitAction = isLong ? "SELL" : "BUY";
        var exitPrice = RoundToTick(isLong ? lastPrice.BidPrice : lastPrice.AskPrice);

        var exitOrder = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = exitAction,
            TotalQuantity = tracked.Quantity,
            OrderType = "LMT",
            LmtPrice = exitPrice,
            Tif = "DAY",
            Transmit = true
        };

        tracked.Status = TrackedOrderStatus.Exiting;
        tracked.ExitOrderId = exitOrder.OrderId;
        _store.Save(tracked);

        _client.CancelOrder(tracked.StopOrderId);
        _client.PlaceOrder(_contract, exitOrder);

        _client.NotifySubscriber(
            $"EXITING ({reason}) - cancel stop {tracked.StopOrderId}, exit LMT {exitAction} {exitPrice:F2} id {exitOrder.OrderId}");
    }

    private void HandleExitingStatus(TrackedOrder tracked, OrderStatus status)
    {
        if (status.OrderId == tracked.StopOrderId)
        {
            if (status.Status == "Filled")
            {
                _client.CancelOrder(tracked.ExitOrderId);
                _client.NotifySubscriber(
                    $"EXIT RACE - stop {status.OrderId} filled during timed exit; cancelling exit leg {tracked.ExitOrderId}. Verify position is flat.");
            }
            return;
        }

        if (status.OrderId == tracked.ExitOrderId && status.Status == "Filled")
        {
            tracked.Status = TrackedOrderStatus.Closed;
            tracked.ExitPrice = status.AvgFillPrice;
            tracked.ClosedAt = DateTime.Now;
            _store.Save(tracked);
            _client.NotifySubscriber($"EXIT FILLED - order {status.OrderId} @ {status.AvgFillPrice:F2} | position closed");
            _journal?.Append(tracked);
            _store.Clear();
            return;
        }

        if (status.OrderId == tracked.ExitOrderId &&
            (status.Status == "Cancelled" || status.Status == "Inactive"))
        {
            _client.NotifySubscriber(
                $"EXIT LEG DEAD WITHOUT FILL - order {status.OrderId} {status.Status}. Position may still be open, verify and flatten manually.");
        }
    }

    private static double RoundToTick(double price)
    {
        return Math.Round(Math.Round(price / TickSize) * TickSize, 2);
    }
}
