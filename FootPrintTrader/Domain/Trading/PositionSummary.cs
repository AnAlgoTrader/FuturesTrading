namespace FootPrintTrader.Domain.Trading;

public class PositionSummary
{
    public decimal NetQuantity { get; set; }
    public string Direction { get; set; } = "";
    public decimal Quantity { get; set; }
    public double AveragePrice { get; set; }
    public double ChartEntryPrice { get; set; }
}
