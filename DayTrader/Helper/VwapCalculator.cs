using System;

namespace DayTrader.Helper;

public class VwapCalculator
{
    private double _sumPV;
    private double _sumVol;
    private DateTime _currentUtcDay = DateTime.MinValue;

    public double AddBar(long timeSeconds, double wap, double volume)
    {
        if (timeSeconds > 0)
        {
            var utcDay = DateTimeOffset.FromUnixTimeSeconds(timeSeconds).UtcDateTime.Date;
            if (utcDay != _currentUtcDay)
            {
                _currentUtcDay = utcDay;
                _sumPV = 0;
                _sumVol = 0;
            }
        }

        _sumPV += wap * volume;
        _sumVol += volume;
        return Current;
    }

    private double Current => _sumVol > 0 ? _sumPV / _sumVol : 0;
}