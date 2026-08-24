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
    private readonly OrderStore _store;
    private TrackedOrder _lastTradeSnapshot;
    private PriceUpdate _lastPrice;

    public event Action<Candle> OnCandleUpdated;
    public event Action<TrackedOrder> OnTrackedOrderChanged;

    public MainWindowViewModel()
    {
        _logger = new Logger(Path.Combine(AppContext.BaseDirectory, "ui.log"));
        _store = new OrderStore(Path.Combine(AppContext.BaseDirectory, "order_state.json"));
        var journal = new TradeJournal(Path.Combine(AppContext.BaseDirectory, "trade_journal.csv"));
        _manager = new OrderManager(_client, _store, journal);

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
        _client.OnErrorMessage += HandleOrderError;

        _aggregator.OnCandleClosed += candle =>
        {
            _client.NotifySubscriber(candle.ToString());
            _manager.OnCandleClosed(candle, _lastPrice);
            NotifyTradeChanged();
            var signal = _detector.OnCandleClosed(candle);
            if (signal != null)
            {
                _client.NotifySubscriber(signal.ToString());
                _manager.OnSignal(signal, contractDetails.Contract, _lastPrice);
                NotifyTradeChanged();
            }
        };

        _client.SubscribeToMarketData(1005, contractDetails);
        _client.NotifySubscriber("Trading engine running. Press Ctrl+C in console or close window to exit.");
        NotifyTradeChanged();
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
        NotifyTradeChanged();
    }

    private void HandleOrderError(int orderId, int errorCode, string message)
    {
        _manager.OnOrderRejected(orderId, errorCode, message);
        NotifyTradeChanged();
    }

    private void NotifyTradeChanged()
    {
        var tracked = _store.Load();
        if (_lastTradeSnapshot == null && tracked == null)
            return;
        if (_lastTradeSnapshot != null && tracked != null &&
            _lastTradeSnapshot.Status == tracked.Status &&
            _lastTradeSnapshot.Direction == tracked.Direction &&
            _lastTradeSnapshot.FilledAt == tracked.FilledAt &&
            Math.Abs(_lastTradeSnapshot.StopPrice - tracked.StopPrice) < 0.001 &&
            Math.Abs(_lastTradeSnapshot.AverageFillPrice - tracked.AverageFillPrice) < 0.001)
            return;
        _lastTradeSnapshot = tracked;
        OnTrackedOrderChanged?.Invoke(tracked);
    }
}
