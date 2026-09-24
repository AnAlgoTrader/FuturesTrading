using System;
using Ib.Csharp.Api.Client.Implementation.Model;

namespace DayTrader.Helper;

public class CandleAggregator
{
    private readonly VwapCalculator _vwapCalculator;
    private DateTime _currentMinute = DateTime.MinValue;
    private double _open;
    private double _high;
    private double _low;
    private double _close;
    private DateTime _candleTime;

    public event Action<DateTime, double, double, double, double, double, string?>? OnCandleUpdate;

    public CandleAggregator(VwapCalculator vwapCalculator)
    {
        _vwapCalculator = vwapCalculator;
    }

    public void AddBar(RealTimeBar bar)
    {
        var barTime = DateTimeOffset.FromUnixTimeSeconds(bar.Date).UtcDateTime;
        var candleTime = new DateTime(barTime.Year, barTime.Month, barTime.Day, barTime.Hour, barTime.Minute, 0, DateTimeKind.Utc);
        var candleTimeUnix = new DateTimeOffset(candleTime).ToUnixTimeSeconds();

        if (_currentMinute == DateTime.MinValue || candleTime != _currentMinute)
        {
            _currentMinute = candleTime;
            _open = bar.Open;
            _high = bar.High;
            _low = bar.Low;
            _close = bar.Close;
            _candleTime = candleTime;
        }
        else
        {
            _high = Math.Max(_high, bar.High);
            _low = Math.Min(_low, bar.Low);
            _close = bar.Close;
        }

        var vwap = _vwapCalculator.AddBar(candleTimeUnix, (double)bar.Wap, (double)bar.Volume);
        OnCandleUpdate?.Invoke(_candleTime, _open, _high, _low, _close, vwap, GetCandleColor(_open, _high, _low, _close, vwap));
    }

    private static string? GetCandleColor(double open, double high, double low, double close, double vwap)
    {
        // Color logic based on candle values goes here; null keeps the default up/down colors.
        return null;
    }
}
