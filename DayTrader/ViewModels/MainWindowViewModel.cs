using System;
using System.IO;
using System.Threading.Tasks;
using Common;
using DayTrader.Helper;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Helper;
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

    private string _signalColor = "#888888";
    public string SignalColor
    {
        get => _signalColor;
        set => this.RaiseAndSetIfChanged(ref _signalColor, value);
    }

    private string _signalName = "Normal";
    public string SignalName
    {
        get => _signalName;
        set => this.RaiseAndSetIfChanged(ref _signalName, value);
    }

    private string _signalDescription = "Unremarkable bar";
    public string SignalDescription
    {
        get => _signalDescription;
        set => this.RaiseAndSetIfChanged(ref _signalDescription, value);
    }

    private string _deltaValue = "Delta: 0";
    public string DeltaValue
    {
        get => _deltaValue;
        set => this.RaiseAndSetIfChanged(ref _deltaValue, value);
    }

    private string _deltaRatio = "Ratio: 0.0000";
    public string DeltaRatioDisplay
    {
        get => _deltaRatio;
        set => this.RaiseAndSetIfChanged(ref _deltaRatio, value);
    }

    private string _deltaStatus = "";
    public string DeltaStatus
    {
        get => _deltaStatus;
        set => this.RaiseAndSetIfChanged(ref _deltaStatus, value);
    }

    private string _deltaStatusColor = "#787b86";
    public string DeltaStatusColor
    {
        get => _deltaStatusColor;
        set => this.RaiseAndSetIfChanged(ref _deltaStatusColor, value);
    }

    private string _streakInfo = "";
    public string StreakInfo
    {
        get => _streakInfo;
        set => this.RaiseAndSetIfChanged(ref _streakInfo, value);
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
        _classifier.OnTradeClassified += _aggregator.OnTrade;
        _aggregator.OnBarUpdated += OnBarUpdated;
        _aggregator.OnBarClosed += OnBarClosed;
        _client.OnNotification += msg => _logger.WriteLine($"[IB] {msg}");
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
        _client.Start("127.0.0.1", 4001, 0);

        var esFutures = await ContractHelper.GetFrontMonth(_client, "ES");
        _logger.WriteLine($"ES contract - Exchange:{esFutures.Contract.Exchange} LastDate:{esFutures.Contract.LastTradeDate}");

        _client.SubscribeToPriceTickByTick(1105, esFutures.Contract);
        _client.SubscribeToTradesTickByTick(1106, esFutures.Contract);
    }

    private void OnBarClosed(Bar bar)
    {
        var result = _volumeClassifier.Classify(bar);
        _logger.WriteLine($"[BAR CLOSED] Vol:{bar.Volume} Spread:{bar.Spread:F4} Delta:{bar.Delta} DeltaRatio:{bar.DeltaRatio:F4} " +
                          $"O:{bar.Open} H:{bar.High} L:{bar.Low} C:{bar.Close} Ticks:{bar.TickCount} => Signal:{result.Signal} Color:{result.Signal.ToColor()}");
        OnBarPush?.Invoke(bar, result.Signal.ToColor());

        SignalColor = result.Signal.ToColor();
        SignalName = result.Signal.GetName();
        SignalDescription = result.Signal.GetDescription();
        DeltaValue = $"Delta: {bar.Delta}";
        DeltaRatioDisplay = $"Ratio: {bar.DeltaRatio:F4}";
        DeltaStatus = result.DeltaConfirmed ? "Confirmed" : "Divergent";
        DeltaStatusColor = result.DeltaConfirmed ? "#26a69a" : "#ef5350";

        if (result.BuyStreak > 0)
            StreakInfo = $"Buy streak: {result.BuyStreak}";
        else if (result.SellStreak > 0)
            StreakInfo = $"Sell streak: {result.SellStreak}";
        else
            StreakInfo = "";
    }

    private void OnBarUpdated(Bar bar)
    {
        OnBarPush?.Invoke(bar, VolumeSignal.Normal.ToColor());
    }
}
