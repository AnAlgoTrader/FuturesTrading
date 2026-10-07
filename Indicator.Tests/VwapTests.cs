using Xunit;

namespace Indicator.Tests;

public class VwapTests
{
    [Fact]
    public void Vwap_UsesTypicalPriceTimesVolume()
    {
        var engine = new IndicatorEngine();
        engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 0, 5000, 5010, 4990, 5000, 100));
        var result = engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 1, 5010, 5020, 4996, 5014, 300));

        // (5000*100 + 5010*300) / 400 = 5007.5
        Assert.Equal(5007.5, result.Vwap, 6);
    }

    [Fact]
    public void Vwap_BandsUseWeightedStandardDeviation()
    {
        var engine = new IndicatorEngine();
        engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 0, 5000, 5010, 4990, 5000, 100));
        var result = engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 1, 5010, 5020, 4996, 5014, 300));

        var vwap = 5007.5;
        var variance = (100 * 5000.0 * 5000.0 + 300 * 5010.0 * 5010.0) / 400.0 - vwap * vwap;
        var stdDev = Math.Sqrt(variance);

        Assert.Equal(vwap + stdDev, result.UpperBand1!.Value, 6);
        Assert.Equal(vwap + 2 * stdDev, result.UpperBand2!.Value, 6);
        Assert.Equal(vwap - stdDev, result.LowerBand1!.Value, 6);
        Assert.Equal(vwap - 2 * stdDev, result.LowerBand2!.Value, 6);
    }

    [Fact]
    public void Vwap_BandsNullBeforeAnyRthVolume()
    {
        var engine = new IndicatorEngine();
        var result = engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 3, 0, 5000, 5010, 4990, 5000, 100));

        Assert.Equal(0, result.Vwap);
        Assert.Null(result.UpperBand1);
        Assert.Null(result.LowerBand1);
    }

    [Fact]
    public void Vwap_ResetsAtNextRthSession()
    {
        var engine = new IndicatorEngine();
        engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 0, 5000, 5010, 4990, 5000, 100));
        engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 1, 5010, 5020, 4996, 5014, 300));
        var beforeReset = engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 2, 5000, 5010, 4990, 5000, 100));
        Assert.Equal((5000.0 * 100 + 5010.0 * 300 + 5000.0 * 100) / 500.0, beforeReset.Vwap, 6);

        var nextSession = new DateOnly(2026, 1, 14);
        var result = engine.ProcessCandle(Fixture.Bar(nextSession, 9, 31, 5100, 5110, 5090, 5100, 100));

        // Only the new session's typical price (5100) counts.
        Assert.Equal(5100, result.Vwap, 6);
    }

    [Fact]
    public void Vwap_IgnoresEthCandles()
    {
        var engine = new IndicatorEngine();
        engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 0, 5000, 5010, 4990, 5000, 100));

        // 20:00 ET is overnight (next session date, not RTH) - VWAP must not move.
        var result = engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 20, 0, 6000, 6010, 5990, 6000, 999));

        Assert.Equal(5000, result.Vwap, 6);
    }

    [Fact]
    public void Vwap_CarriesLastValueIntoNextSessionBeforeRth()
    {
        var engine = new IndicatorEngine();
        engine.ProcessCandle(Fixture.Bar(Fixture.SessionDate, 10, 0, 5000, 5010, 4990, 5000, 100));

        // New session, but still overnight: previous RTH VWAP persists until 09:30.
        var result = engine.ProcessCandle(Fixture.Bar(new DateOnly(2026, 1, 14), 3, 0, 6000, 6010, 5990, 6000, 999));

        Assert.Equal(5000, result.Vwap, 6);
    }

    [Fact]
    public void Vwap_EngineAndFormingAgree()
    {
        var engine = Fixture.EngineWithFixture();
        var bar = Fixture.Bar(5050, 5060, 5040, 5055, 2000);

        var forming = engine.EvaluateForming(bar);
        var committed = engine.ProcessCandle(bar);

        Assert.Equal(forming.Vwap, committed.Vwap, 10);
        Assert.Equal(forming.UpperBand1, committed.UpperBand1);
        Assert.Equal(forming.LowerBand2, committed.LowerBand2);
    }
}
