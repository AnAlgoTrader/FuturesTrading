using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using Xunit;

namespace IndicatorBuild.Tests;

public class WyckoffEngineTests
{
    private static Candle CandleAt(int i, double price, double volume = 100) =>
        new(i * 10L, price - 1, price + 1, price - 1, price, volume);

    private static SwingPoint Low(int i, double price) => new(i * 10L, price, false, i * 10);
    private static SwingPoint High(int i, double price) => new(i * 10L, price, true, i * 10);

    private static BarContext Ctx(Candle c, params SwingPoint[] swings) =>
        new(c, atr: 1, avgVolume: 100, newSwings: swings);

    private static void FeedPriorDowntrend(WyckoffEngine e)
    {
        // descending closes clear enough for prior-trend detection (drift <= -0.5)
        e.OnBar(Ctx(CandleAt(1, 120), Low(1, 120)));
        e.OnBar(Ctx(CandleAt(2, 118), High(2, 118)));
        e.OnBar(Ctx(CandleAt(3, 112), Low(3, 112)));
        e.OnBar(Ctx(CandleAt(4, 110), High(4, 110)));
        e.OnBar(Ctx(CandleAt(5, 105), Low(5, 105)));
        e.OnBar(Ctx(CandleAt(6, 103), High(6, 103)));
    }

    private static void FeedPriorUptrend(WyckoffEngine e)
    {
        e.OnBar(Ctx(CandleAt(1, 100), High(1, 100)));
        e.OnBar(Ctx(CandleAt(2, 103), Low(2, 103)));
        e.OnBar(Ctx(CandleAt(3, 108), High(3, 108)));
        e.OnBar(Ctx(CandleAt(4, 106), Low(4, 106)));
        e.OnBar(Ctx(CandleAt(5, 112), High(5, 112)));
        e.OnBar(Ctx(CandleAt(6, 110), Low(6, 110)));
    }

    [Fact]
    public void Accumulation_EmitsRecipeInOrder_ThenZoneOnPhaseE()
    {
        var e = new WyckoffEngine();
        FeedPriorDowntrend(e);

        e.OnBar(Ctx(CandleAt(7, 100), Low(7, 100)));   // SC
        e.OnBar(Ctx(CandleAt(8, 104), High(8, 104)));  // AR
        e.OnBar(Ctx(CandleAt(9, 101), Low(9, 101)));   // ST (holds above SC low 100)
        e.OnBar(Ctx(CandleAt(10, 106), High(10, 106))); // band top 106
        e.OnBar(Ctx(CandleAt(11, 107, 150), High(11, 107))); // SOS: close 107 > 106, vol 150 >= 1.2x100
        e.OnBar(Ctx(CandleAt(12, 107.5), Low(12, 107.5)));   // LPS: pullback holds above 106
        e.OnBar(Ctx(CandleAt(13, 110), High(13, 110)));      // PhaseE: close 110 > 106

        Assert.Single(e.Zones);
        Assert.Equal(ZoneKind.AccumulationRange, e.Zones[0].Kind);
        Assert.Equal(100, e.Zones[0].Bottom);
        Assert.Equal(107, e.Zones[0].Top);

        var types = e.Events.Select(v => v.Type).ToList();
        Assert.Contains(Concept.SellingClimax, types);
        Assert.Contains(Concept.AutomaticRally, types);
        Assert.Contains(Concept.SecondaryTest, types);
        Assert.Contains(Concept.SignOfStrength, types);
        Assert.Contains(Concept.LastPointOfSupport, types);
        Assert.Contains(Concept.AccumulationBreakout, types);
        Assert.True(types.IndexOf(Concept.SellingClimax) < types.IndexOf(Concept.AutomaticRally));
        Assert.True(types.IndexOf(Concept.AutomaticRally) < types.IndexOf(Concept.SecondaryTest));
        Assert.True(types.IndexOf(Concept.SecondaryTest) < types.IndexOf(Concept.SignOfStrength));
        Assert.True(types.IndexOf(Concept.SignOfStrength) < types.IndexOf(Concept.LastPointOfSupport));
        Assert.True(types.IndexOf(Concept.LastPointOfSupport) < types.IndexOf(Concept.AccumulationBreakout));
    }

    [Fact]
    public void Accumulation_SpringOnPierceBelowSCLow_ThenRecovery()
    {
        var e = new WyckoffEngine();
        FeedPriorDowntrend(e);

        e.OnBar(Ctx(CandleAt(7, 100), Low(7, 100)));    // SC extreme 100
        e.OnBar(Ctx(CandleAt(8, 104), High(8, 104)));   // AR
        e.OnBar(Ctx(CandleAt(9, 99.5), Low(9, 99.5)));  // pierce below 100 -> spring pending
        e.OnBar(Ctx(CandleAt(10, 103), High(10, 103))); // recovery swing high above 100 -> Spring at t9
        e.OnBar(Ctx(CandleAt(11, 105, 150), High(11, 105))); // SOS: close 105 > band top 104
        e.OnBar(Ctx(CandleAt(12, 105.5), Low(12, 105.5)));   // LPS
        e.OnBar(Ctx(CandleAt(13, 109), High(13, 109)));      // PhaseE

        var spring = Assert.Single(e.Events.Where(v => v.Type == Concept.Spring));
        Assert.Equal(9 * 10L, spring.Time);
        Assert.DoesNotContain(e.Events, v => v.Type == Concept.SecondaryTest);
        Assert.Single(e.Zones);
    }

