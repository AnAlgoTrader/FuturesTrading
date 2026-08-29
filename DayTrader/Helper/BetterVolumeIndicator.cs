using System;
using System.Collections.Generic;
using System.Linq;

namespace DayTrader.Helper;

public enum VolumeSignal
{
    Normal,
    ClimaxUp,
    ClimaxDown,
    Churn,
    LowVolume,
    Absorption,
    StackedImbalance
}

public static class VolumeSignalExtensions
{
    private static readonly Dictionary<VolumeSignal, string> Colors = new()
    {
        // slate gray — unremarkable bar
        [VolumeSignal.Normal] = "#787b86",
        // vibrant green — high vol, widespread, closed up (climax buying)
        [VolumeSignal.ClimaxUp] = "#26a69a",
        // vibrant red — high vol, widespread, closed down (climax selling)
        [VolumeSignal.ClimaxDown] = "#ef5350",
        // orange / amber — high vol, narrow spread (indecision / churn)
        [VolumeSignal.Churn] = "#ff9800",
        // yellow / gold — lowest vol in window (apathy)
        [VolumeSignal.LowVolume] = "#fdd835",
        // purple / magenta — high vol, strong delta, price pinned (absorption)
        [VolumeSignal.Absorption] = "#ab47bc",
        // cyan / electric blue — N bars in a row leaning one way (stacked imbalance)
        [VolumeSignal.StackedImbalance] = "#00b0ff"
    };

    public static string ToColor(this VolumeSignal signal)
    {
        return Colors.GetValueOrDefault(signal, "transparent");
    }

    public static string GetName(this VolumeSignal signal)
    {
        return signal switch
        {
            VolumeSignal.Normal => "Normal",
            VolumeSignal.ClimaxUp => "Climax Up",
            VolumeSignal.ClimaxDown => "Climax Down",
            VolumeSignal.Churn => "Churn",
            VolumeSignal.LowVolume => "Low Volume",
            VolumeSignal.Absorption => "Absorption",
            VolumeSignal.StackedImbalance => "Stacked Imbalance",
            _ => "Unknown"
        };
    }

    public static string GetDescription(this VolumeSignal signal)
    {
        return signal switch
        {
            VolumeSignal.Normal => "Volume and spread are within normal ranges — no anomaly detected",
            VolumeSignal.ClimaxUp => "High volume with wide upward spread — indicates aggressive buying that may signal a buying climax or blow-off top",
            VolumeSignal.ClimaxDown => "High volume with wide downward spread — indicates panic selling that may signal a selling climax or capitulation low",
            VolumeSignal.Churn => "High volume with narrow spread — indicates heavy turnover with minimal price progress, signaling accumulation or distribution",
            VolumeSignal.LowVolume => "Lowest volume in lookback window — indicates low market participation, often preceding a breakout or volatility expansion",
            VolumeSignal.Absorption => "High volume with strong delta and narrow spread — indicates passive limit orders are absorbing aggressive market orders",
            VolumeSignal.StackedImbalance => "Multiple consecutive bars with one-sided delta — indicates sustained directional order flow pressure",
            _ => ""
        };
    }
}

public class ClassificationResult
{
    public VolumeSignal Signal { get; set; } = VolumeSignal.Normal;

    /// <summary>
    /// Hysteresis-filtered signal. Identical to Signal unless the classifier was
    /// constructed with minSignalPersistence > 1, in which case a non-Normal signal
    /// only "confirms" here once it has appeared on that many consecutive closed bars.
    /// Use this instead of Signal if you're triggering trades directly off the
    /// indicator rather than just eyeballing it, to avoid acting on single-bar flicker.
    /// </summary>
    public VolumeSignal StableSignal { get; set; } = VolumeSignal.Normal;

    public Bar Bar { get; init; }
    public decimal CumulativeDelta { get; set; }
    public bool? DeltaConfirmed { get; set; }
    public int BuyStreak { get; init; }
    public int SellStreak { get; init; }

