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
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

        private static bool _allowClose;
        private static int _captureStarted;
        private static int _captureCompleted;
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
            AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteSnapshot(null);

            _window.WaitForClose();
        }
        
        private static void ReceiveMessage(object sender, string message)
        {
            if (message == "applicationReady")
            {
                _ = InitAndSubscribe();
                return;
            }

            HandleUiMessage(message);
        }

        private static void HandleUiMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || message[0] != '{') return;

            try
            {
                var update = JsonSerializer.Deserialize<UpdateMessage>(message);
                if (update?.Type == UpdateMessageType.CHART_CAPTURE)
                {
                    var data = update.Data.ValueKind == JsonValueKind.String ? update.Data.GetString() : null;
                    CompleteCapture(DecodePng(data));
                }
            }
            catch (Exception ex)
            {
                _logger?.WriteLine($"[SNAPSHOT] Failed to read UI message: {ex.Message}");
            }
        }

        private static bool OnWindowClosing(object sender, EventArgs e)
        {
            if (_allowClose) return false;

            StartCapture();
            return true;
        }

        private static void StartCapture()
        {
            if (Interlocked.Exchange(ref _captureStarted, 1) == 1) return;

            SendUpdate(UpdateMessageType.CAPTURE_CHART, null);
            _ = Task.Delay(CaptureTimeout).ContinueWith(_ => CompleteCapture(null), TaskScheduler.Default);
        }

        private static void CompleteCapture(byte[] png)
        {
            if (Interlocked.Exchange(ref _captureCompleted, 1) == 1) return;

            _threadedTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            WriteSnapshot(png);

            _allowClose = true;
            try
            {
                _window?.Close();
            }
            catch (Exception ex)
            {
                _logger?.WriteLine($"[SNAPSHOT] Close failed: {ex.Message}");
            }
        }

        private static void WriteSnapshot(byte[] png)
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
                var written = $"[SNAPSHOT] {Path.GetFileName(jsonPath)}";

                if (png is { Length: > 0 })
                {
                    var pngPath = Path.Combine(Logger.DATA_DIR, $"footprint-{stamp}.png");
                    File.WriteAllBytes(pngPath, png);
                    written += $" + {Path.GetFileName(pngPath)} ({png.Length:N0} bytes)";
                }

                _logger?.WriteLine(written);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _snapshotWritten, 0);
                _logger?.WriteLine($"[SNAPSHOT] Write failed: {ex}");
            }
        }

        private static byte[] DecodePng(string dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return null;

            var marker = dataUrl.IndexOf("base64,", StringComparison.Ordinal);
            var base64 = marker >= 0 ? dataUrl[(marker + 7)..] : dataUrl;
            return Convert.FromBase64String(base64);
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