    [Fact]
    public void Distribution_EmitsMirrorRecipe()
    {
        var e = new WyckoffEngine();
        FeedPriorUptrend(e);

        e.OnBar(Ctx(CandleAt(7, 120), High(7, 120)));    // BC extreme 120
        e.OnBar(Ctx(CandleAt(8, 116), Low(8, 116)));     // AR reaction
        e.OnBar(Ctx(CandleAt(9, 118), High(9, 118)));    // ST (holds below BC high)
        e.OnBar(Ctx(CandleAt(10, 114, 150), Low(10, 114))); // SOW: close 114 < band low 116, vol filter
        e.OnBar(Ctx(CandleAt(11, 115), High(11, 115)));     // LPSY: bounce holds below 116
        e.OnBar(Ctx(CandleAt(12, 112), Low(12, 112)));      // PhaseE: close 112 < 116

        Assert.Single(e.Zones);
        Assert.Equal(ZoneKind.DistributionRange, e.Zones[0].Kind);
        Assert.Equal(120, e.Zones[0].Top);

        var types = e.Events.Select(v => v.Type).ToList();
        Assert.Contains(Concept.BuyingClimax, types);
        Assert.Contains(Concept.AutomaticRally, types);
        Assert.Contains(Concept.SecondaryTest, types);
        Assert.Contains(Concept.SignOfWeakness, types);
        Assert.Contains(Concept.LastPointOfSupply, types);
        Assert.Contains(Concept.DistributionBreakout, types);
    }

    [Fact]
    public void Distribution_UtadOnPierceAboveBCHigh_ThenRecovery()
    {
        var e = new WyckoffEngine();
        FeedPriorUptrend(e);

        e.OnBar(Ctx(CandleAt(7, 120), High(7, 120)));    // BC extreme 120
        e.OnBar(Ctx(CandleAt(8, 116), Low(8, 116)));     // AR
        e.OnBar(Ctx(CandleAt(9, 121), High(9, 121)));    // pierce above 120 -> UTAD pending
        e.OnBar(Ctx(CandleAt(10, 117), Low(10, 117)));   // recovery swing low below 120 -> UTAD at t9
        e.OnBar(Ctx(CandleAt(11, 115, 150), Low(11, 115))); // SOW
        e.OnBar(Ctx(CandleAt(12, 115.5), High(12, 115.5))); // LPSY
        e.OnBar(Ctx(CandleAt(13, 111), Low(13, 111)));       // PhaseE

        var utad = Assert.Single(e.Events.Where(v => v.Type == Concept.UpthrustAfterDistribution));
        Assert.Equal(9 * 10L, utad.Time);
        Assert.DoesNotContain(e.Events, v => v.Type == Concept.SecondaryTest);
        Assert.Single(e.Zones);
    }

    [Fact]
    public void QuickNonQualifyingBand_NoZoneNoEvents()
    {
        var e = new WyckoffEngine(minSwings: 5, minBars: 40);
        FeedPriorDowntrend(e);

        e.OnBar(Ctx(CandleAt(7, 100), Low(7, 100)));
        e.OnBar(Ctx(CandleAt(8, 104), High(8, 104)));
        e.OnBar(Ctx(CandleAt(9, 101), Low(9, 101)));
        e.OnBar(Ctx(CandleAt(10, 107), High(10, 107)));   // overshoot swing
        e.OnBar(Ctx(CandleAt(11, 108), High(11, 108)));   // second overshoot -> force reset

        Assert.Empty(e.Zones);
        Assert.DoesNotContain(e.Events, v => v.Type == Concept.AccumulationBreakout);
    }

    [Fact]
    public void FlatPrior_Untyped_NoZone()
    {
        var e = new WyckoffEngine();
        // prior oscillates inside 0.4 pts -> drift < 0.5 -> untyped
        e.OnBar(Ctx(CandleAt(1, 100), Low(1, 100)));
        e.OnBar(Ctx(CandleAt(2, 100.2), High(2, 100.2)));
        e.OnBar(Ctx(CandleAt(3, 99.9), Low(3, 99.9)));
        e.OnBar(Ctx(CandleAt(4, 100.3), High(4, 100.3)));
        e.OnBar(Ctx(CandleAt(5, 100), Low(5, 100)));
        e.OnBar(Ctx(CandleAt(6, 100.4), High(6, 100.4)));

        e.OnBar(Ctx(CandleAt(7, 99.5), Low(7, 99.5)));
        e.OnBar(Ctx(CandleAt(8, 100.6), High(8, 100.6)));
        e.OnBar(Ctx(CandleAt(9, 99.8), Low(9, 99.8)));
        e.OnBar(Ctx(CandleAt(10, 101), High(10, 101)));
        e.OnBar(Ctx(CandleAt(11, 100.2), Low(11, 100.2)));
        e.OnBar(Ctx(CandleAt(12, 101.5), High(12, 101.5)));
        e.OnBar(Ctx(CandleAt(13, 103), High(13, 103)));
        e.OnBar(Ctx(CandleAt(14, 104), High(14, 104)));

        Assert.Empty(e.Zones);
    }
}