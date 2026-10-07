using Xunit;

namespace Indicator.Tests;

public class SessionLevelsTests
{
    [Fact]
    public void PreviousDayLevels_FromPreviousRthSession()
    {
        var engine = Fixture.EngineWithFixture();
        var levels = engine.ProcessCandle(Fixture.Bar(5050, 5055, 5045, 5052, 1000)).Levels;

        Assert.Equal(Fixture.SessionDate, levels.SessionDate);
        Assert.Equal(Fixture.Pdh, levels.PreviousDayHigh);
        Assert.Equal(Fixture.Pdl, levels.PreviousDayLow);
        Assert.Equal(Fixture.Pdc, levels.PreviousDayClose);
        Assert.False(levels.PreviousDayHighBroken);
        Assert.False(levels.PreviousDayLowBroken);
    }

    [Fact]
    public void PreviousDayLevels_NullWithoutPreviousSessionData()
    {
        var engine = new IndicatorEngine();
        engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 9, 30, 5030, 5070, 5030, 5060, 1000));

        var levels = engine.ProcessCandle(Fixture.Bar(5050, 5055, 5045, 5052, 1000)).Levels;

        Assert.Null(levels.PreviousDayHigh);
        Assert.Null(levels.PreviousDayLow);
        Assert.Null(levels.PreviousDayClose);
    }

    [Fact]
    public void OvernightLevels_TrackEthWindow()
    {
        var engine = new IndicatorEngine();
        engine.ProcessCandle(Fixture.Bar(Fixture.PrevDate, 9, 30, 5010, 5060, 5000, 5050, 1000));
        engine.ProcessCandle(Fixture.Bar(Fixture.PrevDate, 9, 31, 5050, 5100, 5040, 5080, 1000));
        engine.ProcessCandle(Fixture.Bar(Fixture.PrevDate, 18, 5, 5050, 5090, 5040, 5070, 1000));

        var levels = engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 3, 0, 5070, 5080, 5010, 5020, 1000))
            .Levels;

        Assert.Equal(Fixture.Onh, levels.OvernightHigh);
        Assert.Equal(Fixture.Onl, levels.OvernightLow);
        Assert.False(levels.OvernightHighBroken);
        Assert.False(levels.OvernightLowBroken);
    }

    [Fact]
    public void InitialBalanceLevels_TrackFirstSixtyMinutes()
    {
        var engine = Fixture.EngineWithFixture();
        var levels = engine.ProcessCandle(Fixture.Bar(5050, 5055, 5045, 5052, 1000)).Levels;

        Assert.Equal(Fixture.Ibh, levels.InitialBalanceHigh);
        Assert.Equal(Fixture.Ibl, levels.InitialBalanceLow);
        Assert.False(levels.InitialBalanceHighBroken);
        Assert.False(levels.InitialBalanceLowBroken);
        Assert.False(levels.OvernightHighBroken);
    }

    [Fact]
    public void PreviousDayLow_BrokenWhenPriceTradesBelow()
    {
        var engine = Fixture.EngineWithFixture();
        var levels = engine.ProcessCandle(Fixture.Bar(5010, 5020, 4995, 5015, 500)).Levels;

        Assert.True(levels.PreviousDayLowBroken);
        Assert.False(levels.PreviousDayHighBroken);
    }

    [Fact]
    public void InitialBalanceHigh_BrokenAfterIbWindowEnds()
    {
        var engine = Fixture.EngineWithFixture();
        var levels = engine.ProcessCandle(Fixture.Bar(5075, 5080, 5070, 5078, 1000)).Levels;

        Assert.True(levels.InitialBalanceHighBroken);
        Assert.False(levels.OvernightHighBroken);
        Assert.False(levels.PreviousDayHighBroken);
    }

    [Fact]
    public void OvernightHigh_BrokenAfterRthStart()
    {
        var engine = Fixture.EngineWithFixture();
        var levels = engine.ProcessCandle(Fixture.Bar(5085, 5095, 5080, 5090, 1000)).Levels;

        Assert.True(levels.OvernightHighBroken);
        Assert.True(levels.InitialBalanceHighBroken);
    }

    [Fact]
    public void Levels_RollOverToNewSession()
    {
        var engine = Fixture.EngineWithFixture();
        engine.ProcessCandle(Fixture.Bar(5050, 5055, 5045, 5052, 1000));

        // Next session (Wed 2026-01-14): previous day is now Tue 2026-01-13.
        engine.ProcessCandle(Fixture.Bar(new DateOnly(2026, 1, 14), 11, 0, 5000, 5010, 4990, 5005, 1000));

        var levels = engine.ProcessCandle(
            Fixture.Bar(new DateOnly(2026, 1, 14), 11, 5, 5005, 5015, 4995, 5010, 1000)).Levels;

        Assert.Equal(new DateOnly(2026, 1, 14), levels.SessionDate);
        Assert.NotNull(levels.PreviousDayHigh);
        Assert.NotNull(levels.PreviousDayLow);
        Assert.Null(levels.OvernightHigh);
        Assert.Null(levels.InitialBalanceHigh);
        Assert.False(levels.PreviousDayHighBroken);
    }
}
