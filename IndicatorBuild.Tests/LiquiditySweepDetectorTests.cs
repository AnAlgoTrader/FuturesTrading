using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class LiquiditySweepDetectorTests
{
    private static Candle CandleAt(int time, double price) =>
        new(time, price - 0.5, price + 0.5, price - 1.0, price, 1);

    private static Candle BarOf(int time, double open, double high, double low, double close) =>
        new(time, open, high, low, close, 1);

    private static BarContext Ctx(Candle candle, params SwingPoint[] newSwings) =>
        new(candle, atr: 1, avgVolume: 100, newSwings: newSwings);

    [Fact]
    public void BullishSweep_DetectedThenConfirmed()
    {
        var engine = new LiquiditySweepDetector(0.35);
        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));

        // low 99.5 < swing low 100, close 100.5 back inside; lower wick 0.7 / range 1.5 = 0.47
        engine.OnBar(Ctx(BarOf(1, 100.2, 101, 99.5, 100.5)));

        var evt = Assert.Single(engine.Events);
        Assert.Equal(Concept.LiquiditySweep, evt.Type);
        Assert.Equal("Bullish", evt.Direction);
        Assert.Equal("Detected", evt.Detail);

        // close above the sweep candle's high -> confirmed
        engine.OnBar(Ctx(BarOf(2, 100.8, 102, 100.4, 101.6)));
        Assert.Equal(2, engine.Events.Count);
        Assert.Equal("Confirmed", engine.Events[1].Detail);
    }

    [Fact]
    public void BullishSweep_FadedWhenCloseBreaksBelow()
    {
        var engine = new LiquiditySweepDetector(0.35);
        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));
        engine.OnBar(Ctx(BarOf(1, 100.2, 101, 99.5, 100.5)));

        // close 99.2 below sweep low 99.5 -> faded
        engine.OnBar(Ctx(BarOf(2, 100.0, 100.4, 99.0, 99.2)));

        Assert.Equal(2, engine.Events.Count);
        Assert.Equal("Faded", engine.Events[1].Detail);
    }

    [Fact]
    public void BearishSweep_DetectedOnUpperWick()
    {
        var engine = new LiquiditySweepDetector(0.35);
        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, true, 0)));

        // high 100.2 > swing high 100, close 99.9 back inside; upper wick 0.1 / range 0.9 = 0.11 < 0.35 -> no sweep
        engine.OnBar(Ctx(BarOf(1, 100.1, 100.2, 99.3, 99.9)));
        Assert.Empty(engine.Events);

        // wider wick: high 101, upper wick 1.0 / range 1.7 = 0.59 -> sweep
        engine.OnBar(Ctx(BarOf(2, 100.0, 101, 99.3, 99.8)));
        var evt = Assert.Single(engine.Events);
        Assert.Equal("Bearish", evt.Direction);
        Assert.Equal("Detected", evt.Detail);
    }

    [Fact]
    public void WeakWickDoesNotTriggerSweep()
    {
        var engine = new LiquiditySweepDetector(0.35);
        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));

        // pierces swing low but lower wick is tiny
        engine.OnBar(Ctx(BarOf(1, 100.2, 101, 99.9, 100.5)));
        Assert.Empty(engine.Events);
    }
}