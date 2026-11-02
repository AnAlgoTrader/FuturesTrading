using System;

namespace FootPrintTrader;

internal class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new TradingApp();
        app.Run();
    }
}
