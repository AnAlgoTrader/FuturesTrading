namespace Trader.Models;

public class Signal
{
    public string Direction { get; init; }
    public double ReturnBps { get; init; }
    public double StdevBps { get; init; }
    public double Ratio { get; init; }
    public double Price { get; init; }

    public override string ToString() => $">>> SIGNAL: {Direction} | return={ReturnBps:F2} bps | stdev={StdevBps:F2} bps | ratio={Ratio:F2}σ | price={Price:F2}";
}
