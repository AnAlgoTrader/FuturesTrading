using System;
using System.Drawing;
using Photino.NET;

namespace Trader
{
    internal class Program
    {
        [STAThread]
        private static void Main()
        {
            var window = new PhotinoWindow()
                .SetTitle("Trader")
                .SetUseOsDefaultSize(false)
                .SetMaximized(true)
                // Most event handlers can be registered after the
                // PhotinoWindow was instantiated by calling a registration 
                // method like the following RegisterWebMessageReceivedHandler.
                // This could be added in the PhotinoWindowOptions if preferred.
                .RegisterWebMessageReceivedHandler((sender, message) =>
                {
                    var window = (PhotinoWindow)sender!;

                    // The message argument is coming in from sendMessage.
                    // "window.external.sendMessage(message: string)"
                    string response = $"Received message: \"{message}\"";

                    // Send a message back the to JavaScript event handler.
                    // "window.external.receiveMessage(callback: Function)"
                    window.SendWebMessage(response);
                })
                .Load("wwwroot/index.html");

            window.WaitForClose();
        }
    }
}