using System;
using System.Collections.Generic;

namespace Trader.Model;

public class ClimaxExhaustionDetector
{
    private const string ClimaxExhaustionColor = "#ff0000";
    private const string DefaultColor = "#808080";

    private static readonly TimeZoneInfo EasternTime = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private readonly int _lookBack;
    private readonly bool _useTwoBars;
    private readonly double _volumeThreshold;
    private readonly List<Candle> _candles = new();

    public ClimaxExhaustionDetector(int lookBack = 20, bool useTwoBars = true, double volumeThreshold = 1.0)
    {
        _lookBack = Math.Max(2, lookBack);
        _useTwoBars = useTwoBars;
        _volumeThreshold = Math.Max(0.0, volumeThreshold);
    }

    /// <summary>Feeds (or updates, by minute) a candle and returns the color for it.</summary>
    public string ProcessCandle(long timeSeconds, double open, double high, double low, double close, double volume)
    {
        if (volume <= 0) return DefaultColor;

        if (_candles.Count == 0 || _candles[^1].TimeSeconds != timeSeconds)
        {
            _candles.Add(MakeCandle(timeSeconds, open, high, low, close, volume));
            while (_candles.Count > _lookBack)
                _candles.RemoveAt(0);
        }
        else
        {
            _candles[^1] = MakeCandle(timeSeconds, open, high, low, close, volume);
        }

        if (_candles.Count < _lookBack) return DefaultColor;

        var signal = EvaluateOneBar(_candles[^1]);
        if (_useTwoBars && _candles.Count >= 2)
        {
            var twoBar = EvaluateTwoBar(_candles[^1]);
            signal = new CandleSignals(
                signal.ClimaxUp || twoBar.ClimaxUp,
                signal.ClimaxDown || twoBar.ClimaxDown,
                signal.Churn || twoBar.Churn,
                signal.LowVolume || twoBar.LowVolume);
        }

        if (signal.ClimaxUp) return ClimaxExhaustionColor;
        return DefaultColor;
    }

    private CandleSignals EvaluateOneBar(Candle current)
    {
        var range = current.Range;
        var buy = current.BuyVolume;
        var sell = current.SellVolume;

        var s4 = buy * range;
        var s5 = (buy - sell) * range;
        var s6 = sell * range;
        var s7 = (sell - buy) * range;
        var s12 = Divide(current.Volume, range);

        var avgVolume = AveragePrior(c => c.Volume);
        var isAboveAvgVol = current.Volume >= avgVolume * _volumeThreshold;

        var lowVolume = current.Volume <= MinPrior(c => c.Volume);
        var climaxUp = current.Close > current.Open && isAboveAvgVol && (
            s4 >= MaxPrior(c => c.BuyVolume * c.Range) ||
            s5 >= MaxPrior(c => (c.BuyVolume - c.SellVolume) * c.Range));
        var climaxDown = current.Close < current.Open && isAboveAvgVol && (
            s6 >= MaxPrior(c => c.SellVolume * c.Range) ||
            s7 >= MaxPrior(c => (c.SellVolume - c.BuyVolume) * c.Range));
        var churn = isAboveAvgVol && s12 >= MaxPrior(c => Divide(c.Volume, c.Range));

        return new CandleSignals(climaxUp, climaxDown, churn, lowVolume);
    }

    private CandleSignals EvaluateTwoBar(Candle current)
    {
        var prev = _candles[^2];

        if (current.SessionDate != prev.SessionDate)
            return new CandleSignals(false, false, false, false);

        var buy = current.BuyVolume;
        var sell = current.SellVolume;

        var s13 = current.Volume + prev.Volume;
        var span = Math.Max(current.High, prev.High) - Math.Min(current.Low, prev.Low);

        var s14 = (buy + prev.BuyVolume) * span;
        var s15 = (buy - sell + prev.BuyVolume - prev.SellVolume) * span;
        var s16 = (sell + prev.SellVolume) * span;
        var s17 = (sell - buy + prev.SellVolume - prev.BuyVolume) * span;
        var s22 = Divide(s13, span);

        var avgPairVol = AveragePriorPair((c, p) => c.Volume + p.Volume);
        var isAboveAvgVol = s13 >= avgPairVol * _volumeThreshold;

        var lowVolume = s13 <= MinPriorPair((c, p) => c.Volume + p.Volume);
        var climaxUp = current.Close > current.Open && prev.Close > prev.Open && isAboveAvgVol && (
            s14 >= MaxPriorPair((c, p) => (c.BuyVolume + p.BuyVolume) * PairSpan(c, p)) ||
            s15 >= MaxPriorPair((c, p) => (c.BuyVolume - c.SellVolume + p.BuyVolume - p.SellVolume) * PairSpan(c, p)));
        var climaxDown = current.Close < current.Open && prev.Close < prev.Open && isAboveAvgVol && (
            s16 >= MaxPriorPair((c, p) => (c.SellVolume + p.SellVolume) * PairSpan(c, p)) ||
            s17 >= MaxPriorPair((c, p) => (c.SellVolume - c.BuyVolume + p.SellVolume - p.BuyVolume) * PairSpan(c, p)));
        var churn = isAboveAvgVol && s22 >= MaxPriorPair((c, p) => Divide(c.Volume + p.Volume, PairSpan(c, p)));

        return new CandleSignals(climaxUp, climaxDown, churn, lowVolume);
    }

