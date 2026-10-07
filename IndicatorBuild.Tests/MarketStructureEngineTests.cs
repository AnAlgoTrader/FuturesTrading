using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class MarketStructureEngineTests
{
    private static Candle CandleAt(int time, double price) =>
        new(time, price - 0.5, price + 0.5, price - 1.0, price, 1);

    private static BarContext Ctx(Candle candle, params SwingPoint[] newSwings) =>
        new(candle, atr: 1, avgVolume: 100, newSwings: newSwings);

    [Fact]
    public void BullishBootstrap_BosThenBearishChoch()
    {
        var engine = new MarketStructureEngine();

        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));
        engine.OnBar(Ctx(CandleAt(1, 105), new SwingPoint(1, 105, true, 1)));

        Assert.Equal(TrendState.Bullish, engine.Trend);

        engine.OnBar(Ctx(CandleAt(2, 106), new SwingPoint(2, 104, false, 2)));
        engine.OnBar(Ctx(CandleAt(3, 103)));

        var events = engine.Events;
        Assert.Equal(2, events.Count);
        Assert.Equal(Concept.BOS, events[0].Type);
        Assert.Equal("Bullish", events[0].Direction);
        Assert.Equal(Concept.ChoCh, events[1].Type);
        Assert.Equal("Bearish", events[1].Direction);
        Assert.Equal(TrendState.Bearish, engine.Trend);
    }

    [Fact]
    public void BearishBootstrap_BosThenBullishChoch()
    {
        var engine = new MarketStructureEngine();

        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, true, 0)));
        engine.OnBar(Ctx(CandleAt(1, 95), new SwingPoint(1, 95, false, 1)));

        Assert.Equal(TrendState.Bearish, engine.Trend);

        engine.OnBar(Ctx(CandleAt(2, 94), new SwingPoint(2, 96, true, 2)));
        engine.OnBar(Ctx(CandleAt(3, 97)));

        var events = engine.Events;
        Assert.Equal(2, events.Count);
        Assert.Equal(Concept.BOS, events[0].Type);
        Assert.Equal("Bearish", events[0].Direction);
        Assert.Equal(Concept.ChoCh, events[1].Type);
        Assert.Equal("Bullish", events[1].Direction);
        Assert.Equal(TrendState.Bullish, engine.Trend);
    }

    [Fact]
    public void BreakEmitsOnceUntilNewSwing()
    {
        var engine = new MarketStructureEngine();

        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));
        engine.OnBar(Ctx(CandleAt(1, 105), new SwingPoint(1, 105, true, 1)));

        engine.OnBar(Ctx(CandleAt(2, 106)));
        engine.OnBar(Ctx(CandleAt(3, 107)));

        Assert.Single(engine.Events);
        Assert.Equal(Concept.BOS, engine.Events[0].Type);
    }

    [Fact]
    public void BootstrapsBullishWhenHighConfirmAfterLow()
    {
        var engine = new MarketStructureEngine();
        engine.OnBar(Ctx(CandleAt(0, 100), new SwingPoint(0, 100, false, 0)));
        engine.OnBar(Ctx(CandleAt(1, 105), new SwingPoint(1, 105, true, 1)));

        // later swing low (idx2) updates the reference level; close above the new high is a fresh BOS
        engine.OnBar(Ctx(CandleAt(2, 109), new SwingPoint(2, 99, false, 2), new SwingPoint(2, 108, true, 2)));
        engine.OnBar(Ctx(CandleAt(3, 107)));
        // still bullish, nothing emitted on bar 3 (level already broken)
        Assert.Single(engine.Events);
    }
}