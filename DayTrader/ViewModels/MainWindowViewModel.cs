using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;
using DayTrader.Helper;
using DayTrader.Model;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace DayTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public event Action<string>? OnInvokeScript;
    public event Action<double, string, string>? OnOrderPriceLine;
    public ReactiveCommand<RxVoid, RxVoid> OnEnterButtonClick { get; }
    private readonly IbClient _client = new();
    private ContractDetails? _qqqContract;
    private readonly VwapCalculator _vwapCalculator;
    private readonly CandleAggregator _candleAggregator;
    private readonly Logger _logger;
    private readonly OrderHelper _orderHelper;
    private double _currentPrice;
    private int _closeTradeInProgress;
    private const decimal OrderQuantity = 25;
    public AccountInfo AccountInfo { get; } = new();
    public MarketPriceUpdate MarketPriceUpdate { get; } = new();

    public MainWindowViewModel()
    {
        _logger = new Logger();
        _orderHelper = new OrderHelper(_logger);
        _orderHelper.OnOrdersChanged += _ => UpdateOrderLine();
        _orderHelper.OnPositionChanged += _ => UpdateOrderLine();
        _vwapCalculator = new VwapCalculator();
        _candleAggregator = new CandleAggregator(_vwapCalculator);
        _candleAggregator.OnCandleUpdate += (candleTime, open, high, low, close, vwap) =>
        {
            var time = new DateTimeOffset(candleTime).ToUnixTimeSeconds();
            OnInvokeScript?.Invoke($"window.updateData({time}, {open}, {high}, {low}, {close}, {vwap})");
        };
        OnEnterButtonClick = ReactiveCommand.Create(OnButtonClicked);
        _ = InitAndSubscribe();
    }

    private void UpdateOrderLine()
    {
        var position = _orderHelper.Position;
        if (position.NetQuantity != 0 && position.AveragePrice > 0)
        {
            var entryPrice = position.AveragePrice;
            var isBuy = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase);

            var evalPrice = _currentPrice > 0 ? _currentPrice : entryPrice;
            double pnlDollar = isBuy
                ? (evalPrice - entryPrice) * (double)position.NetQuantity
                : (entryPrice - evalPrice) * (double)Math.Abs(position.NetQuantity);
            var color = pnlDollar >= 0 ? "#26a69a" : "#ef5350";

            string sign = pnlDollar >= 0 ? "+" : "";
            string pnlTitle = $"{sign}${pnlDollar:F2} @ {position.AveragePrice:F2}";

            OnOrderPriceLine?.Invoke(entryPrice, color, pnlTitle);
        }
        else
        {
            OnOrderPriceLine?.Invoke(0, string.Empty, string.Empty);
        }
    }

    public async Task CloseActiveTrade()
    {
        if (Interlocked.Exchange(ref _closeTradeInProgress, 1) == 1)
        {
            _logger.WriteLine("[TRADE] CloseActiveTrade SKIPPED - another close is already in progress.");
            return;
        }

        try
        {
            var position = _orderHelper.Position;
            if (position.NetQuantity == 0)
            {
                _logger.WriteLine("[TRADE] CloseActiveTrade requested but position is flat.");
                return;
            }

            var closeQty = Math.Abs(position.NetQuantity);
            var closeAction = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
            var closePrice = string.Equals(closeAction, "BUY", StringComparison.OrdinalIgnoreCase) ? MarketPriceUpdate.Ask : MarketPriceUpdate.Bid;

            _logger.WriteLine($"[TRADE] CloseActiveTrade: closing {position.Direction} {closeQty} @ {position.AveragePrice:F2} with {closeAction} {closeQty}");
            await SubmitOrder(closeAction, closeQty, closePrice);
        }
        finally
        {
            Interlocked.Exchange(ref _closeTradeInProgress, 0);
        }
    }

    private async Task SubmitOrder(string action, decimal quantity, double price)
    {
        var order = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = action,
            TotalQuantity = quantity,
            OrderType = "LMT",
            LmtPrice = price,
            Transmit = true,
            Tif = "DAY"
        };
        await _orderHelper.PersistOrder(order);
        _client.PlaceOrder(_qqqContract?.Contract, order);
        _logger.WriteLine($"[TRADE] OrderId:{order.OrderId} - Action:{order.Action} - Quantity:{order.TotalQuantity} - EntryPrice:{order.LmtPrice}");
    }

    private async Task InitAndSubscribe()
    {
        await _orderHelper.ReadCurrentState();

        _client.OnNotification += msg => _logger.WriteLine($"[IB] {msg}");
        _client.Start("127.0.0.1", 4001, 0);
        _client.OnAccountUpdate += accountUpdate =>
        {
            AccountInfo.UpdateOn = accountUpdate.UpdateOn;
            AccountInfo.AvailableFunds = accountUpdate.AvailableFunds;
            AccountInfo.CashEuro = accountUpdate.CashEuro;
            AccountInfo.CashUsd = accountUpdate.CashUsd;
            AccountInfo.BuyingPower = accountUpdate.BuyingPower;
        };
        _client.OnMarketDataPriceUpdate += updates =>
        {
            var marketPrice = updates.First().Value;
            _currentPrice = marketPrice.BidPrice > 0 && marketPrice.AskPrice > 0
                ? (marketPrice.BidPrice + marketPrice.AskPrice) / 2.0
                : (marketPrice.BidPrice > 0 ? marketPrice.BidPrice : marketPrice.AskPrice);
            MarketPriceUpdate.Bid = marketPrice.BidPrice;
            MarketPriceUpdate.Ask = marketPrice.AskPrice;
            UpdateOrderLine();
        };
        _client.OnAccountListUpdate += _client.SubscribeToPortfolioUpdates;
        _client.OnRealTimeBarUpdate += _candleAggregator.AddBar;
        _client.OnOrderStatusUpdate += status => _ = _orderHelper.UpdateOrderStatus(status);
        _qqqContract = await ContractHelper.GetQQQContract(_client);
        await DisplayHistoricalData();
        _client.SubscribeToRealTimeBars(1106, _qqqContract.Contract);
        _client.SubscribeToMarketData(1107, _qqqContract);
    }

    private async Task DisplayHistoricalData()
    {
        var historicalData = await _client.GetHistoricalData(1105, _qqqContract!.Contract, $"3 {nameof(Duration.D)}", $"1 {nameof(BarSize.min)}", "TRADES", 600000);

        foreach (var item in historicalData)
        {
            var time = long.Parse(item.Time);
            var vwap = _vwapCalculator.AddBar(time, (double)item.WAP, (double)item.Volume);
            OnInvokeScript?.Invoke($"window.updateData({item.Time}, {item.Open}, {item.High}, {item.Low}, {item.Close}, {vwap})");
        }
    }

    private void OnButtonClicked()
    {
        _ = SubmitOrder("BUY", OrderQuantity, MarketPriceUpdate.Ask);
    }
}