    public string DeltaDisplay => $"Delta: {Bar?.Delta ?? 0}";
    public string DeltaRatioDisplay => $"Ratio: {Bar?.DeltaRatio ?? 0:F4}";
    public string DeltaStatus => DeltaConfirmed switch
    {
        true => "Confirmed",
        false => "Divergent",
        null => ""
    };
    public string DeltaStatusColor => DeltaConfirmed switch
    {
        true => "#26a69a",
        false => "#ef5350",
        null => "#787b86"
    };

    public string StreakInfo => BuyStreak > 0
        ? $"Buy streak: {BuyStreak}"
        : SellStreak > 0
            ? $"Sell streak: {SellStreak}"
            : "";

    public static ClassificationResult Default => new()
    {
        Signal = VolumeSignal.Normal,
        StableSignal = VolumeSignal.Normal,
        Bar = new Bar()
    };
}

/// <summary>
/// Rolling Better Volume classifier, delta-aware. Feed it CLOSED bars only
/// (subscribe to TickBarAggregator.OnBarClosed) -- classifying a
/// still-forming bar would rank incomplete volume/spread numbers against
/// a window of complete ones.
///
///  - Volume/spread reference stats (max/min/avg) are computed from the PRIOR
///    N bars only, before the current bar is enqueued. Previously the current
///    bar's own spread/volume was folded into the average it was being judged
///    against, which slightly dampened how extreme a genuine climax/churn bar
///    could look.
///  - The rolling window (not just cumulative delta and streaks) is cleared on
///    a session gap, so volume/spread stats never span two different trading
///    sessions (e.g. overnight Globex vs. RTH open) that have structurally
///    different volume regimes.
///  - The absolute-extreme and percentile-fallback checks now share one
///    helper (TryClassifyByVolumeExtreme) instead of duplicating the same
///    Absorption/Climax/Churn/LowVolume logic twice.
///  - Optional signal hysteresis via minSignalPersistence: a non-Normal
///    signal only "confirms" into StableSignal once it has repeated for that
///    many consecutive bars, to reduce bar-to-bar flicker if you're acting on
///    signals directly. Defaults to 1 (no behavior change / same as before).
/// </summary>
public class BetterVolumeClassifier
{
    private readonly Logger _logger;
    private readonly int _lookBack;
    private readonly double _churnSpreadMultiplier;
    private readonly double _absorptionDeltaRatioThreshold;
    private readonly double _absorptionSpreadMaxMultiplier;
    private readonly int _stackedImbalanceCount;
    private readonly double _stackedImbalanceDeltaRatioThreshold;
    private readonly int _minSignalPersistence;

    private readonly Queue<Bar> _window = new();
    private decimal _cumulativeDelta;
    private int _consecutiveBuyImbalance;
    private int _consecutiveSellImbalance;
    private long _previousBarEndTime;

    private VolumeSignal _lastRawSignal = VolumeSignal.Normal;
    private VolumeSignal _stableSignal = VolumeSignal.Normal;
    private int _pendingStreak;

    public BetterVolumeClassifier(
        Logger logger,
        int lookBack = 20,
        double churnSpreadMultiplier = 1.5,
        double absorptionDeltaRatioThreshold = 0.6,
        double absorptionSpreadMaxMultiplier = 0.5,
        int stackedImbalanceCount = 3,
        double stackedImbalanceDeltaRatioThreshold = 0.5,
        int minSignalPersistence = 1)
    {
        _logger = logger;
        _lookBack = lookBack;
        _churnSpreadMultiplier = churnSpreadMultiplier;
        _absorptionDeltaRatioThreshold = absorptionDeltaRatioThreshold;
        _absorptionSpreadMaxMultiplier = absorptionSpreadMaxMultiplier;
        _stackedImbalanceCount = stackedImbalanceCount;
        _stackedImbalanceDeltaRatioThreshold = stackedImbalanceDeltaRatioThreshold;
        _minSignalPersistence = Math.Max(1, minSignalPersistence);
    }

    public void ResetCumulativeDelta()
    {
        _cumulativeDelta = 0;
        _consecutiveBuyImbalance = 0;
        _consecutiveSellImbalance = 0;
        _lastRawSignal = VolumeSignal.Normal;
        _stableSignal = VolumeSignal.Normal;
        _pendingStreak = 0;
        _logger.WriteLine("[CLASSIFY] Cumulative Delta, streaks, and signal state reset.");
    }

