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

        return m;
    }

    private static double Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}