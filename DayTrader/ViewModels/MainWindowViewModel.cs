using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;
using DayTrader.Helper;
using DayTrader.Models;
using DayTrader.Services;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace DayTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public event Action<string>? OnInvokeScript;
    public event Action<double, string, string, double>? OnOrderPriceLine;
    public ReactiveCommand<RxVoid, RxVoid> OnEnterButtonClick { get; }
    private readonly IbClient _client = new();
    private readonly VwapCalculator _vwapCalculator;
    private readonly CandleAggregator _candleAggregator;
    private readonly Logger _logger;
    private readonly OrderHelper _orderHelper;
    private readonly IDisposable _sidePanelScheduler;
    private double _currentTradePrice;
    private double _currentChartPrice;
    private int _closeTradeInProgress;
    private const decimal OrderQuantity = 1;
    private const int ChartMarketDataRequestId = 1108;
    private const int TradeMarketDataRequestId = 1107;
    public AccountInfo AccountInfo { get; } = new();
    public MarketPriceUpdate MarketPriceUpdate { get; } = new();
    public SidePanelInfo SidePanelInfo { get; set; } = new();
    private readonly YahooDataService _yahooDataService = new();
    
    private ContractDetails? _chartContract;
    private ContractDetails? _tradeContract;

    private double TradeMultiplier =>
        double.TryParse(_tradeContract?.Contract.Multiplier, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : 1;

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
        _sidePanelScheduler = ObservableExtensions.Subscribe(Observable.Timer(TimeSpan.Zero, TimeSpan.FromMinutes(1)), async _ => await HandleSidePanelSchedulerTick());
        _ = InitAndSubscribe();
    }

    private async Task HandleSidePanelSchedulerTick()
    {
        var data = await _yahooDataService.GetMarketRegimeAsync();
        SidePanelInfo.Vix = Math.Round(data.Vix, 2);
        SidePanelInfo.Vvix = Math.Round(data.Vvix, 2);
        SidePanelInfo.IvPercentile = data.IvPercentile;
        SidePanelInfo.IvRank = data.IvRank;
        SidePanelInfo.Rv5 = data.Rv5;
        SidePanelInfo.Vrp = data.Vrp;
        SidePanelInfo.VrpZScore = data.VrpZScore;
        SidePanelInfo.VvixPercentile = data.VvixPercentile;
    }

    private void UpdateOrderLine()
    {
        var position = _orderHelper.Position;
        if (position.NetQuantity != 0 && position.AveragePrice > 0)
        {
            var entryPrice = position.AveragePrice;
            var isBuy = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase);

            var evalPrice = _currentTradePrice > 0 ? _currentTradePrice : entryPrice;
            double pnlDollar = isBuy
                ? (evalPrice - entryPrice) * (double)position.NetQuantity * TradeMultiplier
                : (entryPrice - evalPrice) * (double)Math.Abs(position.NetQuantity) * TradeMultiplier;
            var color = pnlDollar >= 0 ? "#26a69a" : "#ef5350";

            string sign = pnlDollar >= 0 ? "+" : "";
            string pnlTitle = $"{sign}${pnlDollar:F2} @ {position.AveragePrice:F2}";

            OnOrderPriceLine?.Invoke(entryPrice, color, pnlTitle, position.ChartEntryPrice);
        }
        else OnOrderPriceLine?.Invoke(0, string.Empty, string.Empty, 0);
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
        _client.PlaceOrder(_tradeContract?.Contract, order);
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
            foreach (var (key, marketPrice) in updates)
            {
                switch (key)
                {
                    case ChartMarketDataRequestId:
                        _currentChartPrice = marketPrice.BidPrice > 0 && marketPrice.AskPrice > 0
                            ? (marketPrice.BidPrice + marketPrice.AskPrice) / 2.0
                            : (marketPrice.BidPrice > 0 ? marketPrice.BidPrice : marketPrice.AskPrice);
                        _orderHelper.CurrentChartPrice = _currentChartPrice;
                        break;
                    case TradeMarketDataRequestId:
                        _currentTradePrice = marketPrice.BidPrice > 0 && marketPrice.AskPrice > 0
                            ? (marketPrice.BidPrice + marketPrice.AskPrice) / 2.0
                            : (marketPrice.BidPrice > 0 ? marketPrice.BidPrice : marketPrice.AskPrice);
                        MarketPriceUpdate.Bid = marketPrice.BidPrice;
                        MarketPriceUpdate.Ask = marketPrice.AskPrice;
                        break;
                }
            }
            UpdateOrderLine();
        };
        _client.OnAccountListUpdate += _client.SubscribeToPortfolioUpdates;
        _client.OnRealTimeBarUpdate += _candleAggregator.AddBar;
        _client.OnOrderStatusUpdate += status => _ = _orderHelper.UpdateOrderStatus(status);
        _chartContract = await ContractHelper.GetChartContract(_client);
        _tradeContract = await ContractHelper.GetTradeContract(_client);
        await DisplayHistoricalData();
        _client.SubscribeToRealTimeBars(1106, _chartContract.Contract);

        if (TradingConfig.IsSingleInstrument)
        {
            _client.SubscribeToMarketData(TradeMarketDataRequestId, _chartContract);
        }
        else
        {
            _client.SubscribeToMarketData(ChartMarketDataRequestId, _chartContract);
            _client.SubscribeToMarketData(TradeMarketDataRequestId, _tradeContract);
        }
    }

    private async Task DisplayHistoricalData()
    {
        var historicalData = await _client.GetHistoricalData(1105, _chartContract!.Contract, $"3 {nameof(Duration.D)}", $"1 {nameof(BarSize.min)}", "TRADES", 600000);

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
