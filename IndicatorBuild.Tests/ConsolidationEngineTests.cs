using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class ConsolidationEngineTests
{
    private static Candle Bar(int time, double open, double high, double low, double close) =>
        new(time, open, high, low, close, 1);

    private static BarContext Ctx(Candle candle) => new(candle, atr: 1, avgVolume: 100);

    [Fact]
    public void TightBand_ThenUpBreakout_EmitsZoneAndBreakout()
    {
        var engine = new ConsolidationEngine(minBars: 4, rangeAtrFactor: 2.0, holdBars: 3);

        // 5 bars in a tight 100 +/- 0.6 band (height 1.2 < 2.0 * (ATR 1))
        for (var i = 0; i < 5; i++)
            engine.OnBar(Ctx(Bar(i, 100, 100.6, 99.4, 100)));

        // wide displacement bar closing above the band high
        engine.OnBar(Ctx(Bar(5, 100, 103, 99, 102.5)));

        var zone = Assert.Single(engine.Zones);
        Assert.Equal(ZoneKind.Consolidation, zone.Kind);
        Assert.Equal("Bullish", zone.Direction);
        Assert.Equal(100.6, zone.Top);
        Assert.Equal(99.4, zone.Bottom);

        Assert.Contains(engine.Events, e => e.Type == Concept.Breakout && e.Direction == "Bullish" && e.Detail == "");
    }

    [Fact]
    public void Breakout_HeldWhenClosesStayBeyond()
    {
        var engine = new ConsolidationEngine(minBars: 4, rangeAtrFactor: 2.0, holdBars: 3);

        for (var i = 0; i < 5; i++)
            engine.OnBar(Ctx(Bar(i, 100, 100.6, 99.4, 100)));

        // breakout bar
        engine.OnBar(Ctx(Bar(5, 100, 103, 99, 102.5)));
        // 3 closes above band high 100.6 -> held
        engine.OnBar(Ctx(Bar(6, 101, 101.4, 100.8, 101.2)));
        engine.OnBar(Ctx(Bar(7, 101, 101.4, 100.8, 101.2)));
        engine.OnBar(Ctx(Bar(8, 101, 101.4, 100.8, 101.2)));

        Assert.Contains(engine.Events, e => e.Type == Concept.Breakout && e.Detail == "Held");
        Assert.DoesNotContain(engine.Events, e => e.Detail == "Failed");
    }

    [Fact]
    public void Breakout_FailedWhenCloseReturnsInside()
    {
        var engine = new ConsolidationEngine(minBars: 4, rangeAtrFactor: 2.0, holdBars: 3);

        for (var i = 0; i < 5; i++)
            engine.OnBar(Ctx(Bar(i, 100, 100.6, 99.4, 100)));

        engine.OnBar(Ctx(Bar(5, 100, 103, 99, 102.5)));
        // next close back inside the band -> failed
        engine.OnBar(Ctx(Bar(6, 100, 100.9, 99.6, 100.3)));

        Assert.Contains(engine.Events, e => e.Type == Concept.Breakout && e.Detail == "Failed");
        Assert.DoesNotContain(engine.Events, e => e.Detail == "Held");
    }

    [Fact]
    public void ShortBand_NoBreakout()
    {
        var engine = new ConsolidationEngine(minBars: 4, rangeAtrFactor: 2.0, holdBars: 3);

        // only 2 tight bars, then a wide bar -> not enough bars
        engine.OnBar(Ctx(Bar(0, 100, 100.6, 99.4, 100)));
        engine.OnBar(Ctx(Bar(1, 100, 100.6, 99.4, 100)));
        engine.OnBar(Ctx(Bar(2, 100, 103, 99, 102.5)));

        Assert.Empty(engine.Zones);
        Assert.Empty(engine.Events);
    }
}