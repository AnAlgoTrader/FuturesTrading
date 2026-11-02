using System.Collections.Generic;
using FootPrintTrader.Domain.Footprint;

namespace FootPrintTrader.UI.Models;

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

public class FootprintBarTag
{
    public string Text { get; set; } = "";
    public string Color { get; set; }
    public string BgColor { get; set; }
    public string BorderColor { get; set; }

    public FootprintBarTag() { }
    public FootprintBarTag(string text, string color = null, string bgColor = null, string borderColor = null)
    {
        Text = text;
        Color = color;
        BgColor = bgColor;
        BorderColor = borderColor;
    }

    public static implicit operator FootprintBarTag(string text) =>
        string.IsNullOrEmpty(text) ? null : new FootprintBarTag(text);
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
    public FootprintBarTag Header { get; set; }
    public FootprintBarTag CandleFooter { get; set; }
    public List<FootprintRenderRow> Rows { get; set; } = new();
    public List<FootprintRenderFooter> Footer { get; set; } = new();
}

public class FootprintRenderSnapshot
{
    public double RowSize { get; set; }
    public int PriceDecimals { get; set; }
    public int TradesPerBar { get; set; }
    public string[] FooterLabels { get; set; } = { "Delta", "Vol", "Secs" };
    public Dictionary<string, string> Colors { get; set; } = new();
    public List<FootprintRenderBar> Bars { get; set; } = new();
    public OrderFlowContext Context { get; set; }
}
