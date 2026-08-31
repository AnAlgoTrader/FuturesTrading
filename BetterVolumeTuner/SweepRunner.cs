using DayTrader.Helper;

namespace BetterVolumeTuner;

/// <summary>
/// Runs a full grid search across BetterVolumeClassifier parameters,
/// scoring each parameter set by the predictive power of its signals.
/// </summary>
public class SweepRunner
{
    private readonly List<Bar> _bars;

    public SweepRunner(List<Bar> bars)
    {
        _bars = bars;
    }

    public List<SweepResult> Run()
    {
        var lookBacks = new[] { 10, 15, 20, 30, 50 };
        var churnSpreads = new[] { 1.0, 1.5, 2.0, 2.5 };
        var absDeltaRatios = new[] { 0.4, 0.5, 0.6, 0.7 };
        var absSpreadMults = new[] { 0.3, 0.5, 0.7 };
        var stackCounts = new[] { 3, 4, 5 };
        var stackDeltaRatios = new[] { 0.3, 0.4, 0.5, 0.6 };

        int total = lookBacks.Length * churnSpreads.Length * absDeltaRatios.Length *
                    absSpreadMults.Length * stackCounts.Length * stackDeltaRatios.Length;

        var results = new List<SweepResult>(total);
        int done = 0;

        // Single silent logger shared across all runs
        using var logger = new SilentLogger();

        foreach (var lb in lookBacks)
        foreach (var cs in churnSpreads)
        foreach (var adr in absDeltaRatios)
        foreach (var asm in absSpreadMults)
        foreach (var sc in stackCounts)
        foreach (var sdr in stackDeltaRatios)
        {
            done++;
            if (done % 100 == 0)
                Console.WriteLine($"  Sweep progress: {done}/{total} ({100 * done / total}%)");

            var result = RunSingle(lb, cs, adr, asm, sc, sdr, logger);
            results.Add(result);
        }

        return results.OrderByDescending(r => r.CompositeScore).ToList();
    }

    private SweepResult RunSingle(
        int lookBack,
        double churnSpreadMult,
        double absDeltaRatio,
        double absSpreadMult,
        int stackCount,
        double stackDeltaRatio,
        Logger logger)
    {
        var classifier = new BetterVolumeClassifier(
            logger,
            lookBack: lookBack,
            churnSpreadMultiplier: churnSpreadMult,
            absorptionDeltaRatioThreshold: absDeltaRatio,
            absorptionSpreadMaxMultiplier: absSpreadMult,
            stackedImbalanceCount: stackCount,
            stackedImbalanceDeltaRatioThreshold: stackDeltaRatio,
            minSignalPersistence: 1);

        var analyzer = new ForwardReturnAnalyzer(5, 10, 20, 60);
        var signalCounts = new Dictionary<VolumeSignal, int>();

        for (int i = 0; i < _bars.Count; i++)
        {
            var result = classifier.Classify(_bars[i]);
            var signal = result.Signal;

            if (!signalCounts.ContainsKey(signal))
                signalCounts[signal] = 0;
            signalCounts[signal]++;

            analyzer.Record(signal, i, _bars);
        }

        var stats = analyzer.GetStats();
        double compositeScore = analyzer.GetCompositeScore();

        int totalSignals = signalCounts.Where(kvp => kvp.Key != VolumeSignal.Normal).Sum(kvp => kvp.Value);
        double signalRate = _bars.Count > 0 ? (double)totalSignals / _bars.Count : 0;

        return new SweepResult
        {
            LookBack = lookBack,
            ChurnSpreadMultiplier = churnSpreadMult,
            AbsorptionDeltaRatioThreshold = absDeltaRatio,
            AbsorptionSpreadMaxMultiplier = absSpreadMult,
            StackedImbalanceCount = stackCount,
            StackedImbalanceDeltaRatioThreshold = stackDeltaRatio,
            CompositeScore = compositeScore,
            SignalRate = signalRate,
            TotalBars = _bars.Count,
            TotalSignals = totalSignals,
            SignalCounts = signalCounts,
            SignalStats = stats
        };
    }
}

public class SweepResult
{
    public int LookBack { get; set; }
    public double ChurnSpreadMultiplier { get; set; }
    public double AbsorptionDeltaRatioThreshold { get; set; }
    public double AbsorptionSpreadMaxMultiplier { get; set; }
    public int StackedImbalanceCount { get; set; }
    public double StackedImbalanceDeltaRatioThreshold { get; set; }
    public double CompositeScore { get; set; }
    public double SignalRate { get; set; }
    public int TotalBars { get; set; }
    public int TotalSignals { get; set; }
    public Dictionary<VolumeSignal, int> SignalCounts { get; set; } = new();
    public Dictionary<VolumeSignal, SignalStats> SignalStats { get; set; } = new();
}
