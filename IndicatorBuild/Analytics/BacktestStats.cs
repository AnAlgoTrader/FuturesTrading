using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IndicatorBuild.Model;

namespace IndicatorBuild.Analytics;

public sealed class M2Metrics
{
    public int PivotLookback { get; set; }
    public double SweepWickFraction { get; set; }
    public double FvgWidthAtrFactor { get; set; }

    public int BosCount { get; set; }
    public int ChochCount { get; set; }
    public int[] FlipLegBuckets { get; set; } = new int[5];
    public double MedianLegBars { get; set; }
    public double? AvgLegBars { get; set; }

    public int SweepDetected { get; set; }
    public int SweepConfirmed { get; set; }
    public int SweepFaded { get; set; }
    public int SweepPending => SweepDetected - SweepConfirmed - SweepFaded;

    public int FvgZones { get; set; }
    public int FvgMitigated { get; set; }
    public double? FvgMedianMitigationBars { get; set; }

    public int ConZones { get; set; }
    public int ConUp { get; set; }
    public int ConDown { get; set; }
    public int BreakoutHeld { get; set; }
    public int BreakoutFailed { get; set; }

    public int RetracementCount { get; set; }
    public int PullbackCount { get; set; }

    public int AccumRanges { get; set; }
    public int DistRanges { get; set; }
    public int WySc { get; set; }
    public int WyBc { get; set; }
    public int WyAr { get; set; }
    public int WySt { get; set; }
    public int WySpring { get; set; }
    public int WyUtad { get; set; }
    public int WySos { get; set; }
    public int WySow { get; set; }
    public int WyLps { get; set; }
    public int WyLpsy { get; set; }
    public int WyPhaseE => WyAccumBreakout + WyDistBreakout;
    public int WyAccumBreakout { get; set; }
    public int WyDistBreakout { get; set; }

    public int MomExhBull { get; set; }
    public int MomExhBear { get; set; }
    public int MomExhTotal => MomExhBull + MomExhBear;
    public int MomDivRegular { get; set; }
    public int MomDivHidden { get; set; }
    public int MomDivTotal => MomDivRegular + MomDivHidden;
    public int MomSqueeze { get; set; }
    public int MomExpansion { get; set; }
    public int MomExpRelease { get; set; }
    public int MomExpVolatility { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Structure (pivot={PivotLookback}): BOS={BosCount}  CHoCH={ChochCount} "
            + $"flips legs: median={MedianLegBars:0.#} avg={AvgLegBars:0.#} bars.");
        sb.AppendLine($"      flip legs: <5={FlipLegBuckets[0]}  5-10={FlipLegBuckets[1]}  "
            + $"10-20={FlipLegBuckets[2]}  20-40={FlipLegBuckets[3]}  >40={FlipLegBuckets[4]}");
        var confirmRate = SweepDetected == 0 ? 0 : 100.0 * SweepConfirmed / SweepDetected;
        var fadeRate = SweepDetected == 0 ? 0 : 100.0 * SweepFaded / SweepDetected;
        sb.AppendLine($"  Sweep (wick>{SweepWickFraction:0.00}): detected={SweepDetected} "
            + $"confirmed={SweepConfirmed} ({confirmRate:0.0}%) faded={SweepFaded} ({fadeRate:0.0}%) pending={SweepPending}");
        var mitRate = FvgZones == 0 ? 0 : 100.0 * FvgMitigated / FvgZones;
        sb.AppendLine($"  FVG (width>{FvgWidthAtrFactor:0.00}xATR): zones={FvgZones} "
            + $"mitigated={FvgMitigated} ({mitRate:0.0}%) median-fill={FvgMedianMitigationBars:0.#} bars");
        var heldRate = ConUp + ConDown == 0 ? 0 : 100.0 * BreakoutHeld / (BreakoutHeld + BreakoutFailed);
        sb.AppendLine($"  Consolidation: ranges={ConZones} (up={ConUp} down={ConDown}) "
            + $"breakouts held={BreakoutHeld} ({heldRate:0.0}%) failed={BreakoutFailed}");
        sb.AppendLine($"  Pullback: pullbacks={PullbackCount} (retracement measurements={RetracementCount})");
        var wyRate = WyPhaseE == 0 ? 0 : 100.0 * WyPhaseE / (AccumRanges + DistRanges);
        sb.AppendLine($"  Wyckoff: accum={AccumRanges} dist={DistRanges} ranges  completion={WyPhaseE} ({wyRate:0.0}%)"
            + $"\n      SC={WySc} BC={WyBc}  AR={WyAr}  ST={WySt}  Spring={WySpring} UTAD={WyUtad}  "
            + $"SOS={WySos} SOW={WySow}  LPS={WyLps} LPSY={WyLpsy}  PhaseE(acc)={WyAccumBreakout} PhaseE(dist)={WyDistBreakout}");
        sb.AppendLine($"  Momentum: exhaust={MomExhTotal} (bull={MomExhBull} bear={MomExhBear})  "
            + $"divergence={MomDivTotal} (regular={MomDivRegular} hidden={MomDivHidden})  "
            + $"squeezes={MomSqueeze} expansions={MomExpansion} (release={MomExpRelease} vol={MomExpVolatility})");
        return sb.ToString();
    }
}

