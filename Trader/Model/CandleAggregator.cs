using System;
using Ib.Csharp.Api.Client.Implementation.Model;

namespace Trader.Model;

public class CandleAggregator
{
    private readonly VwapCalculator _vwapCalculator;
    private readonly ClimaxExhaustionDetector _climaxExhaustionDetector;
    private DateTime _currentMinute = DateTime.MinValue;
    private double _open;
    private double _high;
    private double _low;
    private double _close;
    private double _volume;
    private DateTime _candleTime;
    private bool _hasCandle;

    public event Action<DateTime, double, double, double, double, double, string?>? OnCandleUpdate;

    public CandleAggregator(VwapCalculator vwapCalculator, ClimaxExhaustionDetector? climaxExhaustionDetector = null)
    {
        _vwapCalculator = vwapCalculator;
        _climaxExhaustionDetector = climaxExhaustionDetector ?? new ClimaxExhaustionDetector();
    }

    public void AddBar(RealTimeBar bar)
    {
        if (bar.Volume == 0 || bar is { Open: 0, High: 0, Low: 0, Close: 0 }) return;

        var barTime = DateTimeOffset.FromUnixTimeSeconds(bar.Date).UtcDateTime;
        var candleTime = new DateTime(barTime.Year, barTime.Month, barTime.Day, barTime.Hour, barTime.Minute, 0, DateTimeKind.Utc);
        var candleTimeUnix = new DateTimeOffset(candleTime).ToUnixTimeSeconds();

        if (!_hasCandle || candleTime != _currentMinute)
        {
            _currentMinute = candleTime;
            _hasCandle = true;
            _open = bar.Open;
            _high = bar.High;
            _low = bar.Low;
            _close = bar.Close;
            _volume = (double)bar.Volume;
            _candleTime = candleTime;
        }
        else
        {
            _high = Math.Max(_high, bar.High);
            _low = Math.Min(_low, bar.Low);
            _close = bar.Close;
            _volume += (double)bar.Volume;
        }

        var vwap = _vwapCalculator.AddBar(candleTimeUnix, (double)bar.Wap, (double)bar.Volume);
        var color = _climaxExhaustionDetector.ProcessCandle(candleTimeUnix, _open, _high, _low, _close, _volume);
        OnCandleUpdate?.Invoke(_candleTime, _open, _high, _low, _close, vwap, color);
    }
}
