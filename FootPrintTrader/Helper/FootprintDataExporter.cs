using System;
using System.Collections.Generic;

namespace FootPrintTrader.Helper;

public class FootprintDataExport
{
    public string Symbol { get; set; } = "";
    public string TradeSymbol { get; set; } = "";
    public double TickSize { get; set; }
    public int TradesPerBar { get; set; }
    public DateTime ExportedAt { get; set; }
    public List<FootprintDataBar> Completed { get; set; } = new();
    public FootprintDataBar Current { get; set; }
}

public class FootprintDataBar
{
    public long StartTime { get; set; }
    public long EndTime { get; set; }
    public int TradeCount { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public decimal Volume { get; set; }
    public decimal Delta { get; set; }
    public List<FootprintDataLevel> Levels { get; set; } = new();
}

public class FootprintDataLevel
{
    public double Price { get; set; }
    public decimal BidVolume { get; set; }
    public decimal AskVolume { get; set; }
    public decimal Total { get; set; }
    public decimal Delta { get; set; }
}

/// <summary>
/// Builds a plain-data snapshot of the footprint bars. Contains no render data
/// (colors, bar widths, borders) and no analysis (signals, bias, interpretation).
/// </summary>
public static class FootprintDataExporter
{
    public static FootprintDataExport Build(
        FootprintSnapshot snapshot,
        double tickSize,
        string symbol,
        string tradeSymbol,
        int tradesPerBar,
        DateTime exportedAt)
    {
        var result = new FootprintDataExport
        {
            Symbol = symbol,
            TradeSymbol = tradeSymbol,
            TickSize = tickSize,
            TradesPerBar = tradesPerBar,
            ExportedAt = exportedAt
        };

        foreach (var bar in snapshot.Completed)
            result.Completed.Add(ConvertBar(bar));

        if (snapshot.Current != null)
            result.Current = ConvertBar(snapshot.Current);

        return result;
    }

    private static FootprintDataBar ConvertBar(FootprintBarData bar)
    {
        var levels = new List<FootprintDataLevel>(bar.Levels.Length);
        foreach (var level in bar.Levels)
        {
            levels.Add(new FootprintDataLevel
            {
                Price = Math.Round(level.Price, 10),
                BidVolume = level.BidVolume,
                AskVolume = level.AskVolume,
                Total = level.Total,
                Delta = level.Delta
            });
        }

        return new FootprintDataBar
        {
            StartTime = bar.StartTime,
            EndTime = bar.EndTime,
            TradeCount = bar.TradeCount,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Volume = bar.Volume,
            Delta = bar.Delta,
            Levels = levels
        };
    }
}
