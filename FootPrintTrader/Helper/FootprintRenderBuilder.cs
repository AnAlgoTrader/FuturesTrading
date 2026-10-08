using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FootPrintTrader.Helper;

public class FootprintRenderRow
{
    public double Price { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public double BidBar { get; set; }
    public double AskBar { get; set; }
}

public class FootprintRenderFooter
{
    public string Text { get; set; } = "";
}

public class FootprintRenderBar
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public List<FootprintRenderRow> Rows { get; set; } = new();
    public List<FootprintRenderFooter> Footer { get; set; } = new();
}

public class FootprintRenderSnapshot
{
    public double RowSize { get; set; }
    public string[] FooterLabels { get; set; } = { "Delta", "Vol" };
    public List<FootprintRenderBar> Bars { get; set; } = new();
}

public static class FootprintRenderBuilder
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static FootprintRenderSnapshot Build(FootprintSnapshot snapshot, double tickSize)
    {
        var result = new FootprintRenderSnapshot { RowSize = tickSize };
        foreach (var bar in snapshot.Completed)
            result.Bars.Add(ConvertBar(bar));
        if (snapshot.Current != null)
            result.Bars.Add(ConvertBar(snapshot.Current));
        return result;
    }

    private static FootprintRenderBar ConvertBar(FootprintBarData bar)
    {
        var max = bar.Levels.Length == 0
            ? 0m
            : bar.Levels.Max(l => Math.Max(l.BidVolume, l.AskVolume));

        var rows = bar.Levels.Select(l => new FootprintRenderRow
        {
            Price = Math.Round(l.Price, 10),
            Bid = l.BidVolume,
            Ask = l.AskVolume,
            BidBar = max > 0 ? (double)(l.BidVolume / max) : 0,
            AskBar = max > 0 ? (double)(l.AskVolume / max) : 0
        }).ToList();

        return new FootprintRenderBar
        {
            Id = bar.StartTime.ToString(CultureInfo.InvariantCulture),
            Label = FormatTime(bar.StartTime),
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Rows = rows,
            Footer = new List<FootprintRenderFooter>
            {
                new() { Text = bar.Delta.ToString(CultureInfo.InvariantCulture) },
                new() { Text = bar.Volume.ToString(CultureInfo.InvariantCulture) }
            }
        };
    }

    private static string FormatTime(long unixSeconds)
    {
        if (unixSeconds <= 0 || unixSeconds > 4102444800) return "";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            .ToLocalTime()
            .ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
