namespace FootPrintTrader.UI.Models;

public class PositionUpdate
{
    public string Symbol { get; set; } = "";
    public string Direction { get; set; } = ""; // "BUY", "SELL", or ""
    public decimal NetQuantity { get; set; }
    public decimal Quantity { get; set; }
    public double AveragePrice { get; set; }
    public double CurrentPrice { get; set; }
    public double UnrealizedPnL { get; set; }
    public double PointsPnL { get; set; }
    public int WorkingOrdersCount { get; set; }
}

public class MarketDataUpdate
{
    public double Bid { get; set; }
    public double Ask { get; set; }
}

public class MarketInfo
{
    public string DisplayName { get; set; } = "";
    public string DataSymbol { get; set; } = "";
    public string TradeSymbol { get; set; } = "";
    public string Exchange { get; set; } = "";
    public int PriceDecimals { get; set; }
    public int TradesPerBar { get; set; }
    public string CurrencySign { get; set; } = "$";
}
