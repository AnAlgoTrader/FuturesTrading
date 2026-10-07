using System;
using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class EmaCalculator
{
    private readonly double _multiplier;
    private bool _hasValue;
    private double _value;

    public EmaCalculator(int period)
    {
        _multiplier = 2.0 / (Math.Max(1, period) + 1);
    }

    public bool HasValue => _hasValue;
    public double? Value => _hasValue ? _value : null;

    public double Next(double value)
    {
        if (!_hasValue)
        {
            _value = value;
            _hasValue = true;
        }
        else
        {
            _value = (value - _value) * _multiplier + _value;
        }
        return _value;
    }
}

public class SmaCalculator
{
    private readonly int _period;
    private readonly Queue<double> _window = new();
    private double _sum;

    public SmaCalculator(int period)
    {
        _period = Math.Max(1, period);
    }

    public bool HasValue => _window.Count == _period;
    public double? Value => HasValue ? _sum / _period : null;

    public double? Next(double value)
    {
        _window.Enqueue(value);
        _sum += value;
        if (_window.Count > _period)
            _sum -= _window.Dequeue();
        return Value;
    }
}

public class AtrCalculator
{
    private readonly int _period;
    private double _prevClose;
    private double _sumTr;
    private int _seedCount;
    private bool _valueSet;
    private double _value;

    public AtrCalculator(int period)
    {
        _period = Math.Max(1, period);
    }

    public bool HasValue => _valueSet;
    public double? Value => _valueSet ? _value : null;

    public double? Next(Candle candle)
    {
        var tr = _prevClose == 0
            ? candle.High - candle.Low
            : Math.Max(candle.High - candle.Low,
                Math.Max(Math.Abs(candle.High - _prevClose), Math.Abs(candle.Low - _prevClose)));

        _prevClose = candle.Close;

        if (_seedCount < _period)
        {
            _sumTr += tr;
            _seedCount++;
            if (_seedCount == _period)
            {
                _value = _sumTr / _period;
                _valueSet = true;
            }
        }
        else if (_valueSet)
        {
            _value = (_value * (_period - 1) + tr) / _period;
        }

        return Value;
    }
}

public class RsiCalculator
{
    private readonly int _period;
    private double _prevClose;
    private double _avgGain;
    private double _avgLoss;
    private int _seedCount;
    private bool _valueSet;
    private double _value;

    public RsiCalculator(int period)
    {
        _period = Math.Max(1, period);
    }

    public bool HasValue => _valueSet;
    public double? Value => _valueSet ? _value : null;

    public double? Next(Candle candle)
    {
        if (_prevClose == 0)
        {
            _prevClose = candle.Close;
            return Value;
        }

        var change = candle.Close - _prevClose;
        _prevClose = candle.Close;
        var gain = Math.Max(0, change);
        var loss = Math.Max(0, -change);

        if (_seedCount < _period)
        {
            _avgGain += gain;
            _avgLoss += loss;
            _seedCount++;
            if (_seedCount == _period)
            {
                _avgGain /= _period;
                _avgLoss /= _period;
                _valueSet = true;
            }
        }
        else
        {
            _avgGain = (_avgGain * (_period - 1) + gain) / _period;
            _avgLoss = (_avgLoss * (_period - 1) + loss) / _period;
        }

        if (_valueSet)
        {
            _value = _avgLoss == 0 ? 100 : 100 - 100 / (1 + _avgGain / _avgLoss);
        }

        return Value;
    }
}