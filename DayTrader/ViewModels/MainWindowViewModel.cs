using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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
    private const decimal OrderQuantity = 5;

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
    private int _isClosingTrade;

    private double Multiplier =>
        _tradingContract?.Contract?.Multiplier != null &&
        double.TryParse(_tradingContract.Contract.Multiplier, NumberStyles.Any, CultureInfo.InvariantCulture, out var mult) &&
        mult > 0
            ? mult
            : 1.0;

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

        var position = _orderHelper.GetPosition();
        if (position.NetQuantity != 0)
        {
            var entryPrice = position.AveragePrice;
            var isBuy = position.NetQuantity > 0;

            var evalPrice = _currentMarketPrice > 0 ? _currentMarketPrice : entryPrice;
            double pnlPoints = isBuy ? (evalPrice - entryPrice) : (entryPrice - evalPrice);
            double pnlDollar = pnlPoints * Multiplier * (double)position.Quantity;
            var color = pnlDollar >= 0 ? "#26a69a" : "#ef5350";

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
        _logger.WriteLine($"Futures contract  Symbol:{futuresContract.Contract.Symbol} Exchange:{futuresContract.Contract.Exchange} LastDate:{futuresContract.Contract.LastTradeDate} Multiplier:{_tradingContract?.Contract?.Multiplier}");

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

    public async Task OnBuy(decimal quantity = OrderQuantity)
    {
        double limitPrice = _bestPrice != null && _bestPrice.AskPrice > 0
            ? _bestPrice.AskPrice
            : _currentMarketPrice;

        var order = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = "BUY",
            TotalQuantity = quantity,
            OrderType = "LMT",
            LmtPrice = limitPrice,
            Transmit = true,
            Tif = "GTC"
        };
        await _orderHelper.PersistOrder(order);
        _client.PlaceOrder(_tradingContract?.Contract, order);
        _logger.WriteLine($"[TRADE] OrderId:{order.OrderId} - Action:{order.Action} - Quantity:{order.TotalQuantity} - EntryPrice:{order.LmtPrice}");
    }

    public async Task OnSell(decimal quantity = OrderQuantity)
    {
        double limitPrice = _bestPrice != null && _bestPrice.BidPrice > 0
            ? _bestPrice.BidPrice
            : _currentMarketPrice;

        var order = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = "SELL",
            TotalQuantity = quantity,
            OrderType = "LMT",
            LmtPrice = limitPrice,
            Transmit = true,
            Tif = "GTC"
        };
        await _orderHelper.PersistOrder(order);
        _client.PlaceOrder(_tradingContract?.Contract, order);
        _logger.WriteLine($"[TRADE] OrderId:{order.OrderId} - Action:{order.Action} - Quantity:{order.TotalQuantity} - EntryPrice:{order.LmtPrice}");
    }

    public async Task CloseActiveTrade()
    {
        if (Interlocked.CompareExchange(ref _isClosingTrade, 1, 0) != 0)
        {
            _logger.WriteLine("[TRADE] CloseActiveTrade ignored: close operation already in progress.");
            return;
        }

        try
        {
            var position = _orderHelper.GetPosition();
            if (position.NetQuantity == 0)
            {
                _logger.WriteLine("[TRADE] CloseActiveTrade requested but net position is flat.");
                return;
            }

            _logger.WriteLine($"[TRADE] CloseActiveTrade requested for net {position.Quantity} {position.Direction} position.");
            if (position.NetQuantity > 0)
            {
                await OnSell(position.Quantity);
            }
            else
            {
                await OnBuy(position.Quantity);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isClosingTrade, 0);
        }
    }
}
