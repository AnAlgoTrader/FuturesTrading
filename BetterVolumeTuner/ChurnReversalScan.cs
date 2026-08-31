using DayTrader.Helper;

namespace BetterVolumeTuner;

/// <summary>
/// Scans a range of Churn-reversal filter definitions to check whether ANY
/// combination of (trend strength, small-body threshold) produces a reversal
/// edge. A single fixed filter can hide a real effect; this checks robustness.
/// </summary>
public class ChurnReversalScan
{
    private readonly List<Bar> _bars;
    private readonly Logger _logger;

    public ChurnReversalScan(List<Bar> bars, Logger logger)
    {
        _bars = bars;
        _logger = logger;
    }

    public List<ScanRow> Run(int[] trendLookbacks, int[] minTrendSpreads, double[] maxBodyRatios)
    {
        var classifier = new BetterVolumeClassifier(
            _logger,
            lookBack: 30,
            churnSpreadMultiplier: 2.5,
            absorptionDeltaRatioThreshold: 0.7,
            absorptionSpreadMaxMultiplier: 0.7,
            stackedImbalanceCount: 4,
            stackedImbalanceDeltaRatioThreshold: 0.6);

        // Preclassify once: store churn indices and prior trend stats per bar
        var churnIndices = new List<int>();
        var trendDownMove = new List<double>();   // prior trend move signed
        var avgRange = new List<double>();

        var maxLookback = trendLookbacks.Max();
        for (int i = 0; i < _bars.Count; i++)
        {
            var result = classifier.Classify(_bars[i]);
            if (result.Signal != VolumeSignal.Churn)
            {
                churnIndices.Add(-1);
                trendDownMove.Add(0);
                avgRange.Add(0);
                continue;
            }

            churnIndices.Add(i);
            // prior trend move: close[i-1] - open[i-lookback]
            if (i < maxLookback)
            {
                trendDownMove.Add(0);
                avgRange.Add(0);
                continue;
            }
            double priorClose = _bars[i - 1].Close;
            double openLb = _bars[i - maxLookback].Open;
            trendDownMove.Add(priorClose - openLb);

            double sumRange = 0;
            for (int k = i - maxLookback; k < i; k++) sumRange += _bars[k].Spread;
            avgRange.Add(sumRange / maxLookback);
        }

        var rows = new List<ScanRow>();

        foreach (var lookback in trendLookbacks)
        foreach (var minSpread in minTrendSpreads)
        foreach (var bodyRatio in maxBodyRatios)
        {
            int n = 0, rthN = 0, overnightN = 0;
            var signed10 = new List<double>();
            var signed20 = new List<double>();
            var signed60 = new List<double>();
            // Session-split data for the horizons that matter (10 & 60)
            var rth10 = new List<double>(); var overnight10 = new List<double>();
            var rth60 = new List<double>(); var overnight60 = new List<double>();

            for (int idx = 0; idx < churnIndices.Count; idx++)
            {
                int i = churnIndices[idx];
                if (i < 0 || i < lookback) continue;

                var bar = _bars[i];
                // trend move using this lookback
                double priorClose = _bars[i - 1].Close;
                double openLb = _bars[i - lookback].Open;
                double move = priorClose - openLb;

                // ATR over this lookback
                double sumRange = 0;
                for (int k = i - lookback; k < i; k++) sumRange += _bars[k].Spread;
                double atr = sumRange / lookback;
                if (atr <= 0) continue;

                double spreadMult = Math.Abs(move) / atr;
                if (spreadMult < minSpread) continue;

                // small body
                double range = bar.Spread;
                if (range <= 0) continue;
                double br = Math.Abs(bar.Close - bar.Open) / range;
                if (br > bodyRatio) continue;

                bool trendUp = move > 0;
                n++;
                bool isRth = MarketSession.IsRth(bar.StartTime);

                double Signed(int h)
                {
                    int target = i + h;
                    if (target >= _bars.Count) return double.NaN;
                    double raw = (_bars[target].Close - bar.Close) / bar.Close;
                    return trendUp ? -raw : raw;
                }

                void Add(List<double> list, int h)
                {
                    double s = Signed(h);
                    if (!double.IsNaN(s)) list.Add(s);
                }
                Add(signed10, 10);
                Add(signed20, 20);
                Add(signed60, 60);

                double s10 = Signed(10), s60 = Signed(60);
                if (isRth)
                {
                    rthN++;
                    if (!double.IsNaN(s10)) rth10.Add(s10);
                    if (!double.IsNaN(s60)) rth60.Add(s60);
                }
                else
                {
                    overnightN++;
                    if (!double.IsNaN(s10)) overnight10.Add(s10);
                    if (!double.IsNaN(s60)) overnight60.Add(s60);
                }
            }

            rows.Add(new ScanRow
            {
                TrendLookback = lookback,
                MinTrendSpread = minSpread,
                MaxBodyRatio = bodyRatio,
                N = n,
                RthN = rthN,
                OvernightN = overnightN,
                Signed10 = Stats(signed10),
                Signed20 = Stats(signed20),
                Signed60 = Stats(signed60),
                Rth10 = Stats(rth10),
                Rth60 = Stats(rth60),
                Overnight10 = Stats(overnight10),
                Overnight60 = Stats(overnight60)
            });
        }

        return rows.OrderByDescending(r => r.Signed10.InfoRatio + r.Signed20.InfoRatio + r.Signed60.InfoRatio).ToList();
    }

    private static HorizonStats Stats(List<double> values)
    {
        if (values.Count == 0) return new HorizonStats { N = 0 };
        double mean = values.Average();
        double variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        double std = Math.Sqrt(variance);
        return new HorizonStats
        {
            N = values.Count,
            Mean = mean,
            Std = std,
            InfoRatio = std > 0 ? mean / std : 0,
            WinRate = values.Count(v => v > 0) / (double)values.Count
        };
    }
}

public class ScanRow
{
    public int TrendLookback { get; set; }
    public int MinTrendSpread { get; set; }
    public double MaxBodyRatio { get; set; }
    public int N { get; set; }
    public int RthN { get; set; }
    public int OvernightN { get; set; }
    public HorizonStats Signed10 { get; set; } = new();
    public HorizonStats Signed20 { get; set; } = new();
    public HorizonStats Signed60 { get; set; } = new();
    public HorizonStats Rth10 { get; set; } = new();
    public HorizonStats Rth60 { get; set; } = new();
    public HorizonStats Overnight10 { get; set; } = new();
    public HorizonStats Overnight60 { get; set; } = new();
}

public class HorizonStats
{
    public int N { get; set; }
    public double Mean { get; set; }
    public double Std { get; set; }
    public double InfoRatio { get; set; }
    public double WinRate { get; set; }
}
