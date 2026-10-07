using Xunit;

namespace Indicator.Tests;

public class SwingTests
{
    [Fact]
    public void DetectsConfirmedPivotHigh()
    {
        var engine = new IndicatorEngine();
        var bars = new[]
        {
            Bar(0, 5050, 5060, 5040, 5055),
            Bar(1, 5055, 5065, 5045, 5060),
            Bar(2, 5060, 5070, 5050, 5065),
            Bar(3, 5065, 5080, 5055, 5070),
            Bar(4, 5070, 5070, 5045, 5050),
            Bar(5, 5050, 5065, 5040, 5045),
            Bar(6, 5045, 5060, 5035, 5050)
        };

        IndicatorResult? result = null;
        foreach (var bar in bars)
            result = engine.ProcessCandle(bar);

        var swing = Assert.Single(result!.ActiveSwings);
        Assert.True(swing.IsHigh);
        Assert.Equal(5080, swing.Price);
        Assert.Equal(bars[3].Time, swing.Time);
    }

    [Fact]
    public void DetectsConfirmedPivotLow()
    {
        var engine = new IndicatorEngine();
        var bars = new[]
        {
            Bar(0, 5050, 5060, 5040, 5045),
            Bar(1, 5045, 5055, 5035, 5040),
            Bar(2, 5040, 5050, 5030, 5035),
            Bar(3, 5035, 5045, 5020, 5040),
            Bar(4, 5040, 5050, 5030, 5045),
            Bar(5, 5045, 5055, 5035, 5050),
            Bar(6, 5050, 5060, 5040, 5055)
        };

        IndicatorResult? result = null;
        foreach (var bar in bars)
            result = engine.ProcessCandle(bar);

        var swing = Assert.Single(result!.ActiveSwings);
        Assert.False(swing.IsHigh);
        Assert.Equal(5020, swing.Price);
        Assert.Equal(bars[3].Time, swing.Time);
    }

    [Fact]
    public void BrokenSwing_IsRemoved()
    {
        var engine = new IndicatorEngine();
        var bars = new[]
        {
            Bar(0, 5050, 5060, 5040, 5055),
            Bar(1, 5055, 5065, 5045, 5060),
            Bar(2, 5060, 5070, 5050, 5065),
            Bar(3, 5065, 5080, 5055, 5070),
            Bar(4, 5070, 5070, 5045, 5050),
            Bar(5, 5050, 5065, 5040, 5045),
            Bar(6, 5045, 5060, 5035, 5050)
        };
        foreach (var bar in bars)
            engine.ProcessCandle(bar);

        engine.ProcessCandle(Bar(7, 5050, 5085, 5045, 5080));

        var result = engine.EvaluateForming(Bar(8, 5080, 5082, 5075, 5081));
        Assert.Empty(result.ActiveSwings);
    }

    [Fact]
    public void FlatMarket_HasNoSwings()
    {
        var engine = new IndicatorEngine();
        IndicatorResult? result = null;
        for (var i = 0; i < 12; i++)
            result = engine.ProcessCandle(Bar(i, 5050, 5055, 5045, 5052));

        Assert.Empty(result!.ActiveSwings);
    }

    [Fact]
    public void PivotStrength_IsConfigurable()
    {
        var engine = new IndicatorEngine(new IndicatorOptions { PivotLeft = 1, PivotRight = 1 });
        IndicatorResult? result = null;
        foreach (var bar in new[]
        {
            Bar(0, 5050, 5060, 5040, 5055),
            Bar(1, 5055, 5080, 5050, 5075),
            Bar(2, 5075, 5078, 5050, 5060)
        })
            result = engine.ProcessCandle(bar);

        var swing = Assert.Single(result!.ActiveSwings);
        Assert.Equal(5080, swing.Price);
        Assert.True(swing.IsHigh);
    }

    private static Candle Bar(int minuteOffset, double open, double high, double low, double close) =>
        new(TestTime.Et(2026, 1, 13, 12, 0) + minuteOffset * 60, open, high, low, close, 1000);
}
