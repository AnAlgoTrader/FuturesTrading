using DayTrader.Helper;

namespace BetterVolumeTuner;

/// <summary>
/// Formats and prints sweep results to the console.
/// </summary>
public static class ConsoleReporter
{
    public static void PrintTopResults(List<SweepResult> results, int topN = 20)
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("  BETTER VOLUME PARAMETER SWEEP — TOP RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var header = $"{"Rank",-5} {"Score",8} {"Unwtd",8} {"LB",4} {"Churn",6} {"AbsDR",6} {"AbsSM",6} {"StkN",5} {"StkDR",6} {"SigRate",8} {"#Signals",8}";
        Console.WriteLine(header);
        Console.WriteLine(new string('─', header.Length));

        for (int i = 0; i < Math.Min(topN, results.Count); i++)
        {
            var r = results[i];
            Console.WriteLine(
                $"{i + 1,-5} {r.CompositeScore,8:F4} {r.UnweightedScore,8:F4} {r.LookBack,4} {r.ChurnSpreadMultiplier,6:F1} " +
                $"{r.AbsorptionDeltaRatioThreshold,6:F1} {r.AbsorptionSpreadMaxMultiplier,6:F1} " +
                $"{r.StackedImbalanceCount,5} {r.StackedImbalanceDeltaRatioThreshold,6:F1} " +
                $"{r.SignalRate,8:P1} {r.TotalSignals,8}");
        }

        Console.WriteLine();
    }

    public static void PrintSignalBreakdown(SweepResult result)
    {
        Console.WriteLine($"── Signal Breakdown for Rank 1 (Score: {result.CompositeScore:F4}) ──");
        Console.WriteLine($"   LookBack={result.LookBack}, ChurnSpread={result.ChurnSpreadMultiplier}, " +
                         $"AbsDeltaRatio={result.AbsorptionDeltaRatioThreshold}, AbsSpreadMult={result.AbsorptionSpreadMaxMultiplier}, " +
                         $"StackCount={result.StackedImbalanceCount}, StackDeltaRatio={result.StackedImbalanceDeltaRatioThreshold}");
        Console.WriteLine();

        var sigHeader = $"{"Signal",-18} {"Count",8} {"Rate",8} {"Horizon",8} {"MeanRet",10} {"StdDev",10} {"InfoRatio",10} {"WinRate",8}";
        Console.WriteLine(sigHeader);
        Console.WriteLine(new string('─', sigHeader.Length));

        foreach (var signal in Enum.GetValues<VolumeSignal>())
        {
            if (!result.SignalCounts.TryGetValue(signal, out var count)) continue;
            if (signal == VolumeSignal.Normal && count == result.TotalBars) continue;

            if (!result.SignalStats.TryGetValue(signal, out var stats)) continue;

            bool first = true;
            foreach (var (horizon, hr) in stats.HorizonResults.OrderBy(kvp => kvp.Key))
            {
                string sigName = first ? signal.GetName() : "";
                string cnt = first ? $"{count,8}" : "";
                string rate = first ? $"{(double)count / result.TotalBars,8:P1}" : "";
                Console.WriteLine(
                    $"{sigName,-18} {cnt} {rate} " +
                    $"+{horizon,6}bar {hr.MeanReturn,10:P4} {hr.StdDev,10:P4} " +
                    $"{hr.InformationRatio,10:F4} {hr.PositiveRate,8:P1}");
                first = false;
            }
        }
        Console.WriteLine();
    }