    private double AveragePrior(Func<Candle, double> selector)
    {
        if (_candles.Count <= 1) return 0;
        double sum = 0;
        int count = 0;
        for (var i = 0; i < _candles.Count - 1; i++)
        {
            var value = selector(_candles[i]);
            if (!double.IsNaN(value))
            {
                sum += value;
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    private double AveragePriorPair(Func<Candle, Candle, double> selector)
    {
        if (_candles.Count <= 2) return 0;
        double sum = 0;
        int count = 0;
        for (var i = 1; i < _candles.Count - 1; i++)
        {
            var value = selector(_candles[i], _candles[i - 1]);
            if (!double.IsNaN(value))
            {
                sum += value;
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    private double MaxPrior(Func<Candle, double> selector)
    {
        var max = double.NegativeInfinity;
        for (var i = 0; i < _candles.Count - 1; i++)
        {
            var value = selector(_candles[i]);
            if (!double.IsNaN(value) && value > max) max = value;
        }
        return max;
    }

    private double MinPrior(Func<Candle, double> selector)
    {
        var min = double.PositiveInfinity;
        for (var i = 0; i < _candles.Count - 1; i++)
        {
            var value = selector(_candles[i]);
            if (!double.IsNaN(value) && value < min) min = value;
        }
        return min;
    }

    private double MaxPriorPair(Func<Candle, Candle, double> selector)
    {
        var max = double.NegativeInfinity;
        for (var i = 1; i < _candles.Count - 1; i++)
        {
            var value = selector(_candles[i], _candles[i - 1]);
            if (!double.IsNaN(value) && value > max) max = value;
        }
        return max;
    }

    private double MinPriorPair(Func<Candle, Candle, double> selector)
    {
        var min = double.PositiveInfinity;
        for (var i = 1; i < _candles.Count - 1; i++)
        {
            var value = selector(_candles[i], _candles[i - 1]);
            if (!double.IsNaN(value) && value < min) min = value;
        }
        return min;
    }

    private static Candle MakeCandle(long timeSeconds, double open, double high, double low, double close, double volume)
        => new(timeSeconds, open, high, low, close, volume, SplitBuyVolume(open, high, low, close, volume),
            GetSessionDate(timeSeconds));

    private static int GetSessionDate(long timeSeconds)
    {
        var et = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(timeSeconds), EasternTime);
        return et.Year * 10000 + et.Month * 100 + et.Day;
    }

    private static double SplitBuyVolume(double open, double high, double low, double close, double volume)
    {
        var range = high - low;
        if (close > open)
            return volume * (range / (2 * range + open - close));
        if (close < open)
            return volume * ((range + close - open) / (2 * range + close - open));
        return 0.5 * volume;
    }

    private static double Divide(double numerator, double denominator)
        => denominator != 0 ? numerator / denominator : double.NaN;

    private static double PairSpan(Candle candle, Candle prevCandle)
        => Math.Max(candle.High, prevCandle.High) - Math.Min(candle.Low, prevCandle.Low);

    private readonly record struct Candle(
        long TimeSeconds, double Open, double High, double Low, double Close, double Volume, double BuyVolume,
        int SessionDate)
    {
        public double SellVolume => Volume - BuyVolume;
        public double Range => High - Low;
    }

    private readonly record struct CandleSignals(
        bool ClimaxUp, bool ClimaxDown, bool Churn, bool LowVolume);
}