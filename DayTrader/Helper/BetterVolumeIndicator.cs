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
        // gray — unremarkable bar
        [VolumeSignal.Normal] = "#888888",
        // red — high vol, widespread, closed up
        [VolumeSignal.ClimaxUp] = "#e74c3c",
        // white — high vol, widespread, closed down
        [VolumeSignal.ClimaxDown] = "#ecf0f1",
        // green — high vol, narrow spread (indecision)
        [VolumeSignal.Churn] = "#2ecc71",
        // yellow — lowest vol in window (apathy)
        [VolumeSignal.LowVolume] = "#f1c40f",
        // purple — high vol, strong delta, price pinned
        [VolumeSignal.Absorption] = "#9b59b6",
        // blue — N bars in a row leaning one way
        [VolumeSignal.StackedImbalance] = "#3498db"
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
    public Bar Bar { get; set; }
    public decimal CumulativeDelta { get; set; }
    public bool? DeltaConfirmed { get; set; }
    public int BuyStreak { get; set; }
    public int SellStreak { get; set; }

    public string SignalColor => Signal.ToColor();
    public string SignalName => Signal.GetName();
    public string SignalDescription
    {
        get
        {
            if (Signal == VolumeSignal.StackedImbalance)
            {
                int count = Math.Max(BuyStreak, SellStreak);
                if (count > 0)
                {
                    string side = BuyStreak > SellStreak ? "buying" : "selling";
                    return $"{count} consecutive bars with heavy {side} imbalance — indicates sustained directional order flow pressure";
                }
            }

            return Signal.GetDescription();
        }
    }
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
        Bar = new Bar()
    };
}

/// <summary>
/// Rolling Better Volume classifier, delta-aware. Feed it CLOSED bars only
/// (subscribe to TickBarAggregator.OnBarClosed) -- classifying a
/// still-forming bar would rank incomplete volume/spread numbers against
/// a window of complete ones.
/// </summary>
public class BetterVolumeClassifier
{
    private readonly Logger _logger;
    private readonly int _lookback;
    private readonly double _churnSpreadMultiplier;
    private readonly double _absorptionDeltaRatioThreshold;
    private readonly double _absorptionSpreadMaxMultiplier;
    private readonly int _stackedImbalanceCount;
    private readonly double _stackedImbalanceDeltaRatioThreshold;

    private readonly Queue<Bar> _window = new();
    private decimal _cumulativeDelta;
    private int _consecutiveBuyImbalance;
    private int _consecutiveSellImbalance;
    private long _previousBarEndTime;

    public BetterVolumeClassifier(
        Logger logger,
        int lookback = 8,
        double churnSpreadMultiplier = 1.5,
        double absorptionDeltaRatioThreshold = 0.6,
        double absorptionSpreadMaxMultiplier = 0.5,
        int stackedImbalanceCount = 3,
        double stackedImbalanceDeltaRatioThreshold = 0.5)
    {
        _logger = logger;
        _lookback = lookback;
        _churnSpreadMultiplier = churnSpreadMultiplier;
        _absorptionDeltaRatioThreshold = absorptionDeltaRatioThreshold;
        _absorptionSpreadMaxMultiplier = absorptionSpreadMaxMultiplier;
        _stackedImbalanceCount = stackedImbalanceCount;
        _stackedImbalanceDeltaRatioThreshold = stackedImbalanceDeltaRatioThreshold;
    }

    public void ResetCumulativeDelta()
    {
        _cumulativeDelta = 0;
        _consecutiveBuyImbalance = 0;
        _consecutiveSellImbalance = 0;
        _logger.WriteLine("[CLASSIFY] Cumulative Delta and streaks reset.");
    }

