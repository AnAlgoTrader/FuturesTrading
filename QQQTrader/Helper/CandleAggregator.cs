using System;
using Ib.Csharp.Api.Client.Implementation.Model;

namespace QQQTrader.Helper;

public class CandleAggregator
{
    private DateTime _currentMinute = DateTime.MinValue;
    private double _open;
    private double _high;
    private double _low;
    private double _close;
    private long _timeSeconds;
    private double _sumPV;
    private double _sumVol;

    public event Action<long, double, double, double, double, double>? OnCandleUpdate;

    public void AddBar(RealTimeBar bar)
    {
        var barTime = DateTimeOffset.FromUnixTimeSeconds(bar.Date).UtcDateTime;
        var minuteStart = new DateTime(barTime.Year, barTime.Month, barTime.Day, barTime.Hour, barTime.Minute, 0, DateTimeKind.Utc);
        var minuteSeconds = new DateTimeOffset(minuteStart).ToUnixTimeSeconds();

        if (_currentMinute == DateTime.MinValue || minuteStart != _currentMinute)
        {
            _currentMinute = minuteStart;
            _open = bar.Open;
            _high = bar.High;
            _low = bar.Low;
            _close = bar.Close;
            _timeSeconds = minuteSeconds;
            _sumPV = (double)bar.Wap * (double)bar.Volume;
            _sumVol = (double)bar.Volume;
        }
        else
        {
            _high = Math.Max(_high, bar.High);
            _low = Math.Min(_low, bar.Low);
            _close = bar.Close;
            _sumPV += (double)bar.Wap * (double)bar.Volume;
            _sumVol += (double)bar.Volume;
        }

        var vwap = _sumVol > 0 ? _sumPV / _sumVol : 0;
        OnCandleUpdate?.Invoke(_timeSeconds, _open, _high, _low, _close, vwap);
    }
}
