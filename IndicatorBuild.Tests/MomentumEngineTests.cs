using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class MomentumEngineTests
{
    private static Candle CandleAt(int i, double price, double volume = 100, double wick = 0.01) =>
        new(i * 10L, price, price + wick, price - wick, price, volume);

    private static Candle ExhaustionCandle(int i, double open, double close, double high, double low, double volume) =>
        new(i * 10L, open, high, low, close, volume);

    private static BarContext Ctx(Candle candle, params SwingPoint[] swings) =>
        new(candle, atr: 1, avgVolume: 100, newSwings: swings);

    [Fact]
    public void SellExhaustion_DetectedOnLongLowerWick_HighVolume_ExtremeLowRsi()
    {
        var e = new MomentumEngine();
        for (var i = 0; i < 40; i++)
            e.OnBar(Ctx(CandleAt(i, 100 - i)));

        e.OnBar(Ctx(ExhaustionCandle(50, 60.5, 60, 60.5, 55, volume: 250)));

        var evt = Assert.Single(e.Events.Where(x => x.Type == Concept.Exhaustion));
        Assert.Equal("Bullish", evt.Direction);
        Assert.Equal("Sell", evt.Detail);
    }

    [Fact]
    public void BuyExhaustion_DetectedOnLongUpperWick_HighVolume_ExtremeHighRsi()
    {
        var e = new MomentumEngine();
        for (var i = 0; i < 40; i++)
            e.OnBar(Ctx(CandleAt(i, 100 + i)));

        e.OnBar(Ctx(ExhaustionCandle(50, 139.5, 140, 145, 139.5, volume: 250)));

        var evt = Assert.Single(e.Events.Where(x => x.Type == Concept.Exhaustion));
        Assert.Equal("Bearish", evt.Direction);
        Assert.Equal("Buy", evt.Detail);
    }

    [Fact]
    public void RegularBullishDivergence_DetectedOnLowerLowWithHigherRsi()
    {
        var e = new MomentumEngine();

        // deep decline -> low1 at bar 25 (price 72)
        for (var i = 0; i <= 25; i++)
            e.OnBar(Ctx(CandleAt(i, 100 - i), new SwingPoint(250L, 72, false, 250)));

        // rebound lifts RSI
        for (var i = 26; i <= 45; i++)
            e.OnBar(Ctx(CandleAt(i, 60 + i), new SwingPoint(450L, 92, true, 450)));

        // sharp push below the old low with RSI still well above low1's level
        for (var i = 46; i <= 58; i++)
            e.OnBar(Ctx(CandleAt(i, 92 - (i - 45)), new SwingPoint(580L, 71, false, 580)));

        var evt = Assert.Single(e.Events.Where(x => x.Type == Concept.Divergence));
        Assert.Equal("Bullish", evt.Direction);
        Assert.Equal("Regular", evt.Detail);
        Assert.Equal(580L, evt.Time);
    }

    [Fact]
    public void RegularBearishDivergence_DetectedOnHigherHighWithLowerRsi()
    {
        var e = new MomentumEngine();

        // strong rise -> high1 at bar 25 (price 128)
        for (var i = 0; i <= 25; i++)
            e.OnBar(Ctx(CandleAt(i, 100 + i), new SwingPoint(250L, 128, true, 250)));

        // sharp drop crushes RSI
        for (var i = 26; i <= 45; i++)
            e.OnBar(Ctx(CandleAt(i, 160 - i), new SwingPoint(450L, 120, false, 450)));

        // steady grind back to a new high with RSI still below high1's level
        for (var i = 46; i <= 60; i++)
            e.OnBar(Ctx(CandleAt(i, 105 + i), new SwingPoint(600L, 129.5, true, 600)));

        var evt = Assert.Single(e.Events.Where(x => x.Type == Concept.Divergence));
        Assert.Equal("Bearish", evt.Direction);
        Assert.Equal("Regular", evt.Detail);
        Assert.Equal(600L, evt.Time);
    }

    [Fact]
    public void HiddenBullishDivergence_DetectedOnHigherLowWithLowerRsi()
    {
        var e = new MomentumEngine();

        // rising market, low1 at bar 25 (price 120) with RSI high
        for (var i = 0; i <= 25; i++)
            e.OnBar(Ctx(CandleAt(i, 100 + i), new SwingPoint(250L, 120, false, 250)));

        // prolonged gentle decline -> RSI falls hard but price holds above the old low
        for (var i = 26; i <= 75; i++)
            e.OnBar(Ctx(CandleAt(i, 130 - 0.16 * (i - 25)), new SwingPoint(750L, 122, false, 750)));

        var evt = Assert.Single(e.Events.Where(x => x.Type == Concept.Divergence));
        Assert.Equal("Bullish", evt.Direction);
        Assert.Equal("Hidden", evt.Detail);
        Assert.Equal(750L, evt.Time);
    }

    [Fact]
    public void Compression_EmittedWhenBollingerSqueezesInsideKeltner()
    {
        var e = new MomentumEngine();
        for (var i = 0; i < 25; i++)
            e.OnBar(Ctx(CandleAt(i, 100)));

        var evt = Assert.Single(e.Events.Where(x => x.Type == Concept.Compression));
        Assert.Equal("tight", evt.Detail);
        Assert.Equal(190L, evt.Time);
    }

    [Fact]
    public void Expansion_EmittedOnSqueezeRelease()
    {
        var e = new MomentumEngine();
        for (var i = 0; i < 25; i++)
            e.OnBar(Ctx(CandleAt(i, 100)));

        e.OnBar(Ctx(ExhaustionCandle(30, 100, 106, 108, 99, volume: 100)));

        var evt = Assert.Single(e.Events.Where(x => x.Type == Concept.Expansion));
        Assert.Equal("release", evt.Detail);
        Assert.Equal("Bullish", evt.Direction);
        Assert.Single(e.Events.Where(x => x.Type == Concept.Compression));
    }

    [Fact]
    public void AtrExpansion_EmittedWhenAtrRatioCrossesThreshold()
    {
        var e = new MomentumEngine();
        for (var i = 0; i < 60; i++)
            e.OnBar(new BarContext(CandleAt(i, 100), atr: 1.0, avgVolume: 100));

        e.OnBar(new BarContext(CandleAt(60, 100), atr: 2.5, avgVolume: 100));

        var evt = Assert.Single(e.Events.Where(x => x.Type == Concept.Expansion));
        Assert.Equal("volatility", evt.Detail);
    }
}