public static class BacktestStats
{
    public static M2Metrics Collect(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<ConceptEvent> events,
        IReadOnlyList<Zone> zones,
        int pivotLookback,
        double sweepWickFraction,
        double fvgWidthAtrFactor)
    {
        var timeToIndex = new Dictionary<long, int>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
            timeToIndex[candles[i].Time] = i;

        var m = new M2Metrics
        {
            PivotLookback = pivotLookback,
            SweepWickFraction = sweepWickFraction,
            FvgWidthAtrFactor = fvgWidthAtrFactor
        };

        var flips = new List<int>();
        foreach (var e in events)
        {
            if (e.Type == Concept.BOS) m.BosCount++;
            else if (e.Type == Concept.ChoCh && timeToIndex.TryGetValue(e.Time, out var idx)) flips.Add(idx);
        }

        var legs = new List<int>();
        for (var i = 1; i < flips.Count; i++)
            legs.Add(flips[i] - flips[i - 1]);

        foreach (var leg in legs)
        {
            if (leg < 5) m.FlipLegBuckets[0]++;
            else if (leg < 10) m.FlipLegBuckets[1]++;
            else if (leg < 20) m.FlipLegBuckets[2]++;
            else if (leg < 40) m.FlipLegBuckets[3]++;
            else m.FlipLegBuckets[4]++;
        }

        m.ChochCount = flips.Count;
        m.MedianLegBars = legs.Count == 0 ? 0 : Median(legs);
        m.AvgLegBars = legs.Count == 0 ? null : legs.Average();

        foreach (var e in events)
        {
            if (e.Type != Concept.LiquiditySweep) continue;
            if (e.Detail == "Detected") m.SweepDetected++;
            else if (e.Detail == "Confirmed") m.SweepConfirmed++;
            else if (e.Detail == "Faded") m.SweepFaded++;
        }

        var fvgs = zones.Where(z => z.Kind == ZoneKind.FairValueGap).ToList();
        m.FvgZones = fvgs.Count;
        m.FvgMitigated = fvgs.Count(z => z.State == ZoneState.Mitigated);

        var fillBars = new List<int>();
        foreach (var z in fvgs)
        {
            if (z.State != ZoneState.Mitigated || z.MitigatedTime <= 0) continue;
            if (timeToIndex.TryGetValue(z.EndTime, out var start) &&
                timeToIndex.TryGetValue(z.MitigatedTime, out var end) && end > start)
                fillBars.Add(end - start);
        }
        m.FvgMedianMitigationBars = fillBars.Count == 0 ? null : Median(fillBars);

        m.ConZones = zones.Count(z => z.Kind == ZoneKind.Consolidation);

        foreach (var e in events)
        {
            switch (e.Type)
            {
                case Concept.Breakout:
                    if (e.Detail == "Held") m.BreakoutHeld++;
                    else if (e.Detail == "Failed") m.BreakoutFailed++;
                    else if (e.Direction == "Bullish") m.ConUp++;
                    else if (e.Direction == "Bearish") m.ConDown++;
                    break;
                case Concept.Retracement:
                    m.RetracementCount++;
                    break;
                case Concept.Pullback:
                    m.PullbackCount++;
                    break;
            }
        }

        m.AccumRanges = zones.Count(z => z.Kind == ZoneKind.AccumulationRange);
        m.DistRanges = zones.Count(z => z.Kind == ZoneKind.DistributionRange);

        foreach (var e in events)
        {
            switch (e.Type)
            {
                case Concept.SellingClimax: m.WySc++; break;
                case Concept.BuyingClimax: m.WyBc++; break;
                case Concept.AutomaticRally: m.WyAr++; break;
                case Concept.SecondaryTest: m.WySt++; break;
                case Concept.Spring: m.WySpring++; break;
                case Concept.UpthrustAfterDistribution: m.WyUtad++; break;
                case Concept.SignOfStrength: m.WySos++; break;
                case Concept.SignOfWeakness: m.WySow++; break;
                case Concept.LastPointOfSupport: m.WyLps++; break;
                case Concept.LastPointOfSupply: m.WyLpsy++; break;
                case Concept.AccumulationBreakout: m.WyAccumBreakout++; break;
                case Concept.DistributionBreakout: m.WyDistBreakout++; break;
            }
        }

        foreach (var e in events)
        {
            switch (e.Type)
            {
                case Concept.Exhaustion:
                    if (e.Direction == "Bullish") m.MomExhBull++;
                    else m.MomExhBear++;
                    break;
                case Concept.Divergence:
                    if (e.Detail == "Hidden") m.MomDivHidden++;
                    else m.MomDivRegular++;
                    break;
                case Concept.Compression:
                    m.MomSqueeze++;
                    break;
                case Concept.Expansion:
                    m.MomExpansion++;
                    if (e.Detail == "release") m.MomExpRelease++;
                    else m.MomExpVolatility++;
                    break;
            }
        }

        return m;
    }

    private static double Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}