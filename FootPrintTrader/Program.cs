using System;
using System.Threading.Tasks;
using Common.Helper;
using Common.Model;
using FootPrintTrader.Model;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;
using Photino.NET;

namespace FootPrintTrader
{
    internal class Program
    {
        private static PhotinoWindow _window;
        private static readonly IbClient _client = new();
        private static Logger _logger;
        
        private static ContractDetails _dataContract;
        
        private const int PriceTickRequestId = 1109;
        private const int TradeTickRequestId = 1110;
        
        [STAThread]
        private static void Main()
        {
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

            _window.WaitForClose();
        }
        
        private static void ReceiveMessage(object sender, string message)
        {
            if (message == "applicationReady")
                _ = InitAndSubscribe();
        }

        private static async Task InitAndSubscribe()
        {
            _client.OnNotification += msg => _logger!.WriteLine($"[IB] {msg}");
            _client.OnAccountUpdate += accountUpdate => Console.WriteLine("AccountUpdateReceived");
            _client.Start("127.0.0.1", 4001, 0);
            _client.OnAccountListUpdate += _client.SubscribeToPortfolioUpdates;
            
            _dataContract = await ContractHelper.GetContract(_client, TradingConfig.DataSymbol);
            
            _client.SubscribeToPriceTickByTick(PriceTickRequestId, _dataContract.Contract);
            _client.SubscribeToTradesTickByTick(TradeTickRequestId, _dataContract.Contract);
        }
    }
}