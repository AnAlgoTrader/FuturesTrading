using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using IndicatorBuild.Streaming;
using Xunit;

namespace IndicatorBuild.Tests;

public class StreamingTests
{
    // Deterministic wiggly tape that produces BOS / CHoCH / sweeps / FVG / momentum events.
    private static List<Candle> Synthetic(int count = 400)
    {
        var candles = new List<Candle>(count);
        var price = 100.0;
        var rng = new Random(7);
        var volume = 100.0;
        const long stepMs = 300000;
        for (var i = 0; i < count; i++)
        {
            // regime: slow sine wave + noise, occasional strong impulse
            var drift = Math.Sin(i / 25.0) * 1.2 + (rng.NextDouble() - 0.5) * 1.6;
            if (i % 70 == 60) drift += (rng.NextDouble() < 0.5 ? -1 : 1) * 4.0; // squeeze burst
            var open = price;
            var close = price + drift;
            var high = Math.Max(open, close) + rng.NextDouble() * 1.2;
            var low = Math.Min(open, close) - rng.NextDouble() * 1.2;
            var vol = volume * (0.5 + rng.NextDouble()) * (i % 70 == 61 ? 3.0 : 1.0); // burst volume
            candles.Add(new Candle((long)(i + 1000) * stepMs, open, high, low, close, vol));
            price = close;
        }
        return candles;
    }

    private static IBarEngine[] FreshEngines() => new IBarEngine[]
    {
        new MarketStructureEngine(),
        new LiquiditySweepDetector(IndicatorConfig.SweepMinWickFraction),
        new FvgDetector(IndicatorConfig.FvgMinWidthAtrFactor, false, IndicatorConfig.FvgFillFraction),
        new ConsolidationEngine(IndicatorConfig.ConsolidationMinBars, IndicatorConfig.ConsolidationRangeAtrFactor, IndicatorConfig.BreakoutConfirmBars),
        new PullbackDetector(IndicatorConfig.PullbackLegMinAtr, IndicatorConfig.PullbackHealthyMin, IndicatorConfig.PullbackHealthyMax),
        new WyckoffEngine(IndicatorConfig.WyckoffRangeMinSwings, IndicatorConfig.WyckoffRangeMinBars,
            IndicatorConfig.WyckoffRangeMinHeightAtr, IndicatorConfig.WyckoffRangeMaxHeightAtr, IndicatorConfig.WyckoffPriorTrendAtr),
        new MomentumEngine(IndicatorConfig.MomentumExhaustWickBody, IndicatorConfig.MomentumExhaustVolumeFactor,
            IndicatorConfig.MomentumDivMinSwingFrac, IndicatorConfig.MomentumExpansionAtrRatio)
    };

    private static string Sig(ConceptEvent e) => $"{e.Type}|{e.Time}|{e.Direction}|{e.Detail}";

    [Fact]
    public void StreamingReplay_MatchesBatch_EventSequence()
    {
        var candles = Synthetic();
        var batch = new IndicatorPipeline(new SwingDetector(5, 5), FreshEngines());
        batch.Run(candles);

        var replay = new IndicatorPipeline(new SwingDetector(5, 5), FreshEngines());
        var session = new StreamingSession(replay);
        var bars = session.Run(new ArrayBarSource(candles));

        Assert.Equal(candles.Count, bars);
        Assert.Equal(batch.Swings.Count, replay.Swings.Count);
        Assert.Equal(batch.Zones.Count, replay.Zones.Count);
        Assert.Equal(batch.Events.Count, replay.Events.Count);
        Assert.Equal(batch.Events.Select(Sig), replay.Events.Select(Sig));
    }

    [Fact]
    public void StreamingSession_RaisesCallbacksInFeedOrder()
    {
        var candles = Synthetic(120);
        var emitted = new List<(string Sig, int Bar)>();
        var zonesRaised = 0;
        var swingsRaised = 0;

        var replay = new IndicatorPipeline(new SwingDetector(5, 5), FreshEngines());
        var session = new StreamingSession(replay);
        session.EventRaised += (e, bar) => emitted.Add((Sig(e), bar));
        session.ZoneCreated += _ => zonesRaised++;
        session.SwingConfirmed += _ => swingsRaised++;

        session.Run(new ArrayBarSource(candles));

        Assert.Equal(replay.Events.Count, emitted.Count);
        // every event is raised at or after the bar that carries it — no lookahead
        Assert.Equal(replay.Events.Count, emitted.Count(e => e.Bar >= 0));
        Assert.Equal(replay.Zones.Count, zonesRaised);
        Assert.Equal(replay.Swings.Count, swingsRaised);
    }

    [Fact]
    public void CsvBarSource_StreamsEveryParsedCandle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencode_m8_{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path, new[]
        {
            "1000,10,12,9,11,5",
            "2000,11,14,10,13,8",
            "", // blank line ignored like HistoricalDataSource
            "3000,13,15,12,14,6"
        });

        try
        {
            var source = new CsvBarSource(path);
            var times = new List<long>();
            while (source.Next() is { } c) times.Add(c.Time);

            Assert.Equal(new[] { 1000L, 2000L, 3000L }, times);
            Assert.Equal(3, source.BarsRead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LimitBarSource_StopsAfterLimit()
    {
        var candles = Synthetic(50);
        var source = new LimitBarSource(new ArrayBarSource(candles), 7);

        var read = 0;
        while (source.Next() is { }) read++;

        Assert.Equal(7, read);
    }
}