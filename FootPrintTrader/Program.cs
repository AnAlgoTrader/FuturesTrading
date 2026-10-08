using System;
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
        
        private static ContractDetails _dataContract;
        
        private const int PriceTickRequestId = 1109;
        private const int TradeTickRequestId = 1110;
        private const double TickSize = 0.25;

        private static int _snapshotWritten;
        
        [STAThread]
        private static void Main()
        {
            _footprintClassifier = new FootprintClassifier(tickSize: TickSize, tradesPerBar: 1000);
            _logger = new Logger();
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
            _client.OnNotification += msg => _logger!.WriteLine($"[IB] {msg}");
            _client.OnMarketDataPriceTickByTickUpdate += price =>
            {
                _footprintClassifier.OnQuote(price.Time, price.Bid, price.Ask);
            };
            _client.OnMarketDataTradeTickByTickUpdate += trade =>
            {
                _footprintClassifier.OnTrade(trade.Time, trade.Price, trade.Size);
            };
            _client.Start("127.0.0.1", 4001, 0);
            
            _dataContract = await ContractHelper.GetContract(_client, TradingConfig.DataSymbol);
            
            _client.SubscribeToPriceTickByTick(PriceTickRequestId, _dataContract.Contract);
            _client.SubscribeToTradesTickByTick(TradeTickRequestId, _dataContract.Contract);
            
            _threadedTimer = new Timer(TimerCallback, null, 0, 1000);
        }
        
        private static void TimerCallback(object state)
        {
            var snap = _footprintClassifier.GetSnapshot();
            if (snap.Current == null && snap.Completed.Length == 0) return;

            SendUpdate(UpdateMessageType.FOOTPRINT_SNAPSHOT, FootprintRenderBuilder.Build(snap, TickSize));
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