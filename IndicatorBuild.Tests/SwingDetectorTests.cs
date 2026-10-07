using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class SwingDetectorTests
{
    private static Candle CandleAt(int time, double price) =>
        new(time, price - 0.5, price + 0.5, price - 1.0, price, 1);

    [Fact]
    public void DetectsHighsAndLowsOnSyntheticSequence()
    {
        double[] prices = { 1, 2, 3, 2, 1, 2, 3, 4, 3, 2, 1 };
        var detector = new SwingDetector(left: 2, right: 2);

        for (var i = 0; i < prices.Length; i++)
            detector.ProcessBar(CandleAt(i, prices[i]), i);

        Assert.Equal(new[] { 2, 7 }, detector.Swings.Where(s => s.IsHigh).Select(s => s.Index));
        Assert.Equal(new[] { 4 }, detector.Swings.Where(s => !s.IsHigh).Select(s => s.Index));
    }

    [Fact]
    public void PivotsRequireRightLookback_BeforeConfirmation()
    {
        var detector = new SwingDetector(left: 2, right: 2);
        double[] prices = { 1, 2, 3, 2, 1, 2, 3, 4, 3, 2, 1 };

        for (var i = 0; i < prices.Length; i++)
        {
            detector.ProcessBar(CandleAt(i, prices[i]), i);

            foreach (var swing in detector.Swings)
                Assert.True(swing.Index <= i - 2, $"pivot {swing.Index} emitted at bar {i} (repaint)");
        }

        Assert.Equal(new[] { 2, 7 }, detector.Swings.Where(s => s.IsHigh).Select(s => s.Index));
    }

    [Fact]
    public void NoSwingsBeforeWindowIsFull()
    {
        double[] prices = { 3, 2, 1 }; // less than left+right+1 = 5 bars
        var detector = new SwingDetector(left: 2, right: 2);

        for (var i = 0; i < prices.Length; i++)
            detector.ProcessBar(CandleAt(i, prices[i]), i);

        Assert.Empty(detector.Swings);
    }

    [Fact]
    public void FlatMarketProducesNoSwings()
    {
        var detector = new SwingDetector(left: 2, right: 2);

        for (var i = 0; i < 20; i++)
            detector.ProcessBar(CandleAt(i, 100), i);

        Assert.Empty(detector.Swings);
    }
}