using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DayTrader.Helper;
using DayTrader.Models;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Helper;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;
using ReactiveUI;
using Bar = DayTrader.Helper.Bar;

namespace DayTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IbClient _client = new();
    private readonly BufferedLeeReadyClassifier _classifier = new();
    private readonly TickBarAggregator _aggregator = new();
    private readonly BetterVolumeClassifier _volumeClassifier;
    private readonly Logger _logger;

    public event Action<Bar, string> OnBarPush;
    public event Action<double, string, string> OnOrderPriceLine;

    private ClassificationResult _classification = ClassificationResult.Default;
    private PriceUpdate _bestPrice;
    private ContractDetails _tradingContract;
    private readonly OrderHelper _orderHelper;

    private double _currentMarketPrice;

    public ClassificationResult Classification
    {
        get => _classification;
        set => this.RaiseAndSetIfChanged(ref _classification, value);
    }

    public MainWindowViewModel()
    {
        _logger = new Logger();
        _orderHelper = new OrderHelper(_logger);
        _orderHelper.OnOrdersChanged += HandleOrdersChanged;
        
        _volumeClassifier = new BetterVolumeClassifier(_logger, lookBack: 20,
            churnSpreadMultiplier: 1.1,
            absorptionDeltaRatioThreshold: 0.3,
            absorptionSpreadMaxMultiplier: 0.8);

        _ = InitAndSubscribe();
    }

    private void HandleOrdersChanged(IReadOnlyList<OrderPersistence> orders)
    {
        UpdateOrderLine(_currentMarketPrice);
    }

    private void UpdateOrderLine(double currentPrice)
    {
        if (currentPrice > 0)
        {
            _currentMarketPrice = currentPrice;
        }

        var orders = _orderHelper.Orders;
        if (orders != null && orders.Count == 1 && string.Equals(orders[0].Status, "Filled", StringComparison.OrdinalIgnoreCase))
        {
            var order = orders[0];
            var entryPrice = order.LmtPrice;
            var isBuy = string.Equals(order.Action, "BUY", StringComparison.OrdinalIgnoreCase);
            var color = isBuy ? "#26a69a" : "#ef5350";

            var evalPrice = _currentMarketPrice > 0 ? _currentMarketPrice : entryPrice;
            double pnlPoints = isBuy ? (evalPrice - entryPrice) : (entryPrice - evalPrice);
            double pnlDollar = pnlPoints * 2.0 * (double)order.TotalQuantity;

            string sign = pnlDollar >= 0 ? "+" : "";
            string pnlTitle = $"{sign}${pnlDollar:F2} ({sign}{pnlPoints:F2} pts)";

            OnOrderPriceLine?.Invoke(entryPrice, color, pnlTitle);
        }
        else
        {
            OnOrderPriceLine?.Invoke(0, string.Empty, string.Empty);
        }
    }

    private async Task InitAndSubscribe()
    {
        await _orderHelper.ReadCurrentOrders();

        _client.OnNotification += msg => _logger.WriteLine($"[IB] {msg}");
        _client.Start("127.0.0.1", 4001, 0);

        var futuresContract = await ContractHelper.GetFrontMonth(_client, "NQ");
        _tradingContract = await ContractHelper.GetFrontMonth(_client, "MNQ");
        _logger.WriteLine($"Futures contract  Symbol:{futuresContract.Contract.Symbol} Exchange:{futuresContract.Contract.Exchange} LastDate:{futuresContract.Contract.LastTradeDate}");

        // 1. Fetch historical data and wait for it to complete
        try
        {
            _logger.WriteLine("[SEED] Requesting historical data...");
            var historicalData = await _client.GetHistoricalData(
                1104,
                futuresContract.Contract,
                $"1 {nameof(Duration.D)}",
                $"5 {nameof(BarSize.secs)}",
                "TRADES",
                60000,
                useRth: false);

            if (historicalData is { Count: > 0 })
            {
                var historicalBars = HistoricalSeeder.BuildTickBars(historicalData, ticksPerBar: 1000);
                _logger.WriteLine($"[SEED] Received {historicalData.Count} 5-sec bars => built {historicalBars.Count} 1000-tick candles.");

                foreach (var histBar in historicalBars)
                {
                    var result = _volumeClassifier.Classify(histBar);
                    OnBarPush?.Invoke(histBar, result.Signal.ToColor());
                    Classification = result;
                }

                if (historicalBars.Count > 0)
                {
                    _aggregator.SetLastChartTime(historicalBars[^1].ChartTime);
                }
            }
            else
            {
                _logger.WriteLine("[SEED] No historical bars returned.");
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[SEED] Historical data warmup failed: {ex.Message}");
        }

        // 2. Wire up live stream handlers after historical data has finished processing
        _classifier.OnTradeClassified += _aggregator.OnTrade;
        _aggregator.OnBarUpdated += OnBarUpdated;
        _aggregator.OnBarClosed += OnBarClosed;
        _client.OnMarketDataPriceTickByTickUpdate += priceUpdate =>
        {
            _classifier.OnQuote(priceUpdate.Time, priceUpdate.Bid, priceUpdate.Ask);
            _classifier.Pump();
        };
        _client.OnMarketDataTradeTickByTickUpdate += tradeUpdate =>
        {
            if (tradeUpdate.TickAttribLast.PastLimit) return;
            _classifier.OnTrade(tradeUpdate.Time, tradeUpdate.Price, tradeUpdate.Size);
            _classifier.Pump();
        };
        _client.OnMarketDataPriceUpdate += priceUpdate =>
        {
            _bestPrice = priceUpdate.First().Value;
        };
        _client.OnOrderStatusUpdate += async status =>
        {
            await _orderHelper.UpdateOrderStatus(status);
        };

        // 3. Subscribe to live tick-by-tick market data
        _logger.WriteLine("[LIVE] Subscribing to live tick market data...");
        _client.SubscribeToPriceTickByTick(1105, futuresContract.Contract);
        _client.SubscribeToTradesTickByTick(1106, futuresContract.Contract);
        _client.SubscribeToMarketData(1107, futuresContract);
    }

    private void OnBarClosed(Bar bar)
    {
        var result = _volumeClassifier.Classify(bar);
        _logger.WriteLine($"[BAR CLOSED] Vol:{bar.Volume} Spread:{bar.Spread:F4} Delta:{bar.Delta} DeltaRatio:{bar.DeltaRatio:F4} " +
                          $"O:{bar.Open} H:{bar.High} L:{bar.Low} C:{bar.Close} Ticks:{bar.TickCount} => Signal:{result.Signal} Color:{result.Signal.ToColor()}");
        OnBarPush?.Invoke(bar, result.Signal.ToColor());

        Classification = result;
        UpdateOrderLine(bar.Close);
    }

    private void OnBarUpdated(Bar bar)
    {
        OnBarPush?.Invoke(bar, VolumeSignal.Normal.ToColor());
        UpdateOrderLine(bar.Close);
    }

    public async Task OnBuy()
    {
        var order = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = "BUY",
            TotalQuantity = 1,
            OrderType = "LMT",
            LmtPrice = _bestPrice.AskPrice,
            Transmit = true,
            Tif = "GTC"
        };
        await _orderHelper.PersistOrder(order);
        _client.PlaceOrder(_tradingContract?.Contract, order);
        _logger.WriteLine($"[TRADE] OrderId:{order.OrderId} - Action:{order.Action} - Quantity:{order.TotalQuantity} - EntryPrice:{order.LmtPrice}");
    }

    public async Task OnSell()
    {
        var order = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = "SELL",
            TotalQuantity = 1,
            OrderType = "LMT",
            LmtPrice = _bestPrice.BidPrice,
            Transmit = true,
            Tif = "GTC"
        };
        await _orderHelper.PersistOrder(order);
        _client.PlaceOrder(_tradingContract?.Contract, order);
        _logger.WriteLine($"[TRADE] OrderId:{order.OrderId} - Action:{order.Action} - Quantity:{order.TotalQuantity} - EntryPrice:{order.LmtPrice}");
    }
}
