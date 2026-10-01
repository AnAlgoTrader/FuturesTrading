using System;
using Photino.NET;

namespace Trader
{
    internal class Program
    {
        private static PhotinoWindow? _window;

        [STAThread]
        private static void Main()
        {
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
                InitIbClient();
        }

        private static void InitIbClient()
        {
            Console.WriteLine("Start Ib client");
        }

        private static void SendMessage(string message)
        {
            _window?.SendWebMessage(message);
        }
    }
}