    /// <summary>
    /// Call this from TickBarAggregator.OnBarClosed only.
    /// </summary>
    public ClassificationResult Classify(Bar bar)
    {
        const long sessionGapSeconds = 1800;
        bool sessionBreak = _previousBarEndTime > 0 && (bar.StartTime - _previousBarEndTime) > sessionGapSeconds;
        if (sessionBreak)
        {
            _cumulativeDelta = 0;
            _consecutiveBuyImbalance = 0;
            _consecutiveSellImbalance = 0;
            // Clear the window too: a rolling max/min/avg that spans the session
            // boundary would mix overnight-thin volume with RTH-open volume, which
            // are not comparable baselines.
            _window.Clear();
            _logger.WriteLine("[CLASSIFY] Session break detected (gap > 30m) - Cumulative Delta, streaks, and window reset.");
        }
        _previousBarEndTime = bar.EndTime;

        _cumulativeDelta += bar.Delta;
        TrackImbalanceStreak(bar);

        var result = new ClassificationResult
        {
            Bar = bar,
            CumulativeDelta = _cumulativeDelta,
            BuyStreak = _consecutiveBuyImbalance,
            SellStreak = _consecutiveSellImbalance
        };

        if (_window.Count < _lookBack)
        {
            result.Signal = VolumeSignal.Normal;
            _logger.WriteLine($"[CLASSIFY] Warming up: window={_window.Count}/{_lookBack}");
            return FinalizeClassification();
        }

        // Reference stats come from the PRIOR _lookBack bars only — the current
        // bar has not been enqueued yet, so it can't inflate/deflate its own baseline.
        var volumes = _window.Select(b => b.Volume).ToList();
        var spreads = _window.Select(b => b.Spread).ToList();

        decimal maxVol = volumes.Max();
        decimal minVol = volumes.Min();
        double avgSpread = spreads.Average();

        bool isHighestVolume = bar.Volume >= maxVol;
        bool isLowestVolume = bar.Volume <= minVol;
        bool isWideSpread = bar.Spread >= avgSpread * _churnSpreadMultiplier;
        bool isNarrowSpread = bar.Spread < avgSpread;
        bool isVeryNarrowSpread = bar.Spread <= avgSpread * _absorptionSpreadMaxMultiplier;
        bool isStrongDelta = Math.Abs(bar.DeltaRatio) >= _absorptionDeltaRatioThreshold;

        _logger.WriteLine($"[CLASSIFY] MaxVol:{maxVol} MinVol:{minVol} AvgSpread:{avgSpread:F4} | " +
                          $"HiVol:{isHighestVolume} LoVol:{isLowestVolume} Wide:{isWideSpread} Narrow:{isNarrowSpread} VeryNarrow:{isVeryNarrowSpread} StrongDelta:{isStrongDelta} " +
                          $"BuyStreak:{_consecutiveBuyImbalance} SellStreak:{_consecutiveSellImbalance}");

        if (TryClassifyByVolumeExtreme(bar, isHighestVolume, isLowestVolume, isWideSpread, isNarrowSpread,
                isVeryNarrowSpread, isStrongDelta, result, percentileTier: false))
        {
            return FinalizeClassification();
        }

        if (_consecutiveBuyImbalance >= _stackedImbalanceCount ||
            _consecutiveSellImbalance >= _stackedImbalanceCount)
        {
            result.Signal = VolumeSignal.StackedImbalance;
            result.DeltaConfirmed = true;
            return FinalizeClassification();
        }

        // Percentile fallback: if no absolute threshold fired, check relative position
        var sortedVol = volumes.OrderBy(v => v).ToList();
        int p80Idx = (int)(sortedVol.Count * 0.8);
        int p20Idx = (int)(sortedVol.Count * 0.2);
        bool isTop20Volume = bar.Volume >= sortedVol[p80Idx];
        bool isBottom20Volume = bar.Volume <= sortedVol[p20Idx];

        if (TryClassifyByVolumeExtreme(bar, isTop20Volume, isBottom20Volume, isWideSpread, isNarrowSpread,
                isVeryNarrowSpread, isStrongDelta, result, percentileTier: true))
        {
            return FinalizeClassification();
        }

        result.Signal = VolumeSignal.Normal;
        return FinalizeClassification();

        ClassificationResult FinalizeClassification()
        {
            _window.Enqueue(bar);
            if (_window.Count > _lookBack)
                _window.Dequeue();

            ApplyHysteresis(result);
            return result;
        }
    }

