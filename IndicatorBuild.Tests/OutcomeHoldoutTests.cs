using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Analytics;
using Xunit;

namespace IndicatorBuild.Tests;

public class OutcomeHoldoutTests
{
    private static OutcomeRow Row(string label, string detail, string direction, int count, double win) =>
        new() { Label = label, Detail = detail, Direction = direction, Count = count, WinRate = win };

    [Fact]
    public void Build_MergesSlicesIntoOneRowPerSignal()
    {
        var inSample = new List<OutcomeRow> { Row("A", "", "Bullish", 44, 88) };
        var holdout = new List<OutcomeRow> { Row("A", "", "Bullish", 9, 78) };
        var holdoutGated = new List<OutcomeRow> { Row("A", "", "Bullish", 6, 83) };

        var rows = OutcomeHoldout.Build(inSample, holdout, holdoutGated);

        var row = Assert.Single(rows);
        Assert.Equal("A", row.Label);
        Assert.Equal(44, row.InCount);
        Assert.Equal(88, row.InWin);
        Assert.Equal(9, row.HoldoutCount);
        Assert.Equal(78, row.HoldoutWin);
        Assert.Equal(6, row.GatedCount);
        Assert.Equal(83, row.GatedWin);
    }

    [Fact]
    public void Build_RowOnlyInHoldout_HasNoInSampleSlice()
    {
        var holdout = new List<OutcomeRow> { Row("B", "x", "Bearish", 3, 33) };

        var rows = OutcomeHoldout.Build(new List<OutcomeRow>(), holdout, new List<OutcomeRow>());

        var row = Assert.Single(rows);
        Assert.Equal(0, row.InCount);
        Assert.Null(row.InWin);
        Assert.Equal(3, row.HoldoutCount);
        Assert.Equal(33, row.HoldoutWin);
    }
}