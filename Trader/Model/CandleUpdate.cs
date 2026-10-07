using System.Collections.Generic;
using Indicator;

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

    public SessionLevels Levels { get; set; }
    public double? UpperBand1 { get; set; }
    public double? UpperBand2 { get; set; }
    public double? LowerBand1 { get; set; }
    public double? LowerBand2 { get; set; }
    public List<SwingPoint> ActiveSwings { get; set; } = new();

    public void ApplyOverlay(IndicatorResult result)
    {
        Levels = result.Levels;
        UpperBand1 = result.UpperBand1;
        UpperBand2 = result.UpperBand2;
        LowerBand1 = result.LowerBand1;
        LowerBand2 = result.LowerBand2;
        ActiveSwings = new List<SwingPoint>(result.ActiveSwings);
    }
}
