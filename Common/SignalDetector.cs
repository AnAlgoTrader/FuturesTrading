using System;
using System.Collections.Generic;
using System.Linq;

namespace Common;

public class SignalDetector
{
    private readonly int _windowSize;
    private readonly double _entryThreshold;
    private readonly List<double> _logReturns = new();
    private double _previousClose;
    private bool _hasPreviousClose;

    public SignalDetector(int windowSize = 1000, double entryThreshold = 2.0)
    {
        _windowSize = windowSize;
        _entryThreshold = entryThreshold;
    }

    public Signal OnCandleClosed(Candle candle)
    {
        if (!_hasPreviousClose)
        {
            _previousClose = candle.Close;
            _hasPreviousClose = true;
            return null;
        }

        var logReturn = Math.Log(candle.Close / _previousClose);
        _previousClose = candle.Close;

        _logReturns.Add(logReturn);
        if (_logReturns.Count > _windowSize)
            _logReturns.RemoveAt(0);

        if (_logReturns.Count < 60)
            return null;

        var mean = _logReturns.Average();
        var variance = _logReturns.Average(r => (r - mean) * (r - mean));
        var stdev = Math.Sqrt(variance);

        if (Math.Abs(logReturn) >= _entryThreshold * stdev)
        {
            return new Signal
            {
                Direction = logReturn > 0 ? "LONG" : "SHORT",
                ReturnBps = logReturn * 10000,
                StdevBps = stdev * 10000,
                Ratio = Math.Abs(logReturn) / stdev,
                Price = candle.Close
            };
        }

        return null;
    }
}
