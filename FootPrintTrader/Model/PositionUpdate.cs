namespace FootPrintTrader.Model;

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
