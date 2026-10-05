using System;

namespace Trader.Model;

public class CandleAggregator
{
    private readonly object _lock = new();
    private readonly VwapCalculator _vwapCalculator;
    private DateTime _currentMinute = DateTime.MinValue;
    private double _open;
    private double _high;
    private double _low;
    private double _close;
    private DateTime _candleTime;
    private bool _hasCandle;

    public event Action<DateTime, double, double, double, double, double> OnCandleUpdate;

    public CandleAggregator(VwapCalculator vwapCalculator)
    {
        _vwapCalculator = vwapCalculator;
    }

    public void AddPriceTick(double price, DateTime? time = null)
    {
        if (price <= 0) return;

        DateTime candleTime;
        double open, high, low, close, vwap;

        lock (_lock)
        {
            var now = time ?? DateTime.UtcNow;
            candleTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

            if (!_hasCandle || candleTime != _currentMinute)
            {
                _currentMinute = candleTime;
                _hasCandle = true;
                _open = price;
                _high = price;
                _low = price;
                _close = price;
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
        }

        OnCandleUpdate?.Invoke(candleTime, open, high, low, close, vwap);
    }

    public void UpdateVwap()
    {
        DateTime candleTime;
        double open, high, low, close, vwap;

        lock (_lock)
        {
            if (!_hasCandle) return;
            candleTime = _candleTime;
            open = _open;
            high = _high;
            low = _low;
            close = _close;
            vwap = _vwapCalculator.Current;
        }

        OnCandleUpdate?.Invoke(candleTime, open, high, low, close, vwap);
    }
}
