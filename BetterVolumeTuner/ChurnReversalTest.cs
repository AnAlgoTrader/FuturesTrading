using DayTrader.Helper;

namespace BetterVolumeTuner;

/// <summary>
/// Tests the hypothesis: "Churn candles appearing after an established trend,
/// with a very small candle body, produce a reversal."
///
/// A reversal is measured as forward price drift OPPOSITE to the prior trend.
/// For each qualifying Churn bar we compute signed forward returns (signed so a
/// move against the trend is positive), then aggregate mean / IR / win rate.
///
/// This is a CONDITIONAL hypothesis test — a refinement of the plain (neutral)
/// Churn signal, which we already know carries no standalone directional edge.
/// If the post-trend + small-body filter produces a strong signed-forward-return
/// edge, the hypothesis is supported by the data.
/// </summary>
public class ChurnReversalTest
{
    private readonly List<Bar> _bars;
    private readonly BetterVolumeClassifier _classifier;

    // Hypothesis knobs
    private readonly int _trendLookback;       // bars of prior trend
    private readonly int _minTrendSpread;      // min |trend move| as multiple of bar ATR
    private readonly double _maxBodyRatio;     // max |close-open|/|high-low| for "small candle"
    private readonly int[] _horizons;

    public ChurnReversalTest(
        List<Bar> bars,
        Logger logger,
        int trendLookback = 10,
        int minTrendSpread = 4,
        double maxBodyRatio = 0.25,
        params int[] horizons)
    {
        _bars = bars;
        _classifier = new BetterVolumeClassifier(
            logger,
            lookBack: 30,
            churnSpreadMultiplier: 2.5,
            absorptionDeltaRatioThreshold: 0.7,
            absorptionSpreadMaxMultiplier: 0.7,
            stackedImbalanceCount: 4,
            stackedImbalanceDeltaRatioThreshold: 0.6);
        _trendLookback = trendLookback;
        _minTrendSpread = minTrendSpread;
        _maxBodyRatio = maxBodyRatio;
        _horizons = horizons.Length > 0 ? horizons : new[] { 5, 10, 20, 60 };
    }

    public ChurnReversalReport Run()
    {
        int totalChurn = 0;
        int qualifying = 0;      // Churn + after-trend + small body
        var signedResults = new Dictionary<int, List<double>>(); // horizon -> signed forward returns
        var rawForward = new Dictionary<int, List<double>>();    // horizon -> raw forward returns

        foreach (var h in _horizons)
        {
            signedResults[h] = new List<double>();
            rawForward[h] = new List<double>();
        }

        for (int i = 0; i < _bars.Count; i++)
        {
            var bar = _bars[i];
            // Only look at closed bars already classified (skip warmup where signal=Normal)
            var result = _classifier.Classify(bar);
            if (result.Signal != VolumeSignal.Churn)
                continue;

            totalChurn++;

            // --- Trend condition: strong directional move over prior N bars ---
            if (i < _trendLookback) continue;
            double trendOpen = _bars[i - _trendLookback].Open;
            double trendMove = bar.Close - trendOpen; // candidate bar close vs lookback open
            // Use the close BEFORE this bar for trend sign
            double priorClose = _bars[i - 1].Close;
            double trendMovePrior = priorClose - trendOpen;

            // ATR over prior bars -> trend strength multiple
            double sumRange = 0;
            for (int k = i - _trendLookback; k < i; k++)
                sumRange += _bars[k].Spread;
            double avgRange = sumRange / _trendLookback;
            if (avgRange <= 0) continue;

            double trendSpread = Math.Abs(trendMovePrior) / avgRange;
            if (trendSpread < _minTrendSpread) continue;

            // --- Small body condition ---
            double body = Math.Abs(bar.Close - bar.Open);
            double range = bar.Spread;
            if (range <= 0) continue;
            double bodyRatio = body / range;
            if (bodyRatio > _maxBodyRatio) continue;

            qualifying++;

            // --- Direction of the trend (use prior move) ---
            bool trendIsUp = trendMovePrior > 0;

            // --- Signed forward returns (positive = reversal against trend) ---
            foreach (var h in _horizons)
            {
                int target = i + h;
                if (target >= _bars.Count) continue;
                double entry = bar.Close;
                double exit = _bars[target].Close;
                double raw = (exit - entry) / entry;
                rawForward[h].Add(raw);
                double signed = trendIsUp ? -raw : raw; // for uptrend, falling price = reversal = positive
                signedResults[h].Add(signed);
            }
        }

        var report = new ChurnReversalReport
        {
            TotalChurn = totalChurn,
            Qualifying = qualifying,
            Horizons = _horizons,
            SignedForward = signedResults,
            RawForward = rawForward
        };
        return report;
    }
}

public class ChurnReversalReport
{
    public int TotalChurn { get; set; }
    public int Qualifying { get; set; }
    public int[] Horizons { get; set; } = Array.Empty<int>();
    public Dictionary<int, List<double>> SignedForward { get; set; } = new();
    public Dictionary<int, List<double>> RawForward { get; set; } = new();
}
