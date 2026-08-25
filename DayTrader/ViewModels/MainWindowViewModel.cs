using System;
using System.Threading.Tasks;
using DayTrader.Helper;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Helper;

namespace DayTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IbClient _client = new();
    private readonly BufferedLeeReadyClassifier _classifier = new();
    private readonly TickBarAggregator _aggregator = new();
    private readonly BetterVolumeClassifier _volumeClassifier = new(lookback: 20);

    public MainWindowViewModel()
    {
        //_ = InitAndSubscribe();
    }

    private async Task InitAndSubscribe()
    {
        _classifier.OnTradeClassified += _aggregator.OnTrade;
        _aggregator.OnBarUpdated += OnBarUpdated;
        _aggregator.OnBarClosed += OnBarClosed;
        _client.OnNotification += Console.WriteLine;
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
        _client.NotifySubscriber($"ES contract - Exchange:{esFutures.Contract.Exchange} LastDate:{esFutures.Contract.LastTradeDate}");

        _client.SubscribeToPriceTickByTick(1105, esFutures.Contract);
        _client.SubscribeToTradesTickByTick(1106, esFutures.Contract);
    }

    private void OnBarClosed(Bar bar)
    {
        var result = _volumeClassifier.Classify(bar);
    }

    private void OnBarUpdated(Bar bar)
    {
        Console.WriteLine($"Time:{bar.StartTime} Open:{bar.Open}");
    }
}