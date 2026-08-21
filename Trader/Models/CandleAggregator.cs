using System;

namespace Trader.Models;

public class CandleAggregator
{
    private Candle _currentCandle;

    public event Action<Candle> OnCandleClosed;

    public void OnTick(DateTime timestamp, double price)
    {
        var minute = new DateTime(timestamp.Year, timestamp.Month, timestamp.Day,
            timestamp.Hour, timestamp.Minute, 0);

        if (_currentCandle == null)
        {
            _currentCandle = new Candle(minute, price);
            return;
        }

        if (minute > _currentCandle.Timestamp)
        {
            _currentCandle.IsComplete = true;
            OnCandleClosed?.Invoke(_currentCandle);
            _currentCandle = new Candle(minute, price);
            return;
        }

        _currentCandle.Update(price);
    }
}
