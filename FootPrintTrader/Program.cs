using System;
using Photino.NET;

namespace FootPrintTrader
{
    internal class Program
    {
        private static PhotinoWindow _window;
        
        [STAThread]
        private static void Main()
        {
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
                Console.WriteLine("The application is ready");
        }
    }
}