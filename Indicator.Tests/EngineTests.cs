using Xunit;

namespace Indicator.Tests;

public class EngineTests
{
    private static readonly DateOnly Date = new(2026, 1, 13);

    [Fact]
    public void ProcessCandle_RejectsNonIncreasingTime()
    {
        var engine = new IndicatorEngine();
        var first = Fixture.Bar(Date, 12, 0, 5050, 5055, 5045, 5052, 1000);
        var second = Fixture.Bar(Date, 12, 1, 5050, 5055, 5045, 5052, 1000);
        engine.ProcessCandle(first);
        engine.ProcessCandle(second);

        Assert.Throws<ArgumentException>(() => engine.ProcessCandle(second));
        Assert.Throws<ArgumentException>(() =>
            engine.ProcessCandle(Fixture.Bar(Date, 11, 59, 5050, 5055, 5045, 5052, 1000)));
    }

    [Fact]
    public void EvaluateForming_ThrowsForOlderTime()
    {
        var engine = new IndicatorEngine();
        engine.ProcessCandle(Fixture.Bar(Date, 12, 1, 5050, 5055, 5045, 5052, 1000));

        Assert.Throws<ArgumentException>(() =>
            engine.EvaluateForming(Fixture.Bar(Date, 12, 0, 5050, 5055, 5045, 5052, 1000)));
    }

    [Fact]
    public void EvaluateForming_DoesNotMutateState()
    {
        var engine = Fixture.EngineWithFixture();
        var bar = Fixture.Bar(5050, 5055, 5045, 5052, 2000);

        var forming = engine.EvaluateForming(bar);
        var committed = engine.ProcessCandle(bar);

        Assert.Equal(forming.Signal, committed.Signal);
        Assert.Equal(forming.Color, committed.Color);
        Assert.Equal(forming.Vwap, committed.Vwap, 10);
        Assert.Equal(forming.Levels, committed.Levels);
    }

    [Fact]
    public void EvaluateForming_IsRepeatable()
    {
        var engine = Fixture.EngineWithFixture();
        var bar = Fixture.Bar(5050, 5055, 5045, 5052, 2000);

        var first = engine.EvaluateForming(bar);
        var second = engine.EvaluateForming(bar);

        Assert.Equal(first.Signal, second.Signal);
        Assert.Equal(first.Color, second.Color);
        Assert.Equal(first.Vwap, second.Vwap, 10);
        Assert.Equal(first.Levels, second.Levels);
        Assert.Equal(first.ActiveSwings, second.ActiveSwings);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var engine = Fixture.EngineWithFixture();
        engine.ProcessCandle(Fixture.Bar(5010, 5020, 4995, 5015, 500));

        engine.Reset();

        var afterReset = engine.ProcessCandle(Fixture.Bar(5050, 5055, 5045, 5052, 1000));
        Assert.Null(afterReset.Levels.PreviousDayHigh);
        Assert.Equal(SignalType.None, afterReset.Signal);
        Assert.Empty(afterReset.ActiveSwings);
        // VWAP restarts from this single candle's typical price: (5055 + 5045 + 5052) / 3.
        Assert.Equal((5055 + 5045 + 5052) / 3.0, afterReset.Vwap, 6);

        engine.Reset();
        foreach (var bar in Fixture.Build())
            engine.ProcessCandle(bar);
        var replayed = engine.ProcessCandle(Fixture.Bar(5010, 5020, 4995, 5015, 500));
        Assert.Equal(SignalType.BullSweep, replayed.Signal);
        Assert.Equal(Fixture.Pdh, replayed.Levels.PreviousDayHigh);
    }

    [Fact]
    public void ProcessCandle_RejectsInvalidCandles()
    {
        var engine = new IndicatorEngine();

        Assert.Throws<ArgumentException>(() =>
            engine.ProcessCandle(new Candle(1, 5000, 4990, 5010, 5000, 100)));
        Assert.Throws<ArgumentException>(() =>
            engine.ProcessCandle(new Candle(1, 5000, 5010, 4990, 4980, 100)));
        Assert.Throws<ArgumentException>(() =>
            engine.ProcessCandle(new Candle(1, 0, 0, 0, 0, 100)));
        Assert.Throws<ArgumentException>(() =>
            engine.ProcessCandle(new Candle(1, 5000, 5010, 4990, 5000, -1)));
    }

    [Fact]
    public void SessionRollover_DuringFormingIsIdempotent()
    {
        var engine = Fixture.EngineWithFixture();
        var nextSessionBar = Fixture.Bar(new DateOnly(2026, 1, 14), 10, 0, 5000, 5010, 4990, 5005, 1000);

        var forming = engine.EvaluateForming(nextSessionBar);
        var committed = engine.ProcessCandle(nextSessionBar);

        Assert.Equal(forming.Levels, committed.Levels);
        Assert.Equal(new DateOnly(2026, 1, 14), committed.Levels.SessionDate);
        Assert.NotNull(committed.Levels.PreviousDayHigh);
    }
}