    /// <summary>
    /// Shared by the absolute-extreme check and the top/bottom-20% percentile
    /// fallback so both tiers apply exactly the same Absorption/Climax/Churn/
    /// LowVolume rules instead of two hand-maintained copies of the same logic.
    /// Returns true (and sets result.Signal/DeltaConfirmed) if a signal fired.
    /// </summary>
    private bool TryClassifyByVolumeExtreme(
        Bar bar,
        bool isHighVolume,
        bool isLowVolume,
        bool isWideSpread,
        bool isNarrowSpread,
        bool isVeryNarrowSpread,
        bool isStrongDelta,
        ClassificationResult result,
        bool percentileTier)
    {
        string tierTag = percentileTier ? "[CLASSIFY] Percentile " : null;

        if (isHighVolume && isStrongDelta && isVeryNarrowSpread)
        {
            result.Signal = VolumeSignal.Absorption;
            result.DeltaConfirmed = true;
            if (tierTag != null) _logger.WriteLine(tierTag + "Absorption hit");
            return true;
        }

        if (isHighVolume && isWideSpread)
        {
            bool priceUp = bar.Close > bar.Open;
            bool deltaAgrees = priceUp ? bar.Delta > 0 : bar.Delta < 0;
            result.Signal = priceUp ? VolumeSignal.ClimaxUp : VolumeSignal.ClimaxDown;
            result.DeltaConfirmed = deltaAgrees;
            if (tierTag != null) _logger.WriteLine(tierTag + "Climax hit");
            return true;
        }

        if (isHighVolume && isNarrowSpread)
        {
            result.Signal = VolumeSignal.Churn;
            if (tierTag != null) _logger.WriteLine(tierTag + "Churn hit");
            return true;
        }

        if (isLowVolume)
        {
            result.Signal = VolumeSignal.LowVolume;
            if (tierTag != null) _logger.WriteLine(tierTag + "LowVolume hit");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Filters Signal into StableSignal. With the default minSignalPersistence
    /// of 1 this is a no-op (StableSignal always equals Signal). With a higher
    /// value, a non-Normal signal must repeat on N consecutive closed bars
    /// before StableSignal picks it up, which cuts down on single-bar flicker
    /// if you're triggering trades off the indicator directly. Normal always
    /// reflects immediately, since there's no risk in "confirming" a null signal.
    /// </summary>
    private void ApplyHysteresis(ClassificationResult result)
    {
        if (_minSignalPersistence <= 1)
        {
            result.StableSignal = result.Signal;
            return;
        }

        _pendingStreak = result.Signal == _lastRawSignal ? _pendingStreak + 1 : 1;
        _lastRawSignal = result.Signal;

        if (result.Signal == VolumeSignal.Normal || _pendingStreak >= _minSignalPersistence)
        {
            _stableSignal = result.Signal;
        }
        // else: not yet confirmed for N consecutive bars — StableSignal holds its previous value.

        result.StableSignal = _stableSignal;
    }

    private void TrackImbalanceStreak(Bar bar)
    {
        if (bar.DeltaRatio >= _stackedImbalanceDeltaRatioThreshold)
        {
            _consecutiveBuyImbalance++;
            _consecutiveSellImbalance = 0;
        }
        else if (bar.DeltaRatio <= -_stackedImbalanceDeltaRatioThreshold)
        {
            _consecutiveSellImbalance++;
            _consecutiveBuyImbalance = 0;
        }
        else
        {
            _consecutiveBuyImbalance = 0;
            _consecutiveSellImbalance = 0;
        }
    }
}