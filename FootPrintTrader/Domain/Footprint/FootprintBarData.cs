using System.Linq;

namespace FootPrintTrader.Domain.Footprint;

public readonly record struct FootprintLevel(double Price, decimal BidVolume, decimal AskVolume)
{
    public decimal Total => BidVolume + AskVolume;
    public decimal Delta => AskVolume - BidVolume;
}

public sealed record FootprintBarData(
    int TradeCount, long StartTime, long EndTime,
    double Open, double High, double Low, double Close,
    FootprintLevel[] Levels,          // ascending price
    long Id = 0,                      // unique, monotonic per classifier. Use this as the bar key, NOT StartTime
                                      // (two bars can start in the same IBKR second).
    double DurationSeconds = 0)       // first-to-last trade, from local arrival clock (sub-ms resolution)
{
    public decimal Delta => Levels.Sum(l => l.Delta);
    public decimal Volume => Levels.Sum(l => l.Total);
}

public sealed record FootprintSnapshot(
    FootprintBarData[] Completed,     // capped history, oldest first
    FootprintBarData Current);        // in-progress bar (maybe null)
