using System;
using Ib.Csharp.Api.Client.Implementation.Model;

namespace Common;

public class CandleAggregator
{
    private Candle _currentCandle;

    public event Action<Candle> OnCandleClosed;
    public event Action<Candle> OnCandleUpdated;

    public void OnTick(DateTime timestamp, PriceUpdate price)
    {
        var mid = (price.BidPrice + price.AskPrice) / 2;

        var minute = new DateTime(timestamp.Year, timestamp.Month, timestamp.Day,
            timestamp.Hour, timestamp.Minute, 0);

        if (_currentCandle == null)
        {
            _currentCandle = new Candle(minute, mid);
            OnCandleUpdated?.Invoke(_currentCandle);
            return;
        }

        if (minute > _currentCandle.Timestamp)
        {
            _currentCandle.IsComplete = true;
            OnCandleClosed?.Invoke(_currentCandle);
            _currentCandle = new Candle(minute, mid);
            OnCandleUpdated?.Invoke(_currentCandle);
            return;
        }
        _currentCandle.Update(mid);
        OnCandleUpdated?.Invoke(_currentCandle);
    }
}
