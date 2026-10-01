using System;
using System.Text.Json;
using Ib.Csharp.Api.Client.Implementation;
using Photino.NET;
using Trader.Model;

namespace Trader
{
    internal class Program
    {
        private static PhotinoWindow _window;
        private static readonly IbClient _client = new();
        private static Logger _logger;

        [STAThread]
        private static void Main()
        {
            _logger = new Logger();
            _window = new PhotinoWindow()
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
                InitIbClient();
        }

        private static void InitIbClient()
        {
            _client.OnNotification += msg => _logger.WriteLine($"[IB] {msg}");
            _client.Start("127.0.0.1", 4001, 0);
            _client.OnAccountUpdate += accountUpdate => { SendUpdate(UpdateMessageType.ACCOUNT_UPDATE, accountUpdate); };
            _client.OnAccountListUpdate += _client.SubscribeToPortfolioUpdates;
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