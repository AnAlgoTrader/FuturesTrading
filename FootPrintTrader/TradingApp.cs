using System;
using System.Threading;
using System.Threading.Tasks;
using FootPrintTrader.Configuration;
using FootPrintTrader.Domain.Footprint;
using FootPrintTrader.Services.Export;
using FootPrintTrader.Services.Logging;
using FootPrintTrader.Services.MarketData;
using FootPrintTrader.Services.Options;
using FootPrintTrader.Services.Trading;
using FootPrintTrader.UI;
using FootPrintTrader.UI.Models;
using FootPrintTrader.UI.Rendering;

namespace FootPrintTrader;

public class TradingApp
{
    private const string DataDirectory = "/home/oce/LocalDB/logs/footprint";

    private readonly MarketProfile _market;
    private readonly FootprintClassifier _classifier;
    private readonly FootprintAnalyzer _analyzer;
    private readonly MarketDataService _marketData;
    private readonly OrderManager _orderManager;
    private readonly TradeExecutionService _tradeExecution;
    private readonly OptionsService _optionsService;
    private readonly SnapshotService _snapshotService;
    private readonly WindowManager _windowManager;

    private Timer _futuresTimer;
    private Timer _optionsTimer;

    public TradingApp(MarketProfile market = null)
    {
        _market = market ?? MarketPresets.EsMes;
        var logger = new Logger(DataDirectory);
        _classifier = new FootprintClassifier(tickSize: _market.TickSize, tradesPerBar: _market.TradesPerBar);
        _analyzer = new FootprintAnalyzer();

        _marketData = new MarketDataService(_market, _classifier, logger);
        _orderManager = new OrderManager(logger);
        _tradeExecution = new TradeExecutionService(_marketData, _orderManager, _market, logger);
        _optionsService = new OptionsService(logger);
        _snapshotService = new SnapshotService(DataDirectory, _market, logger, _analyzer);
        _windowManager = new WindowManager();

        WireEvents();
    }

