using System;
using System.Text.Json;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;
using Photino.NET;
using Trader.Model;

namespace Trader
{
    internal class Program
    {
        private const int HistoricalDataRequestId = 1105;
        private const int RealTimeBarRequestId = 1106;
        private const int MarketDataRequestId = 1108;

        private static PhotinoWindow? _window;
        private static readonly IbClient _client = new();
        private static Logger? _logger;
        private static VwapCalculator? _vwapCalculator;
        private static ClimaxExhaustionDetector? _climaxExhaustionDetector;
        private static CandleAggregator? _candleAggregator;
        private static OrderHelper? _orderHelper;
        private static ContractDetails? _contract;

        [STAThread]
        private static void Main()
        {
            _logger = new Logger();
            _vwapCalculator = new VwapCalculator();
            _climaxExhaustionDetector = new ClimaxExhaustionDetector();
            _candleAggregator = new CandleAggregator(_vwapCalculator, _climaxExhaustionDetector);
            _candleAggregator.OnCandleUpdate += SendCandleUpdate;
            _orderHelper = new OrderHelper(_logger);

            _window = new PhotinoWindow()
                .SetTitle("Trader")
                .SetUseOsDefaultSize(false)
                .SetMaximized(true)
                .RegisterWebMessageReceivedHandler(ReceiveMessage)
                .Load("wwwroot/index.html");

            _window.WaitForClose();
        }

        private static void ReceiveMessage(object? sender, string message)
        {
            if (message == "applicationReady")
                _ = InitAndSubscribe();
        }

        private static async Task InitAndSubscribe()
        {
            await _orderHelper!.ReadCurrentState();

            _client.OnNotification += msg => _logger!.WriteLine($"[IB] {msg}");
            _client.Start("127.0.0.1", 4001, 0);
            _client.OnAccountUpdate += accountUpdate => SendUpdate(UpdateMessageType.ACCOUNT_UPDATE, accountUpdate);
            _client.OnAccountListUpdate += _client.SubscribeToPortfolioUpdates;
            _client.OnRealTimeBarUpdate += _candleAggregator!.AddBar;
            _client.OnMarketDataPriceUpdate += updates =>
            {
                foreach (var (key, marketPrice) in updates)
                {
                    if (key != MarketDataRequestId) continue;
                    SendUpdate(UpdateMessageType.MARKET_DATA, new MarketDataUpdate
                    {
                        Bid = marketPrice.BidPrice,
                        Ask = marketPrice.AskPrice
                    });
                }
            };

            _contract = await ContractHelper.GetContract(_client);
            await DisplayHistoricalData();
            _client.SubscribeToRealTimeBars(RealTimeBarRequestId, _contract.Contract);
            _client.SubscribeToMarketData(MarketDataRequestId, _contract);
        }

        private static void SendCandleUpdate(DateTime candleTime, double open, double high, double low, double close,
            double vwap, string? color)
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

        private static async Task DisplayHistoricalData()
        {
            var historicalData = await _client.GetHistoricalData(HistoricalDataRequestId, _contract!.Contract,
                $"5 {nameof(Duration.D)}", $"1 {nameof(BarSize.min)}", "TRADES", 600000, false);

            foreach (var item in historicalData)
            {
                if (item.Volume == 0 || item is { Open: 0, High: 0, Low: 0, Close: 0 }) continue;
                var time = long.Parse(item.Time);
                var vwap = _vwapCalculator!.AddBar(time, (double)item.WAP, (double)item.Volume);
                var color = _climaxExhaustionDetector!.ProcessCandle(time, item.Open, item.High, item.Low, item.Close, (double)item.Volume);
                SendUpdate(UpdateMessageType.CANDLE_UPDATE, new CandleUpdate
                {
                    Time = time,
                    Open = item.Open,
                    High = item.High,
                    Low = item.Low,
                    Close = item.Close,
                    Vwap = vwap,
                    Color = color
                });
            }
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
    }
}
