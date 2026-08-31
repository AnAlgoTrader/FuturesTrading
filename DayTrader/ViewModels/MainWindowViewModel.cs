using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DayTrader.Helper;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Helper;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;
using ReactiveUI;
using Bar = DayTrader.Helper.Bar;

namespace DayTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private const decimal OrderQuantity = 1;

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
    private int _closeTradeInProgress;

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
        _orderHelper.OnOrdersChanged += _ => UpdateOrderLine(_currentMarketPrice);
        _orderHelper.OnPositionChanged += _ => UpdateOrderLine(_currentMarketPrice);

        _volumeClassifier = new BetterVolumeClassifier(_logger, lookBack: 20,
            churnSpreadMultiplier: 1.1,
            absorptionDeltaRatioThreshold: 0.3,
            absorptionSpreadMaxMultiplier: 0.8);

        _ = InitAndSubscribe();
    }

    private void UpdateOrderLine(double currentPrice)
    {
        if (currentPrice > 0)
        {
            _currentMarketPrice = currentPrice;
        }

        var position = _orderHelper.Position;
        if (position.NetQuantity != 0 && position.AveragePrice > 0)
        {
            var entryPrice = position.AveragePrice;
            var isBuy = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase);

            var evalPrice = _currentMarketPrice > 0 ? _currentMarketPrice : entryPrice;
            double pnlPoints = isBuy ? (evalPrice - entryPrice) : (entryPrice - evalPrice);
            double pnlDollar = pnlPoints * 2.0 * (double)Math.Abs(position.NetQuantity);
            var color = pnlDollar >= 0 ? "#26a69a" : "#ef5350";

            string sign = pnlDollar >= 0 ? "+" : "";
            string pnlTitle = $"{sign}${pnlDollar:F2} ({sign}{pnlPoints:F2} pts) @ {position.AveragePrice:F2}";

            OnOrderPriceLine?.Invoke(entryPrice, color, pnlTitle);
        }
        else
        {
            OnOrderPriceLine?.Invoke(0, string.Empty, string.Empty);
        }
    }

    private async Task InitAndSubscribe()
    {
        await _orderHelper.ReadCurrentState();

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
        await SubmitOrder("BUY", OrderQuantity, _bestPrice.AskPrice);
    }

    public async Task OnSell()
    {
        await SubmitOrder("SELL", OrderQuantity, _bestPrice.BidPrice);
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
            var closePrice = string.Equals(closeAction, "BUY", StringComparison.OrdinalIgnoreCase) ? _bestPrice.AskPrice : _bestPrice.BidPrice;

            _logger.WriteLine($"[TRADE] CloseActiveTrade: closing {position.Direction} {closeQty} @ {position.AveragePrice:F2} with {closeAction} {closeQty}");
            await SubmitOrder(closeAction, closeQty, closePrice);
            _logger.WriteLine("[TRADE] CloseActiveTrade: close order submitted.");
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
            Tif = "GTC"
        };
        await _orderHelper.PersistOrder(order);
        _client.PlaceOrder(_tradingContract?.Contract, order);
        _logger.WriteLine($"[TRADE] OrderId:{order.OrderId} - Action:{order.Action} - Quantity:{order.TotalQuantity} - EntryPrice:{order.LmtPrice}");
    }
}