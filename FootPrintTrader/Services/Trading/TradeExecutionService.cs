using System;
using System.Threading;
using System.Threading.Tasks;
using FootPrintTrader.Configuration;
using FootPrintTrader.Services.Logging;
using FootPrintTrader.Services.MarketData;
using IBApi;

namespace FootPrintTrader.Services.Trading;

public class TradeExecutionService
{
    private readonly MarketDataService _marketData;
    private readonly OrderManager _orderManager;
    private readonly MarketProfile _market;
    private readonly Logger _logger;
    private int _closeTradeInProgress;

    public TradeExecutionService(
        MarketDataService marketData,
        OrderManager orderManager,
        MarketProfile market,
        Logger logger)
    {
        _marketData = marketData;
        _orderManager = orderManager;
        _market = market;
        _logger = logger;
    }

    public async Task SubmitOrderAsync(string action, decimal quantity, double price)
    {
        var tradeContract = _marketData.TradeContract;
        if (tradeContract == null)
        {
            _logger?.WriteLine($"[TRADE] SubmitOrder {action} REJECTED: Trade contract is not initialized.");
            return;
        }

        if (price <= 0)
        {
            _logger?.WriteLine($"[TRADE] SubmitOrder {action} REJECTED: Invalid price {price:F2}.");
            return;
        }

        var order = new Order
        {
            OrderId = _marketData.Client.GetNextValidId(),
            Action = action,
            TotalQuantity = quantity,
            OrderType = "LMT",
            LmtPrice = price,
            Transmit = true,
            Tif = "DAY"
        };

        await _orderManager.PersistOrder(order);
        _marketData.Client.PlaceOrder(tradeContract.Contract, order);
        _logger?.WriteLine($"[TRADE] OrderId:{order.OrderId} - Action:{order.Action} - Quantity:{order.TotalQuantity} - EntryPrice:{order.LmtPrice}");
    }

    public Task BuyAsync()
    {
        var price = _marketData.Ask > 0 ? _marketData.Ask : _marketData.CurrentPrice;
        return SubmitOrderAsync("BUY", _market.OrderQuantity, price);
    }

    public Task SellAsync()
    {
        var price = _marketData.Bid > 0 ? _marketData.Bid : _marketData.CurrentPrice;
        return SubmitOrderAsync("SELL", _market.OrderQuantity, price);
    }

    public async Task CloseActiveTradeAsync()
    {
        if (Interlocked.Exchange(ref _closeTradeInProgress, 1) == 1)
        {
            _logger?.WriteLine("[TRADE] CloseActiveTrade SKIPPED - another close is already in progress.");
            return;
        }

        try
        {
            var position = _orderManager.Position;
            if (position.NetQuantity == 0)
            {
                _logger?.WriteLine("[TRADE] CloseActiveTrade requested but position is flat.");
                return;
            }

            var closeQty = Math.Abs(position.NetQuantity);
            var closeAction = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
            var closePrice = string.Equals(closeAction, "BUY", StringComparison.OrdinalIgnoreCase) ? _marketData.Ask : _marketData.Bid;

            _logger?.WriteLine($"[TRADE] CloseActiveTrade: closing {position.Direction} {closeQty} @ {position.AveragePrice:F2} with {closeAction} {closeQty}");
            await SubmitOrderAsync(closeAction, closeQty, closePrice);
        }
        finally
        {
            Interlocked.Exchange(ref _closeTradeInProgress, 0);
        }
    }
}
