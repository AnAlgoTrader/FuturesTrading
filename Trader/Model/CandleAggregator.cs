using System;
using Ib.Csharp.Api.Client.Implementation.Helper;

namespace Trader.Model;

public class CandleAggregator
{
    private readonly object _lock = new();
    private readonly VwapCalculator _vwapCalculator;
    private readonly ReversalDetector _reversalDetector;
    private readonly LeeReadyClassifier _classifier = new();
    private readonly int _ticksPerBucket;

    private long _currentBucketUnix;
    private int _tradesInBucket;
    private double _open;
    private double _high;
    private double _low;
    private double _close;
    private double _volume;
    private double _buyVolume;
    private double _sellVolume;
    private bool _hasCandle;

    public event Action<long, double, double, double, double, double, string> OnCandleUpdate;

    public CandleAggregator(VwapCalculator vwapCalculator, ReversalDetector reversalDetector,
        int ticksPerBucket = TradingConfig.TicksPerBucket)
    {
        _vwapCalculator = vwapCalculator;
        _reversalDetector = reversalDetector;
        _ticksPerBucket = Math.Max(1, ticksPerBucket);
    }

    /// <summary>
    /// Sets the newest historical candle time so the first tick-based candle is strictly newer.
    /// </summary>
    public void SeedLastCandleTime(long unixSeconds)
    {
        lock (_lock)
        {
            if (unixSeconds > _currentBucketUnix)
                _currentBucketUnix = unixSeconds;
        }
    }

    public void UpdateQuote(long time, double bid, double ask)
    {
        lock (_lock)
        {
            _classifier.OnQuote(time, bid, ask);
        }
    }

    public void AddTrade(long timeSeconds, double price, double size)
    {
        if (price <= 0) return;

        long bucketUnix;
        double open, high, low, close, vwap;
        string color;

        lock (_lock)
        {
            var tradeTime = timeSeconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(timeSeconds).UtcDateTime
                : DateTime.UtcNow;

            if (!_hasCandle || _tradesInBucket >= _ticksPerBucket)
            {
                StartBucket(tradeTime, price);
            }

            _tradesInBucket++;

            var side = _classifier.Classify(price);
            var tradeSize = Math.Max(0, size);

            _high = Math.Max(_high, price);
            _low = Math.Min(_low, price);
            _close = price;
            _volume += tradeSize;
            if (side == TradeSide.Buy) _buyVolume += tradeSize;
            else if (side == TradeSide.Sell) _sellVolume += tradeSize;

            bucketUnix = _currentBucketUnix;
            open = _open;
            high = _high;
            low = _low;
            close = _close;
            vwap = _vwapCalculator.Current;
            var delta = _buyVolume - _sellVolume;
            color = _reversalDetector.EvaluateCandle(bucketUnix, open, high, low, close, _volume, delta);
        }

        OnCandleUpdate?.Invoke(bucketUnix, open, high, low, close, vwap, color);
    }

    public void AddPriceTick(double price, DateTime? time = null)
    {
        if (price <= 0) return;

        long bucketUnix;
        double open, high, low, close, vwap;
        string color;

        lock (_lock)
        {
            var now = time ?? DateTime.UtcNow;

            if (!_hasCandle)
            {
                StartBucket(now, price);
            }

            _high = Math.Max(_high, price);
            _low = Math.Min(_low, price);
            _close = price;

            bucketUnix = _currentBucketUnix;
            open = _open;
            high = _high;
            low = _low;
            close = _close;
            vwap = _vwapCalculator.Current;
            var delta = _buyVolume - _sellVolume;
            color = _reversalDetector.EvaluateCandle(bucketUnix, open, high, low, close, _volume, delta);
        }

        OnCandleUpdate?.Invoke(bucketUnix, open, high, low, close, vwap, color);
    }

    public void UpdateVwap()
    {
        long bucketUnix;
        double open, high, low, close, vwap;
        string color;

        lock (_lock)
        {
            if (!_hasCandle) return;
            bucketUnix = _currentBucketUnix;
            open = _open;
            high = _high;
            low = _low;
            close = _close;
            vwap = _vwapCalculator.Current;
            var delta = _buyVolume - _sellVolume;
            color = _reversalDetector.EvaluateCandle(bucketUnix, open, high, low, close, _volume, delta);
        }

        OnCandleUpdate?.Invoke(bucketUnix, open, high, low, close, vwap, color);
    }

    private void StartBucket(DateTime bucketTime, double price)
    {
        if (_hasCandle)
        {
            _reversalDetector.OnCandleCompleted(_currentBucketUnix, _open, _high, _low, _close, _volume, _buyVolume - _sellVolume);
        }

        var unix = new DateTimeOffset(bucketTime).ToUnixTimeSeconds();
        if (unix <= _currentBucketUnix)
        {
            unix = _currentBucketUnix + 1;
        }

        _currentBucketUnix = unix;
        _tradesInBucket = 0;
        _hasCandle = true;
        _open = price;
        _high = price;
        _low = price;
        _close = price;
        _volume = 0;
        _buyVolume = 0;
        _sellVolume = 0;
    }
}
