using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Helper;
using Common.Model;
using FootPrintTrader.Helper;
using FootPrintTrader.Model;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;
using Photino.NET;

namespace FootPrintTrader
{
    internal class Program
    {
        private static FootprintClassifier _footprintClassifier;
        private static PhotinoWindow _window;
        private static readonly IbClient _client = new();
        private static Logger _logger;
        private static Timer _threadedTimer;
        private static OrderHelper _orderHelper;

        private static ContractDetails _dataContract;
        private static ContractDetails _tradeContract;

        private const int MarketDataRequestId = 1108;
        private const int PriceTickRequestId = 1109;
        private const int TradeTickRequestId = 1110;
        private const double TickSize = 0.25;
        private const decimal OrderQuantity = 2m;
        private const double RoundTripCommission = 0.7 * 2;

        private static double _bid;
        private static double _ask;
        private static double _currentPrice;
        private static int _closeTradeInProgress;
        private static int _snapshotWritten;

        private static double TradeMultiplier =>
            double.TryParse(_tradeContract?.Contract.Multiplier, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 5;

        [STAThread]
        private static void Main()
        {
            _footprintClassifier = new FootprintClassifier(tickSize: TickSize, tradesPerBar: 1000);
            _logger = new Logger();
            _orderHelper = new OrderHelper(_logger);
            _orderHelper.OnOrdersChanged += _ => SendPositionUpdate();
            _orderHelper.OnPositionChanged += _ => SendPositionUpdate();

            var window = new PhotinoWindow
            {
                LogVerbosity = 0
            };

            _window = window
                .SetTitle("FootPrintTrader")
                .SetUseOsDefaultSize(false)
                .SetMaximized(true)
                .RegisterWebMessageReceivedHandler(ReceiveMessage)
                .Load("wwwroot/index.html");

            _window.WindowClosing += OnWindowClosing;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteSnapshot();

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

        private static bool OnWindowClosing(object sender, EventArgs e)
        {
            _threadedTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            WriteSnapshot();
            return false;
        }

        private static void WriteSnapshot()
        {
            if (Interlocked.Exchange(ref _snapshotWritten, 1) == 1) return;

            try
            {
                var snapshot = _footprintClassifier?.GetSnapshot();
                if (snapshot == null) return;

                Directory.CreateDirectory(Logger.DATA_DIR);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var payload = FootprintRenderBuilder.Build(snapshot, TickSize);

                var jsonPath = Path.Combine(Logger.DATA_DIR, $"footprint-{stamp}.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, FootprintRenderBuilder.JsonOptions));

                var htmlPath = Path.Combine(Logger.DATA_DIR, $"footprint-{stamp}.html");
                File.WriteAllText(htmlPath, FootprintHtmlExporter.Build(payload, DateTime.Now));

                _logger?.WriteLine($"[SNAPSHOT] {Path.GetFileName(jsonPath)} + {Path.GetFileName(htmlPath)}");
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _snapshotWritten, 0);
                _logger?.WriteLine($"[SNAPSHOT] Write failed: {ex}");
            }
        }

        private static async Task InitAndSubscribe()
        {
            await _orderHelper!.ReadCurrentState();

            _client.OnNotification += msg => _logger!.WriteLine($"[IB] {msg}");
            _client.OnOrderStatusUpdate += status => _ = _orderHelper.UpdateOrderStatus(status);

            _client.OnMarketDataPriceTickByTickUpdate += price =>
            {
                _footprintClassifier.OnQuote(price.Time, price.Bid, price.Ask);
                if (price.Bid > 0 || price.Ask > 0)
                {
                    if (price.Bid > 0) _bid = price.Bid;
                    if (price.Ask > 0) _ask = price.Ask;
                    _currentPrice = _bid > 0 && _ask > 0 ? (_bid + _ask) / 2.0 : (_bid > 0 ? _bid : _ask);
                    _orderHelper.CurrentChartPrice = _currentPrice;
                    SendUpdate(UpdateMessageType.MARKET_DATA, new MarketDataUpdate { Bid = _bid, Ask = _ask });
                    SendPositionUpdate();
                }
            };

            _client.OnMarketDataPriceUpdate += updates =>
            {
                foreach (var (key, marketPrice) in updates)
                {
                    if (key != MarketDataRequestId) continue;
                    if (marketPrice.BidPrice > 0) _bid = marketPrice.BidPrice;
                    if (marketPrice.AskPrice > 0) _ask = marketPrice.AskPrice;
                    _currentPrice = _bid > 0 && _ask > 0 ? (_bid + _ask) / 2.0 : (_bid > 0 ? _bid : _ask);
                    _orderHelper.CurrentChartPrice = _currentPrice;
                    SendUpdate(UpdateMessageType.MARKET_DATA, new MarketDataUpdate { Bid = _bid, Ask = _ask });
                    SendPositionUpdate();
                }
            };

            _client.OnMarketDataTradeTickByTickUpdate += trade =>
            {
                _footprintClassifier.OnTrade(trade.Time, trade.Price, trade.Size);
                if (trade.Price > 0)
                {
                    _currentPrice = trade.Price;
                    _orderHelper.CurrentChartPrice = _currentPrice;
                    if (_bid <= 0 && _ask <= 0)
                    {
                        _bid = _currentPrice - TickSize * 0.5;
                        _ask = _currentPrice + TickSize * 0.5;
                        SendUpdate(UpdateMessageType.MARKET_DATA, new MarketDataUpdate { Bid = _bid, Ask = _ask });
                    }
                }
            };

            _client.Start("127.0.0.1", 4001, 0);

            _dataContract = await ContractHelper.GetContract(_client, TradingConfig.DataSymbol);
            _tradeContract = await ContractHelper.GetContract(_client, TradingConfig.TradeSymbol);

            _client.SubscribeToMarketData(MarketDataRequestId, _dataContract);
            _client.SubscribeToPriceTickByTick(PriceTickRequestId, _dataContract.Contract);
            _client.SubscribeToTradesTickByTick(TradeTickRequestId, _dataContract.Contract);

            SendPositionUpdate();
            _threadedTimer = new Timer(TimerCallback, null, 0, 1000);
        }

        private static void TimerCallback(object state)
        {
            var snap = _footprintClassifier.GetSnapshot();
            if (snap.Current != null || snap.Completed.Length > 0)
            {
                SendUpdate(UpdateMessageType.FOOTPRINT_SNAPSHOT, FootprintRenderBuilder.Build(snap, TickSize));

                if (_bid <= 0 && _ask <= 0)
                {
                    var lastBar = snap.Current ?? snap.Completed[^1];
                    if (lastBar.Close > 0)
                    {
                        _currentPrice = lastBar.Close;
                        _orderHelper.CurrentChartPrice = _currentPrice;
                        _bid = _currentPrice - TickSize * 0.5;
                        _ask = _currentPrice + TickSize * 0.5;
                    }
                }
            }

            if (_bid > 0 || _ask > 0)
            {
                SendUpdate(UpdateMessageType.MARKET_DATA, new MarketDataUpdate { Bid = _bid, Ask = _ask });
            }

            SendPositionUpdate();
        }

        private static void SendPositionUpdate()
        {
            var position = _orderHelper?.Position;
            if (position == null) return;

            double pnlDollar = 0;
            double points = 0;

            if (position.NetQuantity != 0 && position.AveragePrice > 0)
            {
                var entryPrice = position.AveragePrice;
                var isBuy = string.Equals(position.Direction, "BUY", StringComparison.OrdinalIgnoreCase);
                var evalPrice = _currentPrice > 0 ? _currentPrice : entryPrice;
                var diff = isBuy ? (evalPrice - entryPrice) : (entryPrice - evalPrice);
                points = diff;
                pnlDollar = diff * (double)Math.Abs(position.NetQuantity) * TradeMultiplier;
                pnlDollar -= RoundTripCommission * (double)Math.Abs(position.NetQuantity);
            }

            var update = new PositionUpdate
            {
                Symbol = TradingConfig.TradeSymbol,
                Direction = position.Direction,
                NetQuantity = position.NetQuantity,
                Quantity = position.Quantity,
                AveragePrice = position.AveragePrice,
                CurrentPrice = _currentPrice,
                UnrealizedPnL = pnlDollar,
                PointsPnL = points,
                WorkingOrdersCount = _orderHelper.ActiveOrders.Count
            };

            SendUpdate(UpdateMessageType.POSITION_UPDATE, update);
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

        private static void SendUpdate(string type, object data)
        {
            var message = new UpdateMessage
            {
                Type = type,
                Data = JsonSerializer.SerializeToElement(data, FootprintRenderBuilder.JsonOptions)
            };
            _window?.SendWebMessage(JsonSerializer.Serialize(message));
        }
    }
}