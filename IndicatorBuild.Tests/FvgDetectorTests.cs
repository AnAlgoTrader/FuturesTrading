using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class FvgDetectorTests
{
    private static Candle BarOf(int time, double high, double low) =>
        new(time, (high + low) / 2, high, low, (high + low) / 2, 1);

    private static BarContext Ctx(Candle candle) => new(candle, atr: 1, avgVolume: 100);

    [Fact]
    public void BullishFvg_ZoneIsDetected()
    {
        var engine = new FvgDetector(0.2);
        engine.OnBar(Ctx(BarOf(0, 100, 98)));
        engine.OnBar(Ctx(BarOf(1, 100.2, 98.5)));
        engine.OnBar(Ctx(BarOf(2, 103, 101)));

        var zone = Assert.Single(engine.Zones);
        Assert.Equal(ZoneKind.FairValueGap, zone.Kind);
        Assert.Equal(ZoneState.Active, zone.State);
        Assert.Equal("Bullish", zone.Direction);
        Assert.Equal(101, zone.Top);
        Assert.Equal(100, zone.Bottom);
        Assert.Contains(engine.Events, e => e.Type == Concept.FairValueGap && e.Detail == "Created");
    }

    [Fact]
    public void ZoneBelowMinimumWidthIsSkipped()
    {
        var engine = new FvgDetector(0.2);
        engine.OnBar(Ctx(BarOf(0, 100, 98)));
        engine.OnBar(Ctx(BarOf(1, 100.2, 98.5)));
        // gap width only 0.1 < 0.2 * ATR(1)
        engine.OnBar(Ctx(BarOf(2, 101, 100.1)));

        Assert.Empty(engine.Zones);
    }

    [Fact]
    public void BearishFvg_ZoneIsDetected()
    {
        var engine = new FvgDetector(0.2);
        engine.OnBar(Ctx(BarOf(0, 102, 100)));
        engine.OnBar(Ctx(BarOf(1, 101, 99.5)));
        engine.OnBar(Ctx(BarOf(2, 99, 97)));

        var zone = Assert.Single(engine.Zones);
        Assert.Equal("Bearish", zone.Direction);
        Assert.Equal(99, zone.Bottom);
        Assert.Equal(100, zone.Top);
    }

    [Fact]
    public void BullishFvg_MitigatedWhenPriceReturns()
    {
        var engine = new FvgDetector(0.2);
        engine.OnBar(Ctx(BarOf(0, 100, 98)));
        engine.OnBar(Ctx(BarOf(1, 100.5, 98.5)));
        engine.OnBar(Ctx(BarOf(2, 103, 101.2))); // zone [100, 101.2]

        // price above zone, no mitigation
        engine.OnBar(Ctx(BarOf(3, 104, 102)));
        Assert.Equal(ZoneState.Active, engine.Zones[0].State);

        // low 99.8 reaches into zone -> mitigated
        engine.OnBar(Ctx(BarOf(4, 102, 99.8)));
        Assert.Equal(ZoneState.Mitigated, engine.Zones[0].State);
        Assert.Contains(engine.Events, e => e.Type == Concept.FairValueGap && e.Detail == "Mitigated");
    }

    [Fact]
    public void NoFvgWithoutThreeCandles()
    {
        var engine = new FvgDetector(0.2);
        engine.OnBar(Ctx(BarOf(0, 100, 98)));
        engine.OnBar(Ctx(BarOf(1, 100.2, 98.5)));
        Assert.Empty(engine.Zones);
    }
}