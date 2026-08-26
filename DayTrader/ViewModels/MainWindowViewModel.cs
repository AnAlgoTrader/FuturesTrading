using System;
using System.IO;
using System.Threading.Tasks;
using DayTrader.Helper;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Helper;
using Ib.Csharp.Api.Client.Implementation.Model;
using ReactiveUI;

namespace DayTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IbClient _client = new();
    private readonly BufferedLeeReadyClassifier _classifier = new();
    private readonly TickBarAggregator _aggregator = new();
    private readonly BetterVolumeClassifier _volumeClassifier;
    private readonly Logger _logger;

    public event Action<Bar, string> OnBarPush;

    private ClassificationResult _classification = ClassificationResult.Default;
    public ClassificationResult Classification
    {
        get => _classification;
        set => this.RaiseAndSetIfChanged(ref _classification, value);
    }

    public MainWindowViewModel()
    {
        var dataDir = "/home/oce/LocalDB/logs/futures";
        Directory.CreateDirectory(dataDir);
        _logger = new Logger(Path.Combine(dataDir, "daytrader.log"));
        _volumeClassifier = new BetterVolumeClassifier(_logger, lookback: 20,
            churnSpreadMultiplier: 1.1,
            absorptionDeltaRatioThreshold: 0.3,
            absorptionSpreadMaxMultiplier: 0.8);

        _ = InitAndSubscribe();
    }

    private async Task InitAndSubscribe()
    {
        _client.OnNotification += msg => _logger.WriteLine($"[IB] {msg}");
        _client.Start("127.0.0.1", 4001, 0);

        var futuresContract = await ContractHelper.GetFrontMonth(_client, "NQ");
        _logger.WriteLine($"Futures contract  Symbol:{futuresContract.Contract.Symbol} Exchange:{futuresContract.Contract.Exchange} LastDate:{futuresContract.Contract.LastTradeDate}");

        // 1. Fetch historical data and wait for it to complete
        try
        {
            _logger.WriteLine("[SEED] Requesting historical data...");
            // IB supports up to 1800 S for 1-sec bars. useRth: false ensures non-RTH futures data is included.
            var historicalData = await _client.GetHistoricalData(
                1104,
                futuresContract.Contract,
                $"1 {nameof(Duration.D)}",
                $"5 {nameof(BarSize.secs)}",
                "TRADES",
                20000,
                useRth: false);

            if (historicalData is { Count: > 0 })
            {
                var historicalBars = HistoricalSeeder.BuildTickBars(historicalData, ticksPerBar: 1000);
                _logger.WriteLine($"[SEED] Received {historicalData.Count} 1-sec bars => built {historicalBars.Count} 1000-tick candles.");

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

        // 3. Subscribe to live tick-by-tick market data
        _logger.WriteLine("[LIVE] Subscribing to live tick market data...");
        _client.SubscribeToPriceTickByTick(1105, futuresContract.Contract);
        _client.SubscribeToTradesTickByTick(1106, futuresContract.Contract);
    }

    private void OnBarClosed(Bar bar)
    {
        var result = _volumeClassifier.Classify(bar);
        _logger.WriteLine($"[BAR CLOSED] Vol:{bar.Volume} Spread:{bar.Spread:F4} Delta:{bar.Delta} DeltaRatio:{bar.DeltaRatio:F4} " +
                          $"O:{bar.Open} H:{bar.High} L:{bar.Low} C:{bar.Close} Ticks:{bar.TickCount} => Signal:{result.Signal} Color:{result.Signal.ToColor()}");
        OnBarPush?.Invoke(bar, result.Signal.ToColor());

        Classification = result;
    }

    private void OnBarUpdated(Bar bar)
    {
        OnBarPush?.Invoke(bar, VolumeSignal.Normal.ToColor());
    }

    public void OnBuy()
    {
        Console.WriteLine("[TRADE] Buy button clicked");
        _logger.WriteLine("[TRADE] Buy button clicked");
    }

    public void OnSell()
    {
        Console.WriteLine("[TRADE] Sell button clicked");
        _logger.WriteLine("[TRADE] Sell button clicked");
    }
}
