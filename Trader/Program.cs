using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Helper;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;
using Photino.NET;
using Trader.Model;

namespace Trader
{
    internal class Program
    {
        private static FootPrintHelper _footPrintHelper = new();
        private const int HistoricalDataRequestId = 1105;
        private const int MarketDataRequestId = 1108;
        private const int FootPrintPriceRequestId = 1109;
        private const int FootPrintTradesRequestId = 1110;
        private const decimal OrderQuantity = 2;
        private static Timer _footPrintTimer;
        private static int _footPrintUpdating;

        private static PhotinoWindow _window;
        private static readonly IbClient _client = new();
        private static Logger _logger;
        private static VwapCalculator _vwapCalculator;
        private static ReversalDetector _reversalDetector;
        private static CandleAggregator _candleAggregator;
        private static OrderHelper _orderHelper;
        private static ContractDetails _dataContract;
        private static ContractDetails _tradeContract;
        private static double _bid;
        private static double _ask;
        private static double _currentPrice;
        private static int _closeTradeInProgress;

        private static double TradeMultiplier =>
            double.TryParse(_tradeContract?.Contract.Multiplier, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 1;

        [STAThread]
        private static void Main()
        {
            _footPrintHelper = new FootPrintHelper();
            _logger = new Logger();
            _vwapCalculator = new VwapCalculator();
            _reversalDetector = new ReversalDetector();
            _candleAggregator = new CandleAggregator(_vwapCalculator, _reversalDetector);
            _candleAggregator.OnCandleUpdate += SendCandleUpdate;
            _orderHelper = new OrderHelper(_logger);
            _orderHelper.OnOrdersChanged += _ => UpdateOrderLine();
            _orderHelper.OnPositionChanged += _ => UpdateOrderLine();

            var window = new PhotinoWindow
            {
                LogVerbosity = 0
            };

            _window = window
                .SetTitle("Trader")
                .SetUseOsDefaultSize(false)
                .SetMaximized(true)
                .RegisterWebMessageReceivedHandler(ReceiveMessage)
                .Load("wwwroot/index.html");

            _window.WaitForClose();
        }

        private static void ReceiveMessage(object sender, string message)
        {
            if (message == "applicationReady")
                _ = InitAndSubscribe();
            else if (message == "buy")
                _ = SubmitOrder("BUY", OrderQuantity, _ask > 0 ? _ask : _currentPrice);
            else if (message == "sell")
                _ = SubmitOrder("SELL", OrderQuantity, _bid > 0 ? _bid : _currentPrice);
            else if (message == "closeTrade")
                _ = CloseActiveTrade();
        }

        private static async Task InitAndSubscribe()
        {
            await _orderHelper!.ReadCurrentState();

            _client.OnNotification += msg => _logger!.WriteLine($"[IB] {msg}");
            _client.Start("127.0.0.1", 4001, 0);
            _client.OnAccountUpdate += accountUpdate => SendUpdate(UpdateMessageType.ACCOUNT_UPDATE, accountUpdate);
            _client.OnAccountListUpdate += _client.SubscribeToPortfolioUpdates;
            _client.OnOrderStatusUpdate += status => _ = _orderHelper.UpdateOrderStatus(status);
            _client.OnMarketDataPriceTickByTickUpdate += price =>
            {
                _footPrintHelper.UpdatePrice(price);
                _candleAggregator.UpdateQuote(price.Time, price.Bid, price.Ask);
            };
            _client.OnMarketDataTradeTickByTickUpdate += trade =>
            {
                _footPrintHelper.UpdateTrade(trade);
                _vwapCalculator.AddTrade(trade.Time, trade.Price, (double)trade.Size);
                _candleAggregator.AddTrade(trade.Time, trade.Price, (double)trade.Size);
            };
            _client.OnMarketDataPriceUpdate += updates =>
            {
                foreach (var (key, marketPrice) in updates)
                {
                    if (key != MarketDataRequestId) continue;
                    _bid = marketPrice.BidPrice;
                    _ask = marketPrice.AskPrice;
                    _currentPrice = _bid > 0 && _ask > 0
                        ? (_bid + _ask) / 2.0
                        : (_bid > 0 ? _bid : _ask);
                    _orderHelper.CurrentChartPrice = _currentPrice;
                    SendUpdate(UpdateMessageType.MARKET_DATA, new MarketDataUpdate
                    {
                        Bid = _bid,
                        Ask = _ask
                    });

                    if (_bid > 0)
                    {
                        _candleAggregator.AddPriceTick(_bid);
                    }
                }

                UpdateOrderLine();
            };

            _dataContract = await ContractHelper.GetContract(_client, TradingConfig.DataSymbol);
            _tradeContract = await ContractHelper.GetContract(_client, TradingConfig.TradeSymbol);
            SendContractUpdate();
            await DisplayHistoricalData();
            _client.SubscribeToMarketData(MarketDataRequestId, _dataContract);
            _client.SubscribeToPriceTickByTick(FootPrintPriceRequestId, _dataContract.Contract);
            _client.SubscribeToTradesTickByTick(FootPrintTradesRequestId, _dataContract.Contract);
            _footPrintTimer = new Timer(_ => SendFootPrintUpdate(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private static void SendCandleUpdate(DateTime candleTime, double open, double high, double low, double close, double vwap, string color)
        {
            SendUpdate(UpdateMessageType.CANDLE_UPDATE, new CandleUpdate
            {
                Time = new DateTimeOffset(candleTime).ToUnixTimeSeconds(),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Vwap = vwap,
                Color = color
            });
        }

        private static void SendFootPrintUpdate()
        {
            if (Interlocked.Exchange(ref _footPrintUpdating, 1) == 1) return;
            try
            {
                var columns = _footPrintHelper.GetSnapshot();
                SendUpdate(UpdateMessageType.FOOTPRINT_UPDATE, columns);
            }
            finally
            {
                Interlocked.Exchange(ref _footPrintUpdating, 0);
            }
        }

        private static void SendContractUpdate()
        {
            var contract = _dataContract!.Contract;
            var expiration = DateTime.ParseExact(contract.LastTradeDateOrContractMonth, "yyyyMMdd",
                CultureInfo.InvariantCulture);
            SendUpdate(UpdateMessageType.CONTRACT_UPDATE, new ContractUpdate
            {
                Symbol = contract.Symbol,
                Expiration = expiration.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
        }

        private static async Task DisplayHistoricalData()
        {
            var historicalData = await _client.GetHistoricalData(HistoricalDataRequestId, _dataContract!.Contract,
                $"5 {nameof(Duration.D)}", $"1 {nameof(BarSize.min)}", "TRADES", 600000, false);

            var candles = new SortedDictionary<long, CandleUpdate>();
            foreach (var item in historicalData)
            {
                if (item.Volume == 0 || item is { Open: 0, High: 0, Low: 0, Close: 0 }) continue;
                var time = long.Parse(item.Time);
                var vwap = _vwapCalculator!.AddBar(time, (double)item.WAP, (double)item.Volume);
                var color = _reversalDetector!.ProcessHistoricalCandle(time, item.Open, item.High, item.Low, item.Close, (double)item.Volume);
                candles[time] = new CandleUpdate
                {
                    Time = time,
                    Open = item.Open,
                    High = item.High,
                    Low = item.Low,
                    Close = item.Close,
                    Vwap = vwap,
                    Color = color
                };
            }

            _logger!.WriteLine($"[CHART] Setting {candles.Count} historical candles.");
            SendUpdate(UpdateMessageType.CANDLE_SET, new CandleSet { Candles = new List<CandleUpdate>(candles.Values) });
        }

        private static void SendUpdate(string type, object data)
        {
            var message = new UpdateMessage
            {
                Type = type,
                Data = JsonSerializer.SerializeToElement(data)
            };
            SendMessage(JsonSerializer.Serialize(message));
        }

        private static void SendMessage(string message)
        {
            _window?.SendWebMessage(message);
        }

        private static async Task SubmitOrder(string action, decimal quantity, double price)
        {
            if (_tradeContract == null)
            {
                _logger!.WriteLine($"[TRADE] SubmitOrder {action} REJECTED: Trade contract is not initialized.");
                return;
            }

            if (price <= 0)
            {
                _logger!.WriteLine($"[TRADE] SubmitOrder {action} REJECTED: Invalid price {price:F2}.");
                return;
            }

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
            await _orderHelper!.PersistOrder(order);
            _client.PlaceOrder(_tradeContract.Contract, order);
            _logger!.WriteLine($"[TRADE] OrderId:{order.OrderId} - Action:{order.Action} - Quantity:{order.TotalQuantity} - EntryPrice:{order.LmtPrice}");
        }

        private static async Task CloseActiveTrade()
        {
            if (Interlocked.Exchange(ref _closeTradeInProgress, 1) == 1)
            {
                _logger!.WriteLine("[TRADE] CloseActiveTrade SKIPPED - another close is already in progress.");
                return;
            }

            try
            {
                var position = _orderHelper!.Position;
                if (position.NetQuantity == 0)
                {
                    _logger!.WriteLine("[TRADE] CloseActiveTrade requested but position is flat.");
                    return;
                }

                var closeQty = Math.Abs(position.NetQuantity);
                var closeAction = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
                var closePrice = string.Equals(closeAction, "BUY", StringComparison.OrdinalIgnoreCase) ? _ask : _bid;

                _logger!.WriteLine($"[TRADE] CloseActiveTrade: closing {position.Direction} {closeQty} @ {position.AveragePrice:F2} with {closeAction} {closeQty}");
                await SubmitOrder(closeAction, closeQty, closePrice);
            }
            finally
            {
                Interlocked.Exchange(ref _closeTradeInProgress, 0);
            }
        }

        private static void UpdateOrderLine()
        {
            var position = _orderHelper!.Position;
            if (position.NetQuantity != 0 && position.AveragePrice > 0)
            {
                var entryPrice = position.AveragePrice;
                var isBuy = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase);

                var evalPrice = _currentPrice > 0 ? _currentPrice : entryPrice;
                double pnlDollar = isBuy
                    ? (evalPrice - entryPrice) * (double)position.NetQuantity * TradeMultiplier
                    : (entryPrice - evalPrice) * (double)Math.Abs(position.NetQuantity) * TradeMultiplier;
                var color = pnlDollar >= 0 ? "#26a69a" : "#ef5350";

                string sign = pnlDollar >= 0 ? "+" : "";
                string pnlTitle = $"{sign}${pnlDollar:F2} @ {position.AveragePrice:F2}";

                SendUpdate(UpdateMessageType.ORDER_LINE, new OrderLineUpdate
                {
                    Price = entryPrice,
                    Color = color,
                    Title = pnlTitle,
                    ChartPrice = position.ChartEntryPrice
                });
            }
            else
            {
                SendUpdate(UpdateMessageType.ORDER_LINE, new OrderLineUpdate
                {
                    Price = 0,
                    Color = "",
                    Title = "",
                    ChartPrice = 0
                });
            }
        }
    }
}