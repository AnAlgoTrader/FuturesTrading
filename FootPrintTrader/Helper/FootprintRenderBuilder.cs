using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FootPrintTrader.Model;

namespace FootPrintTrader.Helper;

public class FootprintRenderRow
{
    public double Price { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public double BidBar { get; set; }
    public double AskBar { get; set; }
    public string BidFill { get; set; }
    public string AskFill { get; set; }
    public string Border { get; set; }
    public string PriceBorder { get; set; }
}

public class FootprintRenderFooter
{
    public string Text { get; set; } = "";
    public string Color { get; set; }
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
    public string Color { get; set; }
    public List<FootprintRenderRow> Rows { get; set; } = new();
    public List<FootprintRenderFooter> Footer { get; set; } = new();
}

public class FootprintRenderSnapshot
{
    public double RowSize { get; set; }
    public int TradesPerBar { get; set; }
    public string[] FooterLabels { get; set; } = { "Delta", "Vol", "Secs" };
    public Dictionary<string, string> Colors { get; set; } = new();
    public List<FootprintRenderBar> Bars { get; set; } = new();
    public OrderFlowContext Context { get; set; }
    public MarketInterpretation Interpretation { get; set; }
}

public static class FootprintRenderBuilder
{
    public const string UpColor = "#2fcf93";
    public const string DownColor = "#f0647b";
    public const string PocBorder = "#f2b705";
    public const string PriceBorder = "#ff8c1a";
    public const string AskFill = "rgba(47,207,147,0.42)";
    public const string BidFill = "rgba(240,100,123,0.42)";

    private const decimal ImbalanceRatio = 0.6m;
    private const decimal MinVolumeRatio = 0.1m;


    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static FootprintRenderSnapshot Build(FootprintSnapshot snapshot, double tickSize, double currentPrice = 0)
    {
        var result = new FootprintRenderSnapshot
        {
            RowSize = tickSize,
            TradesPerBar = TradingConfig.TicksPerBucket,
            Colors = new Dictionary<string, string>
            {
                ["background"] = "#0f131a",
                ["text"] = "#e4e8f0",
                ["muted"] = "#8a93a6",
                ["grid"] = "#262d3b",
                ["up"] = UpColor,
                ["down"] = DownColor,
                ["bar"] = "rgba(130,150,210,0.18)"
            }
        };

        foreach (var bar in snapshot.Completed)
            result.Bars.Add(ConvertBar(bar));
        if (snapshot.Current != null)
            result.Bars.Add(ConvertBar(snapshot.Current));

        if (currentPrice > 0 && result.Bars.Count > 0)
            MarkCurrentPrice(result.Bars[^1], currentPrice, tickSize);

        result.Context = FootprintAnalyzer.AnalyzeContext(snapshot, tickSize, currentPrice);
        result.Interpretation = FootprintInterpreter.Interpret(result.Context, snapshot, tickSize);
        return result;
    }

    private static void MarkCurrentPrice(FootprintRenderBar bar, double price, double tickSize)
    {
        var rowPrice = Math.Round(price / tickSize) * tickSize;
        var tolerance = tickSize * 0.5 + 1e-9;

        foreach (var row in bar.Rows)
        {
            if (Math.Abs(row.Price - rowPrice) > tolerance) continue;
            row.PriceBorder = PriceBorder;
            return;
        }
    }

    private static FootprintRenderBar ConvertBar(FootprintBarData bar)
    {
        var levels = bar.Levels;
        var maxSide = levels.Length == 0
            ? 0m
            : levels.Max(l => Math.Max(l.BidVolume, l.AskVolume));
        var maxTotal = levels.Length == 0
            ? 0m
            : levels.Max(l => l.Total);
        var volumeFloor = maxTotal * MinVolumeRatio;

        var pocIndex = -1;
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i].Total != maxTotal) continue;
            pocIndex = i;
            break;
        }

        var rows = new List<FootprintRenderRow>(levels.Length);
        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            var row = new FootprintRenderRow
            {
                Price = Math.Round(level.Price, 10),
                Bid = level.BidVolume,
                Ask = level.AskVolume,
                BidBar = maxSide > 0 ? (double)(level.BidVolume / maxSide) : 0,
                AskBar = maxSide > 0 ? (double)(level.AskVolume / maxSide) : 0
            };

            if (i == pocIndex)
                row.Border = PocBorder;

            if (level.Total > 0 && level.Total >= volumeFloor &&
                Math.Abs(level.Delta) / level.Total >= ImbalanceRatio)
            {
                if (level.AskVolume > level.BidVolume) row.AskFill = AskFill;
                else if (level.BidVolume > level.AskVolume) row.BidFill = BidFill;
            }

            rows.Add(row);
        }

        var delta = bar.Delta;
        return new FootprintRenderBar
        {
            Id = bar.StartTime.ToString(CultureInfo.InvariantCulture),
            Label = FormatTime(bar.StartTime),
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Color = bar.Close < bar.Open ? DownColor : UpColor,
            Rows = rows,
            Footer = new List<FootprintRenderFooter>
            {
                new()
                {
                    Text = delta.ToString(CultureInfo.InvariantCulture),
                    Color = delta > 0 ? UpColor : delta < 0 ? DownColor : null
                },
                new() { Text = bar.Volume.ToString(CultureInfo.InvariantCulture) },
                new() { Text = Math.Max(0, bar.EndTime - bar.StartTime).ToString(CultureInfo.InvariantCulture) + "s" }
            }
        };
    }

    public static string FormatTime(long unixSeconds)
    {
        if (unixSeconds <= 0 || unixSeconds > 4102444800) return "";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            .ToLocalTime()
            .ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
