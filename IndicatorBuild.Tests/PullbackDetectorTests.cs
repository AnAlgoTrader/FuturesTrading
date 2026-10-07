using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class PullbackDetectorTests
{
    private static Candle CandleAt(int time, double price) =>
        new(time, price - 0.5, price + 0.5, price - 1.0, price, 1);

    private static BarContext Ctx(Candle candle, params SwingPoint[] newSwings) =>
        new(candle, atr: 1, avgVolume: 100, newSwings: newSwings);

    [Fact]
    public void Uptrend_PullbackOnCloseReclaimingDipHigh()
    {
        var engine = new PullbackDetector(legMinAtr: 0, healthyMin: 0.382, healthyMax: 0.618);

        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));
        engine.OnBar(Ctx(CandleAt(1, 108), new SwingPoint(1, 108, true, 1)));   // impulse leg up 100->108
        engine.OnBar(Ctx(CandleAt(2, 104), new SwingPoint(2, 104, false, 2)));  // 50% dip (dip bar high 104.5)
        Assert.DoesNotContain(engine.Events, e => e.Type == Concept.Pullback);

        // close above the dip bar's high 104.5 on the recovery swing
        engine.OnBar(Ctx(CandleAt(3, 110), new SwingPoint(3, 110, true, 3)));

        Assert.Contains(engine.Events, e => e.Type == Concept.Retracement && e.Direction == "Bullish");
        var evt = Assert.Single(engine.Events.Where(e => e.Type == Concept.Pullback));
        Assert.Equal("Bullish", evt.Direction);
        Assert.StartsWith("retrace 50", evt.Detail);
    }

    [Fact]
    public void Uptrend_ShallowRetrace_NoPullback()
    {
        var engine = new PullbackDetector(legMinAtr: 0, healthyMin: 0.382, healthyMax: 0.618);

        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));
        engine.OnBar(Ctx(CandleAt(1, 108), new SwingPoint(1, 108, true, 1)));
        engine.OnBar(Ctx(CandleAt(2, 106), new SwingPoint(2, 106, false, 2)));  // 25% retrace < 38.2%
        engine.OnBar(Ctx(CandleAt(3, 110), new SwingPoint(3, 110, true, 3)));

        Assert.Contains(engine.Events, e => e.Type == Concept.Retracement);
        Assert.DoesNotContain(engine.Events, e => e.Type == Concept.Pullback);
    }

    [Fact]
    public void Downtrend_PullbackOnCloseReclaimingDipLow()
    {
        var engine = new PullbackDetector(legMinAtr: 0, healthyMin: 0.382, healthyMax: 0.618);

        engine.OnBar(Ctx(CandleAt(0, 108), new SwingPoint(0, 108, true, 0)));
        engine.OnBar(Ctx(CandleAt(1, 100), new SwingPoint(1, 100, false, 1)));  // impulse leg down 108->100
        engine.OnBar(Ctx(CandleAt(2, 104), new SwingPoint(2, 104, true, 2)));   // 50% bounce (bounce bar low 103)
        engine.OnBar(Ctx(CandleAt(3, 98), new SwingPoint(3, 98, false, 3)));    // close below bounce bar low 103

        Assert.Contains(engine.Events, e => e.Type == Concept.Retracement && e.Direction == "Bearish");
        var evt = Assert.Single(engine.Events.Where(e => e.Type == Concept.Pullback));
        Assert.Equal("Bearish", evt.Direction);
    }

    [Fact]
    public void Uptrend_DipNotReclaimed_NoPullback()
    {
        var engine = new PullbackDetector(legMinAtr: 0, healthyMin: 0.382, healthyMax: 0.618);

        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));
        engine.OnBar(Ctx(CandleAt(1, 108), new SwingPoint(1, 108, true, 1)));
        engine.OnBar(Ctx(CandleAt(2, 104), new SwingPoint(2, 104, false, 2)));  // dip bar high 104.5
        engine.OnBar(Ctx(CandleAt(3, 104)));
        engine.OnBar(Ctx(CandleAt(4, 101)));   // keeps drifting, never closes above 104.5
        engine.OnBar(Ctx(CandleAt(5, 99), new SwingPoint(5, 99, false, 5)));

        Assert.Contains(engine.Events, e => e.Type == Concept.Retracement);
        Assert.DoesNotContain(engine.Events, e => e.Type == Concept.Pullback);
    }
}