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

    public bool Ready => _logReturns.Count >= 60;

    public void Seed(IEnumerable<double> closes)
    {
        foreach (var close in closes)
            TryAddClose(close, out _);
    }

    public Signal OnCandleClosed(Candle candle)
    {
        if (!TryAddClose(candle.Close, out var logReturn))
            return null;

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

    private bool TryAddClose(double close, out double logReturn)
    {
        logReturn = 0;

        if (!_hasPreviousClose)
        {
            if (close <= 0)
                return false;
            _previousClose = close;
            _hasPreviousClose = true;
            return false;
        }

        if (close <= 0)
            return false;

        logReturn = Math.Log(close / _previousClose);
        _previousClose = close;

        _logReturns.Add(logReturn);
        if (_logReturns.Count > _windowSize)
            _logReturns.RemoveAt(0);

        return true;
    }
}
