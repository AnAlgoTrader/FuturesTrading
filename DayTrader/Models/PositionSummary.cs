namespace DayTrader.Models;

/// <summary>
/// Net position derived from the persisted fill list.
/// Direction follows the sign of NetQuantity: positive = long (BUY),
/// negative = short (SELL), zero = flat.
/// </summary>
public class PositionSummary
{
    public decimal NetQuantity { get; set; }
    public string Direction { get; set; } = "";
    public decimal Quantity { get; set; }
    public double AveragePrice { get; set; }
}
