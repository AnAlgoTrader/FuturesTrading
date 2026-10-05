namespace Trader.Model;

public class CandleUpdate
{
    public long Time { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Vwap { get; set; }
    public string Color { get; set; } = "";
}
