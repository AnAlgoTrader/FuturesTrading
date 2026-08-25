using System;
using System.Collections.Generic;
using System.Linq;
using Common;

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
}

public class ClassificationResult
{
    public VolumeSignal Signal { get; set; }
    public Bar Bar { get; set; }
    public decimal CumulativeDelta { get; set; }
    public bool DeltaConfirmed { get; set; }
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

    /// <summary>Call this from TickBarAggregator.OnBarClosed only.</summary>
    public ClassificationResult Classify(Bar bar)
    {
        _cumulativeDelta += bar.Delta;
        TrackImbalanceStreak(bar);

        _window.Enqueue(bar);
        if (_window.Count > _lookback)
            _window.Dequeue();

        var result = new ClassificationResult
        {
            Bar = bar,
            CumulativeDelta = _cumulativeDelta
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

        if (_consecutiveBuyImbalance >= _stackedImbalanceCount ||
            _consecutiveSellImbalance >= _stackedImbalanceCount)
        {
            result.Signal = VolumeSignal.StackedImbalance;
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