    /// <summary>Call this from TickBarAggregator.OnBarClosed only.</summary>
    public ClassificationResult Classify(Bar bar)
    {
        const long sessionGapSeconds = 1800;
        if (_previousBarEndTime > 0 && (bar.StartTime - _previousBarEndTime) > sessionGapSeconds)
        {
            _cumulativeDelta = 0;
            _consecutiveBuyImbalance = 0;
            _consecutiveSellImbalance = 0;
            _logger.WriteLine("[CLASSIFY] Session break detected (gap > 30m) - Cumulative Delta and streaks reset.");
        }
        _previousBarEndTime = bar.EndTime;

        _cumulativeDelta += bar.Delta;
        TrackImbalanceStreak(bar);

        _window.Enqueue(bar);
        if (_window.Count > _lookback)
            _window.Dequeue();

        var result = new ClassificationResult
        {
            Bar = bar,
            CumulativeDelta = _cumulativeDelta,
            BuyStreak = _consecutiveBuyImbalance,
            SellStreak = _consecutiveSellImbalance
        };

        if (_window.Count < _lookback)
        {
            result.Signal = VolumeSignal.Normal;
            _logger.WriteLine($"[CLASSIFY] Warming up: window={_window.Count}/{_lookback}");
            return result;
        }

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
        bool isStrongDelta = System.Math.Abs(bar.DeltaRatio) >= _absorptionDeltaRatioThreshold;

        _logger.WriteLine($"[CLASSIFY] MaxVol:{maxVol} MinVol:{minVol} AvgSpread:{avgSpread:F4} | " +
                          $"HiVol:{isHighestVolume} LoVol:{isLowestVolume} Wide:{isWideSpread} Narrow:{isNarrowSpread} VeryNarrow:{isVeryNarrowSpread} StrongDelta:{isStrongDelta} " +
                          $"BuyStreak:{_consecutiveBuyImbalance} SellStreak:{_consecutiveSellImbalance}");

        if (isHighestVolume && isStrongDelta && isVeryNarrowSpread)
        {
            result.Signal = VolumeSignal.Absorption;
            result.DeltaConfirmed = true;
            return result;
        }

        if (isHighestVolume && isWideSpread)
        {
            bool priceUp = bar.Close > bar.Open;
            bool deltaAgrees = priceUp ? bar.Delta > 0 : bar.Delta < 0;

            result.Signal = priceUp ? VolumeSignal.ClimaxUp : VolumeSignal.ClimaxDown;
            result.DeltaConfirmed = deltaAgrees;
            return result;
        }

        if (_consecutiveBuyImbalance >= _stackedImbalanceCount ||
            _consecutiveSellImbalance >= _stackedImbalanceCount)
        {
            result.Signal = VolumeSignal.StackedImbalance;
            result.DeltaConfirmed = true;
            return result;
        }

        if (isHighestVolume && isNarrowSpread)
        {
            result.Signal = VolumeSignal.Churn;
            return result;
        }

        if (isLowestVolume)
        {
            result.Signal = VolumeSignal.LowVolume;
            return result;
        }

        // Percentile fallback: if no absolute threshold fired, check relative position
        var sortedVol = volumes.OrderBy(v => v).ToList();
        int p80Idx = (int)(sortedVol.Count * 0.8);
        int p20Idx = (int)(sortedVol.Count * 0.2);
        bool isTop20Volume = bar.Volume >= sortedVol[p80Idx];
        bool isBottom20Volume = bar.Volume <= sortedVol[p20Idx];

        if (isTop20Volume && isStrongDelta && isVeryNarrowSpread)
        {
            result.Signal = VolumeSignal.Absorption;
            result.DeltaConfirmed = true;
            _logger.WriteLine($"[CLASSIFY] Percentile Absorption hit");
            return result;
        }

        if (isTop20Volume && isWideSpread)
        {
            bool priceUp = bar.Close > bar.Open;
            bool deltaAgrees = priceUp ? bar.Delta > 0 : bar.Delta < 0;
            result.Signal = priceUp ? VolumeSignal.ClimaxUp : VolumeSignal.ClimaxDown;
            result.DeltaConfirmed = deltaAgrees;
            _logger.WriteLine($"[CLASSIFY] Percentile Climax hit");
            return result;
        }

        if (isTop20Volume && isNarrowSpread)
        {
            result.Signal = VolumeSignal.Churn;
            _logger.WriteLine($"[CLASSIFY] Percentile Churn hit");
            return result;
        }

        if (isBottom20Volume)
        {
            result.Signal = VolumeSignal.LowVolume;
            _logger.WriteLine($"[CLASSIFY] Percentile LowVolume hit");
            return result;
        }

        result.Signal = VolumeSignal.Normal;
        return result;
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
