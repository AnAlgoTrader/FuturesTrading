using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;

namespace UI.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IbClient _client = new();
    private readonly CandleAggregator _aggregator = new();
    private readonly SignalDetector _detector = new(windowSize: 1000, entryThreshold: 2.0);
    private readonly OrderManager _manager;
    private readonly Logger _logger;
    private PriceUpdate _lastPrice;

    public event Action<Candle> OnCandleUpdated;

    public MainWindowViewModel()
    {
        _logger = new Logger(Path.Combine(AppContext.BaseDirectory, "ui.log"));
        var store = new OrderStore(Path.Combine(AppContext.BaseDirectory, "order_state.json"));
        _manager = new OrderManager(_client, store);

        _client.OnNotification += msg =>
        {
            Console.WriteLine(msg);
            _logger.WriteLine($"[IB] {msg}");
        };

        _aggregator.OnCandleUpdated += candle => OnCandleUpdated?.Invoke(candle);

        _ = InitAndSubscribe();
    }

    private async Task InitAndSubscribe()
    {
        _client.OnMarketDataPriceUpdate += HandleMarketUpdate;
        _client.Start("127.0.0.1", 4001, 0);

        var contractDetails = await FuturesContract.GetFrontMonth(_client, "MES");
        if (contractDetails == null)
        {
            _client.NotifySubscriber("No valid MES contract found.");
            return;
        }

        _client.NotifySubscriber($"Using contract - Exchange:{contractDetails.Contract.Exchange} LastDate:{contractDetails.Contract.LastTradeDate}");

        _client.OnOrderStatusUpdate += HandleOrderStatus;

        _aggregator.OnCandleClosed += candle =>
        {
            _client.NotifySubscriber(candle.ToString());
            _manager.OnCandleClosed(candle, _lastPrice);
            var signal = _detector.OnCandleClosed(candle);
            if (signal != null)
            {
                _client.NotifySubscriber(signal.ToString());
                _manager.OnSignal(signal, contractDetails.Contract, _lastPrice);
            }
        };

        _client.SubscribeToMarketData(1005, contractDetails);
        _client.NotifySubscriber("Trading engine running. Press Ctrl+C in console or close window to exit.");
    }

    private void HandleMarketUpdate(Dictionary<int, PriceUpdate> marketDataUpdate)
    {
        var update = marketDataUpdate.First();
        if (update.Value.BidPrice <= 0 || update.Value.AskPrice <= 0)
            return;
        _lastPrice = update.Value;
        _aggregator.OnTick(DateTime.Now, update.Value);
    }

    private void HandleOrderStatus(OrderStatus status)
    {
        _client.NotifySubscriber($"OrderStatus - Id:{status.OrderId} Status:{status.Status} Filled:{status.Filled} Avg:{status.AvgFillPrice:F2}");
        _manager.OnOrderStatus(status);
    }
}