    public static void PrintSignalDistribution(List<SweepResult> topResults)
    {
        Console.WriteLine("── Signal Frequency Across Top 10 Parameter Sets ──");
        Console.WriteLine();

        var signals = Enum.GetValues<VolumeSignal>().Where(s => s != VolumeSignal.Normal).ToList();
        var header = $"{"Signal",-18}";
        for (int i = 0; i < Math.Min(10, topResults.Count); i++)
            header += $" {"#" + (i + 1),8}";
        Console.WriteLine(header);
        Console.WriteLine(new string('─', header.Length));

        foreach (var signal in signals)
        {
            string line = $"{signal.GetName(),-18}";
            for (int i = 0; i < Math.Min(10, topResults.Count); i++)
            {
                topResults[i].SignalCounts.TryGetValue(signal, out var count);
                line += $" {count,8}";
            }
            Console.WriteLine(line);
        }
        Console.WriteLine();
    }

    public static void PrintParameterSensitivity(List<SweepResult> results)
    {
        Console.WriteLine("── Parameter Sensitivity Analysis ──");
        Console.WriteLine();

        // Group by each parameter and show avg score
        var params_ = new (string Name, Func<SweepResult, object> Selector)[]
        {
            ("LookBack", r => r.LookBack),
            ("ChurnSpread", r => r.ChurnSpreadMultiplier),
            ("AbsDeltaRatio", r => r.AbsorptionDeltaRatioThreshold),
            ("AbsSpreadMult", r => r.AbsorptionSpreadMaxMultiplier),
            ("StackCount", r => r.StackedImbalanceCount),
            ("StackDeltaRatio", r => r.StackedImbalanceDeltaRatioThreshold),
        };

        foreach (var (name, selector) in params_)
        {
            Console.WriteLine($"  {name}:");
            var grouped = results
                .GroupBy(r => selector(r)!)
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in grouped)
            {
                double avgScore = group.Average(r => r.CompositeScore);
                double maxScore = group.Max(r => r.CompositeScore);
                Console.WriteLine($"    {group.Key,6} → avg={avgScore:F4}  max={maxScore:F4}  (n={group.Count()})");
            }
            Console.WriteLine();
        }
    }

    public static void PrintChurnReversal(ChurnReversalReport report)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("  CHURN REVERSAL HYPOTHESIS TEST — Churn after trend + small body");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine($"  Total Churn signals:      {report.TotalChurn}");
        Console.WriteLine($"  Qualifying (trend+small): {report.Qualifying}");
        Console.WriteLine($"  Qualification rate:       {(report.TotalChurn > 0 ? (double)report.Qualifying / report.TotalChurn : 0):P1}");
        Console.WriteLine();
        Console.WriteLine($"  Signed forward return = move AGAINST the prior trend (positive = reversal):");
        Console.WriteLine();

        var header = $"  {"Horizon",-10} {"N",-8} {"SignedMean",12} {"SignedStd",12} {"InfoRatio",12} {"ReverseWinRate",12}";
        Console.WriteLine(header);
        Console.WriteLine("  " + new string('─', header.Length - 2));

        foreach (var h in report.Horizons.OrderBy(x => x))
        {
            var signed = report.SignedForward[h];
            if (signed.Count == 0) continue;
            double mean = signed.Average();
            double variance = signed.Sum(r => (r - mean) * (r - mean)) / signed.Count;
            double std = Math.Sqrt(variance);
            double ir = std > 0 ? mean / std : 0;
            double revWinRate = signed.Count(r => r > 0) / (double)signed.Count;

            Console.WriteLine($"  {($"+" + h + "bar"),-10} {signed.Count,-8} {mean,12:P4} {std,12:P4} {ir,12:F4} {revWinRate,12:P1}");
        }

        Console.WriteLine();
        Console.WriteLine("  Interpretation: reverse win rate >> 50% with a positive signed mean and");
        Console.WriteLine("  meaningful IR (|IR| > 0.2 is notable for 1-min bars) supports the hypothesis.");
        Console.WriteLine();
    }

    public static void PrintChurnReversalScan(List<ScanRow> rows)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("  CHURN REVERSAL FILTER SCAN — every (trend, small-body) combination");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("  Sorted by combined positive info ratio across 10/20/60-bar horizons.");
        Console.WriteLine("  Positive signed mean = reversal. Negative = trend continuation.");
        Console.WriteLine();

        var header = $"  {"TrendLB",-8} {"MinSpread",-10} {"MaxBody",-8} {"N",-6} {"10-IR",-8} {"10Win",-8} {"20-IR",-8} {"20Win",-8} {"60-IR",-8} {"60Win",-8}";
        Console.WriteLine(header);
        Console.WriteLine("  " + new string('─', header.Length - 2));

        foreach (var r in rows.Take(40))
        {
            string ir10 = r.Signed10.N == 0 ? "  --" : r.Signed10.InfoRatio.ToString("F3");
            string w10 = r.Signed10.N == 0 ? " --" : r.Signed10.WinRate.ToString("P1");
            string ir20 = r.Signed20.N == 0 ? "  --" : r.Signed20.InfoRatio.ToString("F3");
            string w20 = r.Signed20.N == 0 ? " --" : r.Signed20.WinRate.ToString("P1");
            string ir60 = r.Signed60.N == 0 ? "  --" : r.Signed60.InfoRatio.ToString("F3");
            string w60 = r.Signed60.N == 0 ? " --" : r.Signed60.WinRate.ToString("P1");

            Console.WriteLine(
                $"  {r.TrendLookback,-8} {r.MinTrendSpread,-10} {r.MaxBodyRatio,-8:F2} {r.N,-6} " +
                $"{ir10,-8} {w10,-8} {ir20,-8} {w20,-8} {ir60,-8} {w60,-8}");
        }
        Console.WriteLine();
        Console.WriteLine("  Negative IR at all horizons across all filters would falsify the hypothesis.");
        Console.WriteLine();

        Console.WriteLine("── RTH vs Overnight breakdown (signed return = reversal, 10 & 60 bar horizons) ──");
        Console.WriteLine();
        var h2 = $"  {"TrendLB",-8} {"MinSpread",-10} {"MaxBody",-8} │ {"RTHn",-6} {"RTH10-IR",-9} {"RTH10Win",-9} {"RTH60-IR",-9} {"RTH60Win",-9} │ {"OvnN",-6} {"Ovn10-IR",-9} {"Ovn10Win",-9} {"Ovn60-IR",-9} {"Ovn60Win",-9}";
        Console.WriteLine(h2);
        Console.WriteLine("  " + new string('─', h2.Length - 2));

        foreach (var r in rows.Take(20))
        {
            Console.WriteLine(
                $"  {r.TrendLookback,-8} {r.MinTrendSpread,-10} {r.MaxBodyRatio,-8:F2} │ " +
                $"{r.RthN,-6} {(r.Rth10.N == 0 ? "   --" : r.Rth10.InfoRatio.ToString("F3")),-9} " +
                $"{(r.Rth10.N == 0 ? "  --" : r.Rth10.WinRate.ToString("P1")),-9} " +
                $"{(r.Rth60.N == 0 ? "   --" : r.Rth60.InfoRatio.ToString("F3")),-9} " +
                $"{(r.Rth60.N == 0 ? "  --" : r.Rth60.WinRate.ToString("P1")),-9} │ " +
                $"{r.OvernightN,-6} {(r.Overnight10.N == 0 ? "   --" : r.Overnight10.InfoRatio.ToString("F3")),-9} " +
                $"{(r.Overnight10.N == 0 ? "  --" : r.Overnight10.WinRate.ToString("P1")),-9} " +
                $"{(r.Overnight60.N == 0 ? "   --" : r.Overnight60.InfoRatio.ToString("F3")),-9} " +
                $"{(r.Overnight60.N == 0 ? "  --" : r.Overnight60.WinRate.ToString("P1")),-9}");
        }
        Console.WriteLine();
        Console.WriteLine("  RTH = Mon-Fri 09:30-16:00 ET. Overnight = Globex session.");
        Console.WriteLine();
    }
}
