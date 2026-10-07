using System;
using Indicator;
using Ib.Csharp.Api.Client.Implementation.Helper;

namespace Trader.Model;

public class CandleAggregator
{
    private readonly object _lock = new();
    private readonly IndicatorEngine _indicator;
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

    public event Action<CandleUpdate> OnCandleUpdate;

    public CandleAggregator(IndicatorEngine indicator, int ticksPerBucket = TradingConfig.TicksPerBucket)
    {
        _indicator = indicator;
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
        IndicatorResult overlay;

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

            var result = _indicator.EvaluateForming(new Candle(bucketUnix, open, high, low, close, _volume));
            vwap = result.Vwap;
            color = result.Color;
            overlay = result;
        }

        var update = new CandleUpdate
        {
            Time = bucketUnix,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Vwap = vwap,
            Color = color
        };
        update.ApplyOverlay(overlay);
        OnCandleUpdate?.Invoke(update);
    }

    public void AddPriceTick(double price, DateTime? time = null)
    {
        if (price <= 0) return;

        long bucketUnix;
        double open, high, low, close, vwap;
        string color;
        IndicatorResult overlay;

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

            var result = _indicator.EvaluateForming(new Candle(bucketUnix, open, high, low, close, _volume));
            vwap = result.Vwap;
            color = result.Color;
            overlay = result;
        }

        var update = new CandleUpdate
        {
            Time = bucketUnix,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Vwap = vwap,
            Color = color
        };
        update.ApplyOverlay(overlay);
        OnCandleUpdate?.Invoke(update);
    }

    private void StartBucket(DateTime bucketTime, double price)
    {
        if (_hasCandle)
        {
            _indicator.ProcessCandle(
                new Candle(_currentBucketUnix, _open, _high, _low, _close, _volume));
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
