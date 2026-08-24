using System;
using System.Globalization;
using System.IO;

namespace Common;

public class TradeJournal
{
    private const double MesPointValue = 5.0;
    private const double EsPointValue = 50.0;

    private readonly string _filePath;
    private readonly object _lock = new();

    public TradeJournal(string filePath)
    {
        _filePath = filePath;
        if (!File.Exists(_filePath))
        {
            File.WriteAllText(_filePath,
                "Symbol,Direction,Quantity,EntryTime,EntryPrice,ExitTime,ExitPrice,BarsHeld,PnL" + Environment.NewLine);
        }
    }

    public void Append(TrackedOrder order)
    {
        var points = order.Direction == TradeDirection.Long
            ? order.ExitPrice - order.AverageFillPrice
            : order.AverageFillPrice - order.ExitPrice;
        var pnl = points * (double)order.Quantity * PointValue(order.Symbol);

        var line = string.Join(",",
            order.Symbol,
            order.Direction,
            order.Quantity.ToString(CultureInfo.InvariantCulture),
            order.FilledAt.ToString("yyyy-MM-dd HH:mm:ss"),
            order.AverageFillPrice.ToString("F2", CultureInfo.InvariantCulture),
            order.ClosedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            order.ExitPrice.ToString("F2", CultureInfo.InvariantCulture),
            order.BarsHeld.ToString(CultureInfo.InvariantCulture),
            pnl.ToString("F2", CultureInfo.InvariantCulture));

        lock (_lock)
        {
            File.AppendAllText(_filePath, line + Environment.NewLine);
        }
    }

    private static double PointValue(string symbol)
    {
        if (symbol == "MES") return MesPointValue;
        if (symbol == "ES") return EsPointValue;
        return 1.0;
    }
}
