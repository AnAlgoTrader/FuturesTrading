using System;

namespace IndicatorBuild.Model;

public class VwapCalculator
{
    private readonly object _lock = new();
    private double _sumPv;
    private double _sumVol;
    private DateTime _currentUtcDay = DateTime.MinValue;

    public double Current
    {
        get
        {
            lock (_lock)
            {
                return _sumVol > 0 ? _sumPv / _sumVol : 0;
            }
        }
    }

    public double AddBar(long timeSeconds, double price, double volume)
    {
        lock (_lock)
        {
            if (timeSeconds > 0)
            {
                var utcDay = DateTimeOffset.FromUnixTimeSeconds(timeSeconds).UtcDateTime.Date;
                if (utcDay != _currentUtcDay)
                {
                    _currentUtcDay = utcDay;
                    _sumPv = 0;
                    _sumVol = 0;
                }
            }

            if (volume <= 0 || price <= 0) return Current;

            _sumPv += price * volume;
            _sumVol += volume;
            return Current;
        }
    }
}