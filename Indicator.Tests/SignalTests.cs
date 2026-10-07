using Xunit;

namespace Indicator.Tests;

public class SignalTests
{
    private static IndicatorResult Test(IndicatorEngine engine, Candle bar) => engine.ProcessCandle(bar);

    [Fact]
    public void NeutralBar_StaysGray()
    {
        var engine = Fixture.EngineWithFixture();
        var result = Test(engine, Fixture.Bar(5050, 5055, 5045, 5052, 1000));

        Assert.Equal(SignalType.None, result.Signal);
        Assert.Equal("#808080", result.Color);
    }

    [Fact]
    public void NoSignals_BeforeMinHistoryBars()
    {
        var engine = new IndicatorEngine();
        for (var i = 0; i < 10; i++)
        {
            var time = new TimeOnly(10, 0).AddMinutes(i);
            engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, time.Hour, time.Minute,
                5050, 5200, 4900, 5150, 5000));
        }

        var result = engine.ProcessCandle(
            Fixture.Bar(Fixture.SessionDate, 10, 10, 5050, 5200, 4900, 5150, 5000));

        Assert.Equal(SignalType.None, result.Signal);
    }

    [Fact]
    public void BullAbsorption_Green()
    {
        var engine = Fixture.EngineWithFixture();
        // Volume 2.0x SMA, spread 3 (<= 0.5x avg spread), lower wick 67% of range.
        var result = Test(engine, Fixture.Bar(5052, 5053, 5050, 5052, 2000));

        Assert.Equal(SignalType.BullAbsorption, result.Signal);
        Assert.Equal("#00C853", result.Color);
    }

    [Fact]
    public void BearAbsorption_Red()
    {
        var engine = Fixture.EngineWithFixture();
        // Volume 2.0x SMA, spread 3 (<= 0.5x avg spread), upper wick 67% of range.
        var result = Test(engine, Fixture.Bar(5052, 5054, 5051, 5052, 2000));

        Assert.Equal(SignalType.BearAbsorption, result.Signal);
        Assert.Equal("#FF1744", result.Color);
    }

    [Fact]
    public void Absorption_WhenBothWicksQualify_TieBreaksToLargerWick()
    {
        var engine = Fixture.EngineWithFixture();
        // Equal 50% wicks on both sides -> lower ratio >= upper ratio -> bullish.
        var result = Test(engine, Fixture.Bar(5051, 5053, 5049, 5051, 2000));

        Assert.Equal(SignalType.BullAbsorption, result.Signal);
    }

    [Fact]
    public void BullSweep_Cyan()
    {
        var engine = Fixture.EngineWithFixture();
        // Trades below PDL 5000 (and ONL 5010), closes back above.
        var result = Test(engine, Fixture.Bar(5010, 5020, 4995, 5015, 500));

        Assert.Equal(SignalType.BullSweep, result.Signal);
        Assert.Equal("#00E5FF", result.Color);
    }

    [Fact]
    public void BearSweep_Orange()
    {
        var engine = Fixture.EngineWithFixture();
        // Trades above PDH 5100, closes back below.
        var result = Test(engine, Fixture.Bar(5090, 5105, 5085, 5092, 500));

        Assert.Equal(SignalType.BearSweep, result.Signal);
        Assert.Equal("#FF9100", result.Color);
    }

    [Fact]
    public void VolumeExpansion_Yellow()
    {
        var engine = Fixture.EngineWithFixture();
        // Volume 2.5x SMA, spread 30 (>= 1.5x avg spread).
        var result = Test(engine, Fixture.Bar(5050, 5070, 5040, 5065, 2500));

        Assert.Equal(SignalType.VolumeExpansion, result.Signal);
        Assert.Equal("#FFEA00", result.Color);
    }

    [Fact]
    public void Priority_SweepBeatsExpansion()
    {
        var engine = Fixture.EngineWithFixture();
        // PDL sweep AND volume expansion on the same bar.
        var result = Test(engine, Fixture.Bar(5010, 5040, 4995, 5015, 3000));

        Assert.Equal(SignalType.BullSweep, result.Signal);
    }

    [Fact]
    public void Priority_SweepBeatsAbsorption()
    {
        var engine = Fixture.EngineWithFixture();
        // Narrow spread + high volume + 80% lower wick + PDL sweep on the same bar.
        var result = Test(engine, Fixture.Bar(5003, 5004, 4999, 5003, 3000));

        Assert.Equal(SignalType.BullSweep, result.Signal);
    }

    [Fact]
    public void NoAbsorption_WithoutHighVolume()
    {
        var engine = Fixture.EngineWithFixture();
        // Perfect bull absorption shape but volume below the 1.5x threshold.
        var result = Test(engine, Fixture.Bar(5052, 5053, 5050, 5052, 1400));

        Assert.Equal(SignalType.None, result.Signal);
    }

    [Fact]
    public void NoAbsorption_WithoutNarrowSpread()
    {
        var engine = Fixture.EngineWithFixture();
        // High volume and 60% lower wick but spread 6 > 0.5x avg spread (5).
        var result = Test(engine, Fixture.Bar(5052, 5056, 5050, 5052, 2000));

        Assert.Equal(SignalType.None, result.Signal);
    }

    [Fact]
    public void CustomColors_AreUsed()
    {
        var options = new IndicatorOptions
        {
            BullSweepColor = "#123456",
            NeutralColor = "#111111"
        };
        var engine = new IndicatorEngine(options);
        foreach (var bar in Fixture.Build())
            engine.ProcessCandle(bar);

        var sweep = engine.ProcessCandle(Fixture.Bar(5010, 5020, 4995, 5015, 500));
        Assert.Equal("#123456", sweep.Color);

        engine.Reset();
        foreach (var bar in Fixture.Build())
            engine.ProcessCandle(bar);
        var neutral = engine.ProcessCandle(Fixture.Bar(5050, 5055, 5045, 5052, 1000));
        Assert.Equal("#111111", neutral.Color);
    }
}
