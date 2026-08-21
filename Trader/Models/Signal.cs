using System;

namespace Trader.Models;

public class Signal
{
    public string Direction { get; set; }
    public double ReturnBps { get; set; }
    public double StdevBps { get; set; }
    public double Ratio { get; set; }
    public double Price { get; set; }

    public override string ToString() =>
        $">>> SIGNAL: {Direction} | return={ReturnBps:F2} bps | stdev={StdevBps:F2} bps | ratio={Ratio:F2}σ | price={Price:F2}";
}
