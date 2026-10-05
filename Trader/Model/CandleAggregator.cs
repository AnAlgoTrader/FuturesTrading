using System;
using Ib.Csharp.Api.Client.Implementation.Helper;

namespace Trader.Model;

public class CandleAggregator
{
    private readonly object _lock = new();
    private readonly VwapCalculator _vwapCalculator;
    private readonly ReversalDetector _reversalDetector;
    private readonly LeeReadyClassifier _classifier = new();

    private DateTime _currentMinute = DateTime.MinValue;
    private long _currentMinuteUnix;
    private double _open;
    private double _high;
    private double _low;
    private double _close;
    private double _volume;
    private double _buyVolume;
    private double _sellVolume;
    private DateTime _candleTime;
    private bool _hasCandle;

    public event Action<DateTime, double, double, double, double, double, string> OnCandleUpdate;

    public CandleAggregator(VwapCalculator vwapCalculator, ReversalDetector reversalDetector)
    {
        _vwapCalculator = vwapCalculator;
        _reversalDetector = reversalDetector;
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

        DateTime candleTime;
        double open, high, low, close, vwap;
        string color;

        lock (_lock)
        {
            var tradeTime = timeSeconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(timeSeconds).UtcDateTime
                : DateTime.UtcNow;
            candleTime = new DateTime(tradeTime.Year, tradeTime.Month, tradeTime.Day, tradeTime.Hour, tradeTime.Minute, 0, DateTimeKind.Utc);
            var candleTimeUnix = new DateTimeOffset(candleTime).ToUnixTimeSeconds();

            var side = _classifier.Classify(price);
            var tradeSize = Math.Max(0, size);

            if (!_hasCandle || candleTime != _currentMinute)
            {
                if (_hasCandle)
                {
                    _reversalDetector.OnCandleCompleted(_currentMinuteUnix, _open, _high, _low, _close, _volume, _buyVolume - _sellVolume);
                }

                _currentMinute = candleTime;
                _currentMinuteUnix = candleTimeUnix;
                _hasCandle = true;
                _open = price;
                _high = price;
                _low = price;
                _close = price;
                _volume = tradeSize;
                _buyVolume = side == TradeSide.Buy ? tradeSize : 0;
                _sellVolume = side == TradeSide.Sell ? tradeSize : 0;
                _candleTime = candleTime;
            }
            else
            {
                _high = Math.Max(_high, price);
                _low = Math.Min(_low, price);
                _close = price;
                _volume += tradeSize;
                if (side == TradeSide.Buy) _buyVolume += tradeSize;
                else if (side == TradeSide.Sell) _sellVolume += tradeSize;
            }

            open = _open;
            high = _high;
            low = _low;
            close = _close;
            vwap = _vwapCalculator.Current;
            var delta = _buyVolume - _sellVolume;
            color = _reversalDetector.EvaluateCandle(candleTimeUnix, open, high, low, close, _volume, delta);
        }

        OnCandleUpdate?.Invoke(candleTime, open, high, low, close, vwap, color);
    }

    public void AddPriceTick(double price, DateTime? time = null)
    {
        if (price <= 0) return;

        DateTime candleTime;
        double open, high, low, close, vwap;
        string color;

        lock (_lock)
        {
            var now = time ?? DateTime.UtcNow;
            candleTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
            var candleTimeUnix = new DateTimeOffset(candleTime).ToUnixTimeSeconds();

            if (!_hasCandle || candleTime != _currentMinute)
            {
                if (_hasCandle)
                {
                    _reversalDetector.OnCandleCompleted(_currentMinuteUnix, _open, _high, _low, _close, _volume, _buyVolume - _sellVolume);
                }

                _currentMinute = candleTime;
                _currentMinuteUnix = candleTimeUnix;
                _hasCandle = true;
                _open = price;
                _high = price;
                _low = price;
                _close = price;
                _volume = 0;
                _buyVolume = 0;
                _sellVolume = 0;
                _candleTime = candleTime;
            }
            else
            {
                _high = Math.Max(_high, price);
                _low = Math.Min(_low, price);
                _close = price;
            }

            open = _open;
            high = _high;
            low = _low;
            close = _close;
            vwap = _vwapCalculator.Current;
            var delta = _buyVolume - _sellVolume;
            color = _reversalDetector.EvaluateCandle(candleTimeUnix, open, high, low, close, _volume, delta);
        }

        OnCandleUpdate?.Invoke(candleTime, open, high, low, close, vwap, color);
    }

    public void UpdateVwap()
    {
        DateTime candleTime;
        double open, high, low, close, vwap;
        string color;

        lock (_lock)
        {
            if (!_hasCandle) return;
            candleTime = _candleTime;
            open = _open;
            high = _high;
            low = _low;
            close = _close;
            vwap = _vwapCalculator.Current;
            var candleTimeUnix = new DateTimeOffset(candleTime).ToUnixTimeSeconds();
            var delta = _buyVolume - _sellVolume;
            color = _reversalDetector.EvaluateCandle(candleTimeUnix, open, high, low, close, _volume, delta);
        }

        OnCandleUpdate?.Invoke(candleTime, open, high, low, close, vwap, color);
    }
}
