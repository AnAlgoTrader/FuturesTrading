using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Analytics;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using IndicatorBuild.Rendering;
using Xunit;

namespace IndicatorBuild.Tests;

public class ConfluenceScorerTests
{
    private static Candle CandleAt(int i, double price, double volume = 100) =>
        new(i * 10L, price, price, price, price, volume);

    private static CandleAnalysis Analysis(Candle c, params ConceptEvent[] evts)
    {
        var a = new CandleAnalysis(c);
        a.Events.AddRange(evts);
        return a;
    }

    private static ConceptEvent Evt(Concept type, string dir) =>
        new() { Time = 0L, Type = type, Direction = dir };

    [Fact]
    public void BullishEvent_FullyAligned_ScoresFour_Qualified()
    {
        // rising prices -> EMA below close, RSI > 50; big bullish body + volume spike
        var candles = new List<Candle>();
        for (var i = 0; i < 70; i++)
            candles.Add(i < 60 ? CandleAt(i, 90 + i) : new Candle(i * 10L, 140, 146, 140, 146, 300));

        var analyses = candles.Select(c => new CandleAnalysis(c)).ToList();
        analyses[69].Events.Add(Evt(Concept.Breakout, "Bullish"));

        ConfluenceScorer.Score(candles, analyses, gate: 2);

        var evt = analyses[69].Events.Single();
        Assert.Equal(4, evt.Score);
        Assert.True(evt.Qualified);
    }

    [Fact]
    public void CounterTrendEvent_LowScore_Unqualified()
    {
        // rising prices but a BEARISH event -> trend + RSI + body all fight it; low volume
        var candles = new List<Candle>();
        for (var i = 0; i < 70; i++)
            candles.Add(CandleAt(i, 90 + i));
        var analysis = Analysis(candles.Last(), Evt(Concept.ChoCh, "Bearish"));
        var analyses = candles.Select((c, i) => i == 69 ? analysis : new CandleAnalysis(c)).ToList();

        ConfluenceScorer.Score(candles, analyses, gate: 2);

        Assert.Equal(0, analysis.Events.Single().Score);
        Assert.False(analysis.Events.Single().Qualified);
    }

    [Fact]
    public void NonGatedEvent_IsNeverDegraded()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 60; i++)
            candles.Add(CandleAt(i, 90 + i));

        var analysis = Analysis(candles[59], Evt(Concept.BOS, "Bullish"));
        var analyses = candles.Select((c, i) => i == 59 ? analysis : new CandleAnalysis(c)).ToList();

        ConfluenceScorer.Score(candles, analyses, gate: 99);

        var evt = analysis.Events.Single();
        Assert.True(evt.Qualified);
        Assert.Equal(0, evt.Score);
    }

    [Fact]
    public void Resolver_KeepsBelowGateEventGray()
    {
        var analysis = Analysis(new Candle(1L, 100, 105, 95, 100, 100),
            Evt(Concept.ChoCh, "Bearish"));
        analysis.Events[0].Qualified = false;

        Assert.Equal(IndicatorConfig.DefaultColor, ColorResolver.Resolve(analysis));

        analysis.Events[0].Qualified = true;
        Assert.Equal(IndicatorConfig.BearColor, ColorResolver.Resolve(analysis));
    }
}

public class OutcomeCompareTests
{
    [Fact]
    public void Compare_DeltasGatedWinAgainstAll()
    {
        var all = new List<OutcomeRow>
        {
            new() { Label = "A", Detail = "", Direction = "Bullish", Count = 40, WinRate = 45 },
            new() { Label = "B", Detail = "", Direction = "Bullish", Count = 10, WinRate = 60 }
        };
        var gated = new List<OutcomeRow>
        {
            new() { Label = "A", Detail = "", Direction = "Bullish", Count = 20, WinRate = 70 }
        };

        var rows = OutcomeCompare.Build(all, gated);

        var a = Assert.Single(rows, r => r.Label == "A");
        Assert.Equal(40, a.AllCount);
        Assert.Equal(20, a.GatedCount);
        Assert.Equal(45, a.AllWin);
        Assert.Equal(70, a.GatedWin);

        var b = Assert.Single(rows, r => r.Label == "B");
        Assert.Equal(0, b.GatedCount);
        Assert.Null(b.GatedWin);
    }
}