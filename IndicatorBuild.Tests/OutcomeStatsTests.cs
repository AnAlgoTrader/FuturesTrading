using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Analytics;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class OutcomeStatsTests
{
    private static Candle CandleAt(int i, double price, double volume = 100) =>
        new(i * 10L, price, price, price, price, volume);

    private static ConceptEvent Evt(Concept type, int time, string dir, string detail = "") =>
        new() { Time = time * 10L, Type = type, Direction = dir, Detail = detail };

    [Fact]
    public void WinRateAndExcurionsComputedInPredictedDirection()
    {
        // 3 bullish events that ride +2 ATR forward, 1 that goes adverse -> 75% win
        var candles = new List<Candle>();
        for (var i = 0; i < 40; i++)
            candles.Add(CandleAt(i, i)); // prices rise 1/bar

        var events = new List<ConceptEvent>
        {
            Evt(Concept.Pullback, 2, "Bullish"),
            Evt(Concept.Pullback, 5, "Bullish"),
            Evt(Concept.Pullback, 8, "Bullish"),
            Evt(Concept.Pullback, 10, "Bearish") // predicted down; prices rise -> lose
        };

        var rows = OutcomeStats.Collect(candles, events, forwardBars: 10, minWinAtr: 0.25, hitAtr: 1.0);
        var bull = Assert.Single(rows, r => r.Direction == "Bullish");

        Assert.Equal(3, bull.Count);
        Assert.Equal(100.0, bull.WinRate); // each +10 move ≈ much more than 0.25 ATR
        Assert.True(bull.Hit1AtrRate == 100.0);
        Assert.True(bull.Fail1AtrRate == 0.0);

        var bear = Assert.Single(rows, r => r.Direction == "Bearish");
        Assert.Equal(0.0, bear.WinRate);
        Assert.True(bear.Fail1AtrRate == 100.0);
    }

    [Fact]
    public void NullAtrFloor_ZoneWidthUsedAsScale_DoesNotDivideByZero()
    {
        // flat candles -> ATR seed = high-low = 0 for the first bar; engine must not blow up
        var candles = new List<Candle>();
        for (var i = 0; i < 30; i++)
            candles.Add(CandleAt(i, 100));
        candles[15] = new Candle(150L, 100, 101, 99, 100, 100);

        var events = new List<ConceptEvent>
        {
            Evt(Concept.ChoCh, 3, "Bullish"),
            Evt(Concept.ChoCh, 5, "Bearish")
        };

        var rows = OutcomeStats.Collect(candles, events, forwardBars: 10);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Count == 1));
    }

    [Fact]
    public void TruncatedWindowEventsAreSkipped()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 20; i++)
            candles.Add(CandleAt(i, i));

        var events = new List<ConceptEvent>
        {
            Evt(Concept.Pullback, 2, "Bullish"),  // idx 2 + 10 = 12 < 20 -> kept
            Evt(Concept.Pullback, 15, "Bullish") // idx 15 + 10 = 25 >= 20 -> skipped
        };

        var rows = OutcomeStats.Collect(candles, events, forwardBars: 10);
        var row = Assert.Single(rows);
        Assert.Equal(1, row.Count);
    }

    [Fact]
    public void NonDirectionalEventsAreIgnored()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 25; i++)
            candles.Add(CandleAt(i, i));

        var events = new List<ConceptEvent>
        {
            Evt(Concept.Compression, 3, ""),
            Evt(Concept.FairValueGap, 4, "Bullish", "Created"),
            Evt(Concept.Divergence, 6, "Bullish", "Regular")
        };

        var rows = OutcomeStats.Collect(candles, events, forwardBars: 10);
        var row = Assert.Single(rows);
        Assert.Equal("Divergence", row.Label);
        Assert.Equal("Regular", row.Detail);
        Assert.Equal(1, row.Count);
    }
}