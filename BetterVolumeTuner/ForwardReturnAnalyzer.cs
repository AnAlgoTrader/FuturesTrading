namespace BetterVolumeTuner;

/// <summary>
/// Computes forward return statistics for each signal type.
/// For every bar classified with a signal, records the N-bar forward return
/// and aggregates mean, std, and information ratio.
/// </summary>
public class ForwardReturnAnalyzer
{
    private readonly int[] _horizons;

    /// <summary>Signal -> horizon -> list of forward returns.</summary>
    private readonly Dictionary<VolumeSignalKey, Dictionary<int, List<double>>> _returns = new();

    public ForwardReturnAnalyzer(params int[] horizons)
    {
        _horizons = horizons.Length > 0 ? horizons : new[] { 5, 10, 20, 60 };
    }

    public void Record(DayTrader.Helper.VolumeSignal signal, int barIndex, List<DayTrader.Helper.Bar> bars)
    {
        if (signal == DayTrader.Helper.VolumeSignal.Normal) return;

        var key = new VolumeSignalKey(signal);
        if (!_returns.ContainsKey(key))
            _returns[key] = new Dictionary<int, List<double>>();
        var signalReturns = _returns[key];

        foreach (var h in _horizons)
        {
            int targetIndex = barIndex + h;
            if (targetIndex >= bars.Count) continue;

            double entryPrice = bars[barIndex].Close;
            double exitPrice = bars[targetIndex].Close;
            if (entryPrice == 0) continue;

            double ret = (exitPrice - entryPrice) / entryPrice;
            if (!signalReturns.ContainsKey(h))
                signalReturns[h] = new List<double>();
            signalReturns[h].Add(ret);
        }
    }

    public Dictionary<DayTrader.Helper.VolumeSignal, SignalStats> GetStats()
    {
        var result = new Dictionary<DayTrader.Helper.VolumeSignal, SignalStats>();

        foreach (var (key, horizons) in _returns)
        {
            var stats = new SignalStats { Signal = key.Signal };
            foreach (var h in _horizons)
            {
                if (!horizons.ContainsKey(h)) continue;
                var rets = horizons[h];
                if (rets.Count == 0) continue;

                double mean = rets.Average();
                double variance = rets.Sum(r => (r - mean) * (r - mean)) / rets.Count;
                double std = Math.Sqrt(variance);
                double infoRatio = std > 0 ? mean / std : 0;

                stats.HorizonResults[h] = new HorizonResult
                {
                    Count = rets.Count,
                    MeanReturn = mean,
                    StdDev = std,
                    InformationRatio = infoRatio,
                    PositiveRate = rets.Count(r => r > 0) / (double)rets.Count
                };
            }
            result[key.Signal] = stats;
        }

        return result;
    }

    /// <summary>
    /// Composite score: weighted sum of absolute information ratios across horizons.
    /// Higher = more predictive signal.
    ///
    /// Per-signal weights control how much each signal type contributes to the
    /// overall score, independent of how often it fires. This prevents a
    /// frequency-dominated, low-value signal like LowVolume from overwhelming
    /// rare-but-informative signals like Absorption, and lets the sweep favor
    /// parameter sets that make the genuinely predictive signals actionable.
    ///
    /// Signals with fewer than minSamples observations are heavily discounted,
    /// since an IR computed on a handful of bars is statistically meaningless.
    /// </summary>
    public double GetCompositeScore(
        Dictionary<DayTrader.Helper.VolumeSignal, double>? signalWeights = null,
        int minSamples = 50)
    {
        // Default weights: emphasize high-willpower microstructure signals over
        // the frequent but weak LowVolume marker.
        signalWeights ??= new Dictionary<DayTrader.Helper.VolumeSignal, double>
        {
            [DayTrader.Helper.VolumeSignal.ClimaxUp] = 1.2,
            [DayTrader.Helper.VolumeSignal.ClimaxDown] = 1.2,
            [DayTrader.Helper.VolumeSignal.Churn] = 1.0,
            [DayTrader.Helper.VolumeSignal.LowVolume] = 0.25,
            [DayTrader.Helper.VolumeSignal.Absorption] = 1.5,
            [DayTrader.Helper.VolumeSignal.StackedImbalance] = 1.5,
        };

        double score = 0;
        foreach (var (key, horizons) in _returns)
        {
            double sigWeight = signalWeights.GetValueOrDefault(key.Signal, 0.5);

            foreach (var (h, rets) in horizons)
            {
                if (rets.Count < Math.Min(minSamples, 10)) continue;
                double mean = rets.Average();
                double variance = rets.Sum(r => (r - mean) * (r - mean)) / rets.Count;
                double std = Math.Sqrt(variance);
                double ir = std > 0 ? Math.Abs(mean) / std : 0;
                // Weight shorter horizons more
                double weight = 1.0 / Math.Sqrt(h);
                // Discount signals with too few samples to be reliable
                double sampleDiscount = rets.Count < minSamples ? (double)rets.Count / minSamples : 1.0;
                score += ir * weight * sigWeight * sampleDiscount;
            }
        }
        return score;
    }

    public void Merge(ForwardReturnAnalyzer other)
    {
        foreach (var (key, horizons) in other._returns)
        {
            if (!_returns.ContainsKey(key))
                _returns[key] = new Dictionary<int, List<double>>();
            foreach (var (h, rets) in horizons)
            {
                if (!_returns[key].ContainsKey(h))
                    _returns[key][h] = new List<double>();
                _returns[key][h].AddRange(rets);
            }
        }
    }
}

public class VolumeSignalKey : IEquatable<VolumeSignalKey>
{
    public DayTrader.Helper.VolumeSignal Signal { get; }

    public VolumeSignalKey(DayTrader.Helper.VolumeSignal signal) => Signal = signal;

    public bool Equals(VolumeSignalKey? other) => other != null && Signal == other.Signal;
    public override bool Equals(object? obj) => Equals(obj as VolumeSignalKey);
    public override int GetHashCode() => Signal.GetHashCode();
}

public class SignalStats
{
    public DayTrader.Helper.VolumeSignal Signal { get; set; }
    public Dictionary<int, HorizonResult> HorizonResults { get; set; } = new();
}

public class HorizonResult
{
    public int Count { get; set; }
    public double MeanReturn { get; set; }
    public double StdDev { get; set; }
    public double InformationRatio { get; set; }
    public double PositiveRate { get; set; }
}
