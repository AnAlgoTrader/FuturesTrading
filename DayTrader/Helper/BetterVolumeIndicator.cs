using System;
using System.Collections.Generic;

namespace DayTrader.Helper;

public class BetterVolumeIndicator
{
    private const string ClimaxUpColor = "#ff0000";
    private const string ClimaxDownColor = "#ffffff";
    private const string ChurnColor = "#009b32";
    private const string LowVolumeColor = "#ffff00";
    private const string ClimaxChurnColor = "#ff00ff";
    private const string DefaultColor = "#808080";

    private static readonly TimeZoneInfo EasternTime = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private readonly int _lookBack;
    private readonly bool _useTwoBars;
    private readonly List<Candle> _candles = new();

    public BetterVolumeIndicator(int lookBack = 20, bool useTwoBars = true)
    {
        _lookBack = Math.Max(2, lookBack);
        _useTwoBars = useTwoBars;
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

        var signal = _useTwoBars
            ? EvaluateTwoBar(_candles[^1])
            : EvaluateOneBar(_candles[^1]);

        if (signal.Churn && (signal.ClimaxUp || signal.ClimaxDown)) return ClimaxChurnColor;
        if (signal.LowVolume) return LowVolumeColor;
        if (signal.ClimaxUp) return ClimaxUpColor;
        if (signal.ClimaxDown) return ClimaxDownColor;
        if (signal.Churn) return ChurnColor;
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
        var s8 = Divide(buy, range);
        var s9 = Divide(buy - sell, range);
        var s10 = Divide(sell, range);
        var s11 = Divide(sell - buy, range);
        var s12 = Divide(current.Volume, range);

        var lowVolume = current.Volume <= MinPrior(c => c.Volume);
        var climaxUp = current.Close > current.Open && (
            s4 >= MaxPrior(c => c.BuyVolume * c.Range) ||
            s5 >= MaxPrior(c => (c.BuyVolume - c.SellVolume) * c.Range) ||
            s10 <= MinPrior(c => Divide(c.SellVolume, c.Range)) ||
            s11 <= MinPrior(c => Divide(c.SellVolume - c.BuyVolume, c.Range)));
        var climaxDown = current.Close < current.Open && (
            s6 >= MaxPrior(c => c.SellVolume * c.Range) ||
            s7 >= MaxPrior(c => (c.SellVolume - c.BuyVolume) * c.Range) ||
            s8 <= MinPrior(c => Divide(c.BuyVolume, c.Range)) ||
            s9 <= MinPrior(c => Divide(c.BuyVolume - c.SellVolume, c.Range)));
        var churn = s12 >= MaxPrior(c => Divide(c.Volume, c.Range));

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
        var s18 = Divide(buy + prev.BuyVolume, span);
        var s19 = Divide(buy - sell + prev.BuyVolume - prev.SellVolume, span);
        var s20 = Divide(sell + prev.SellVolume, span);
        var s21 = Divide(sell - buy + prev.SellVolume - prev.BuyVolume, span);
        var s22 = Divide(s13, span);

        var lowVolume = s13 <= MinPriorPair((c, p) => c.Volume + p.Volume);
        var climaxUp = current.Close > current.Open && prev.Close > prev.Open && (
            s14 >= MaxPriorPair((c, p) => (c.BuyVolume + p.BuyVolume) * PairSpan(c, p)) ||
            s15 >= MaxPriorPair((c, p) => (c.BuyVolume - c.SellVolume + p.BuyVolume - p.SellVolume) * PairSpan(c, p)) ||
            s20 <= MinPriorPair((c, p) => Divide(c.SellVolume + p.SellVolume, PairSpan(c, p))) ||
            s21 <= MinPriorPair((c, p) => Divide(c.SellVolume - c.BuyVolume + p.SellVolume - p.BuyVolume, PairSpan(c, p))));
        var climaxDown = current.Close < current.Open && prev.Close < prev.Open && (
            s16 >= MaxPriorPair((c, p) => (c.SellVolume + p.SellVolume) * PairSpan(c, p)) ||
            s17 >= MaxPriorPair((c, p) => (c.SellVolume - c.BuyVolume + p.SellVolume - p.BuyVolume) * PairSpan(c, p)) ||
            s18 <= MinPriorPair((c, p) => Divide(c.BuyVolume + p.BuyVolume, PairSpan(c, p))) ||
            s19 <= MinPriorPair((c, p) => Divide(c.BuyVolume - c.SellVolume + p.BuyVolume - p.SellVolume, PairSpan(c, p))));
        var churn = s22 >= MaxPriorPair((c, p) => Divide(c.Volume + p.Volume, PairSpan(c, p)));

        return new CandleSignals(climaxUp, climaxDown, churn, lowVolume);
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
            SessionDate(timeSeconds));

    private static int SessionDate(long timeSeconds)
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