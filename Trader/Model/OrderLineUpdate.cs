namespace Trader.Model;

public class OrderLineUpdate
{
    public double Price { get; set; }
    public string Color { get; set; } = "";
    public string Title { get; set; } = "";
    public double ChartPrice { get; set; }
}
