using System;

namespace Trader.Models;

public class Candle
{
    public DateTime Timestamp { get; }
    public double Open { get; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public int TickCount { get; set; }
    public bool IsComplete { get; set; }

    public Candle(DateTime timestamp, double price)
    {
        Timestamp = timestamp;
        Open = price;
        High = price;
        Low = price;
        Close = price;
        TickCount = 1;
    }

    public void Update(double price)
    {
        if (price > High) High = price;
        if (price < Low) Low = price;
        Close = price;
        TickCount++;
    }

    public override string ToString() => $"{Timestamp:HH:mm:ss} O:{Open:F2} H:{High:F2} L:{Low:F2} C:{Close:F2} Ticks:{TickCount}";
}
