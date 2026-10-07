using System;
using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class SharedCalculatorsTests
{
    private static Candle CandleAt(double price, double volume = 1) =>
        new(0, price - 1, price + 1, price - 2, price, volume);

    [Fact]
    public void Ema_SeedsWithFirstValue()
    {
        var ema = new EmaCalculator(period: 3);

        ema.Next(10);
        Assert.Equal(10, ema.Value);
        ema.Next(10);
        Assert.Equal(10, ema.Value);
        // constant input yields constant output
        for (var i = 0; i < 20; i++)
            Assert.Equal(10, ema.Next(10));
    }

    [Fact]
    public void Sma_NoValueUntilWindowIsFull()
    {
        var sma = new SmaCalculator(period: 3);

        Assert.Null(sma.Next(1));
        Assert.Null(sma.Next(2));
        Assert.Equal(2, sma.Next(3)); // (1+2+3)/3
        Assert.Equal(3, sma.Next(4)); // (2+3+4)/3
    }

    [Fact]
    public void Atr_ConvergesToBarRangeOnConstantRanges()
    {
        // Constant candles: high-low = 3 (price+1 .. price-2), no gap vs prev close => TR = 3 always.
        var atr = new AtrCalculator(period: 3);

        double? last = null;
        for (var i = 0; i < 20; i++)
            last = atr.Next(CandleAt(100, 1));

        Assert.NotNull(last);
        Assert.Equal(3, last.Value, 6);
    }

    [Fact]
    public void Rsi_TrendingDownIsLow_TrendingUpIsHigh()
    {
        var rsi = new RsiCalculator(period: 5);

        for (var i = 0; i < 30; i++)
            rsi.Next(CandleAt(100 - i)); // steady decline
        var downRsi = rsi.Value;
        Assert.NotNull(downRsi);
        Assert.True(downRsi < 20, $"expected low RSI on downtrend, got {downRsi}");

        var upRsi = new RsiCalculator(period: 5);
        for (var i = 0; i < 30; i++)
            upRsi.Next(CandleAt(100 + i));
        Assert.NotNull(upRsi.Value);
        Assert.True(upRsi.Value > 80, $"expected high RSI on uptrend, got {upRsi.Value}");
    }

    [Fact]
    public void Rsi_ReflectsOnlyRecentMove()
    {
        var rsi = new RsiCalculator(period: 3);

        // Whipsaw: one bar up, one bar down => RSI near 50 after warmup.
        var sum = 0.0;
        var count = 0;
        for (var i = 0; i < 20; i++)
        {
            var v = rsi.Next(CandleAt(i % 2 == 0 ? 100 : 104));
            if (v.HasValue)
            {
                sum += v.Value;
                count++;
            }
        }

        Assert.True(count > 0);
        Assert.InRange(sum / count, 35, 65);
    }
}