    private void WireEvents()
    {
        _orderManager.OnOrdersChanged += _ => SendPositionUpdate();
        _orderManager.OnPositionChanged += _ => SendPositionUpdate();

        _marketData.OnQuoteUpdated += (bid, ask) =>
        {
            _windowManager.SendFuturesUpdate(UpdateMessageType.MARKET_DATA, new MarketDataUpdate { Bid = bid, Ask = ask });
            SendPositionUpdate();
        };

        _marketData.OnPriceUpdated += price =>
        {
            _orderManager.CurrentChartPrice = price;
            SendPositionUpdate();
        };

        _marketData.OnOrderStatusUpdated += status => _ = _orderManager.UpdateOrderStatus(status);

        _optionsService.OnOptionsChainUpdated += chain =>
            _windowManager.SendOptionsUpdate(UpdateMessageType.OPTIONS_CHAIN, chain);

        _optionsService.OnOptionsError += msg =>
            _windowManager.SendOptionsUpdate(UpdateMessageType.OPTIONS_ERROR, new OptionsErrorUpdate { Message = msg });

        _optionsService.OnSpreadQuoteUpdated += quote =>
            _windowManager.SendOptionsUpdate(UpdateMessageType.OPTIONS_SPREAD_QUOTE, quote);

        _optionsService.OnSpreadStatusChanged += status =>
            _windowManager.SendOptionsUpdate(UpdateMessageType.OPTIONS_SPREAD_STATUS, status);

        _windowManager.OnApplicationReady += () =>
        {
            _windowManager.SendFuturesUpdate(UpdateMessageType.MARKET_INFO, new MarketInfo
            {
                DisplayName = _market.DisplayName,
                DataSymbol = _market.DataSymbol,
                TradeSymbol = _market.TradeSymbol,
                Exchange = _market.Exchange,
                PriceDecimals = _market.PriceDecimals,
                TradesPerBar = _market.TradesPerBar,
                CurrencySign = _market.CurrencySign ?? "$"
            });

            _ = InitAndSubscribeAsync();
            _windowManager.OpenOptionsWindow(_market.DisplayName);
        };

        _windowManager.OnBuyRequested += () => _ = _tradeExecution.BuyAsync();
        _windowManager.OnSellRequested += () => _ = _tradeExecution.SellAsync();
        _windowManager.OnCloseTradeRequested += () => _ = _tradeExecution.CloseActiveTradeAsync();

        _windowManager.OnOptionsReady += () =>
        {
            if (_optionsService.LastOptionsChain != null)
                _windowManager.SendOptionsUpdate(UpdateMessageType.OPTIONS_CHAIN, _optionsService.LastOptionsChain);

            _ = _optionsService.FetchOptionsChainAsync();
        };

        _windowManager.OnSpreadStaged += command => _optionsService.StageSpread(command);

        _windowManager.OnOptionsWindowClosing += () => _optionsService.ClearSpread();

        _windowManager.OnFuturesWindowClosing += () =>
        {
            _futuresTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _optionsTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _optionsService.ClearSpread();
            WriteSnapshot();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteSnapshot();
    }

    public void Run()
    {
        _windowManager.InitializeFuturesWindow(_market.DisplayName);
        _windowManager.WaitForClose();
    }

    private async Task InitAndSubscribeAsync()
    {
        await _orderManager.ReadCurrentState();
        _optionsService.AttachClient(_marketData.Client);
        await _marketData.StartAsync();

        SendPositionUpdate();
        _futuresTimer = new Timer(FuturesTimerCallback, null, 0, 1000);
        _optionsTimer = new Timer(OptionsTimerCallback, null, 0, 60000);
    }

    private void FuturesTimerCallback(object state)
    {
        var snap = _classifier.GetSnapshot();
        if (snap.Current != null || snap.Completed.Length > 0)
        {
            _windowManager.SendFuturesUpdate(
                UpdateMessageType.FOOTPRINT_SNAPSHOT,
                FootprintRenderBuilder.Build(snap, _market.TickSize, _marketData.CurrentPrice, _market, _analyzer));

            if (_marketData.Bid <= 0 && _marketData.Ask <= 0)
            {
                var lastBar = snap.Current ?? snap.Completed[^1];
                if (lastBar.Close > 0)
                {
                    _marketData.UpdateSyntheticBidAsk(lastBar.Close);
                }
            }
        }

        if (_marketData.Bid > 0 || _marketData.Ask > 0)
        {
            _windowManager.SendFuturesUpdate(UpdateMessageType.MARKET_DATA, new MarketDataUpdate
            {
                Bid = _marketData.Bid,
                Ask = _marketData.Ask
            });
        }

        SendPositionUpdate();
    }

    private void OptionsTimerCallback(object state)
    {
        _ = _optionsService.FetchOptionsChainAsync();
    }

    private void SendPositionUpdate()
    {
        var position = _orderManager.Position;
        if (position == null) return;

        double pnlDollar = 0;
        double points = 0;

        if (position.NetQuantity != 0 && position.AveragePrice > 0)
        {
            var entryPrice = position.AveragePrice;
            var isBuy = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase);
            var evalPrice = _marketData.CurrentPrice > 0 ? _marketData.CurrentPrice : entryPrice;
            var diff = isBuy ? (evalPrice - entryPrice) : (entryPrice - evalPrice);
            points = diff;
            pnlDollar = diff * (double)Math.Abs(position.NetQuantity) * _marketData.TradeMultiplier;
            pnlDollar -= _market.CommissionPerSide * 2 * (double)Math.Abs(position.NetQuantity);
        }

        var update = new PositionUpdate
        {
            Symbol = _market.TradeSymbol,
            Direction = position.Direction,
            NetQuantity = position.NetQuantity,
            Quantity = position.Quantity,
            AveragePrice = position.AveragePrice,
            CurrentPrice = _marketData.CurrentPrice,
            UnrealizedPnL = pnlDollar,
            PointsPnL = points,
            WorkingOrdersCount = _orderManager.ActiveOrders.Count
        };

        _windowManager.SendFuturesUpdate(UpdateMessageType.POSITION_UPDATE, update);
    }

    private void WriteSnapshot()
    {
        var snapshot = _classifier.GetSnapshot();
        _snapshotService.WriteSnapshot(snapshot, _marketData.CurrentPrice);
    }
}
