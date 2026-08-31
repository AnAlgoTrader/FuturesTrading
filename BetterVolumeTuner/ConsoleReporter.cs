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

        var header = $"{"Rank",-5} {"Score",8} {"LB",4} {"Churn",6} {"AbsDR",6} {"AbsSM",6} {"StkN",5} {"StkDR",6} {"SigRate",8} {"#Signals",8}";
        Console.WriteLine(header);
        Console.WriteLine(new string('─', header.Length));

        for (int i = 0; i < Math.Min(topN, results.Count); i++)
        {
            var r = results[i];
            Console.WriteLine(
                $"{i + 1,-5} {r.CompositeScore,8:F4} {r.LookBack,4} {r.ChurnSpreadMultiplier,6:F1} " +
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
}
