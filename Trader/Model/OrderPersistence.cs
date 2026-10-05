namespace Trader.Model;

public class OrderPersistence
{
    public int OrderId { get; set; }
    public string Action { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public string OrderType { get; set; }
    public double LmtPrice { get; set; }
    public double AvgFillPrice { get; set; }
    public string Status { get; set; }
}
