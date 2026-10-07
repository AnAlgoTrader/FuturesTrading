using System;
using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class ConsolidationEngine : BarEngine
{
    private readonly int _minBars;
    private readonly double _rangeAtrFactor;
    private readonly int _holdBars;

    private Band? _band;
    private BreakoutOutcome? _outcome;
    private int _barIndex;
    private readonly List<Zone> _zones = new();

    private sealed record Band(long StartTime, int StartIndex, double High, double Low, int Count, double AtrSum);

    private sealed record BreakoutOutcome(bool IsUp, double Level)
    {
        public int BarsOutside { get; set; }
    }

    public override string Name => "Consolidation";
    public override IReadOnlyList<Zone> Zones => _zones;

    public ConsolidationEngine(int minBars = 10, double rangeAtrFactor = 1.5, int holdBars = 3)
    {
        _minBars = Math.Max(3, minBars);
        _rangeAtrFactor = rangeAtrFactor;
        _holdBars = Math.Max(1, holdBars);
    }

    public override void OnBar(BarContext context)
    {
        var candle = context.Candle;
        var atr = context.Atr ?? 0;
        var index = _barIndex++;

        TrackOutcome(candle, atr);

        if (_band is null)
        {
            _band = new Band(candle.Time, index, candle.High, candle.Low, 1, atr);
            return;
        }

        var bandAtr = _band.AtrSum / _band.Count;
        var extendedHigh = Math.Max(_band.High, candle.High);
        var extendedLow = Math.Min(_band.Low, candle.Low);
        var maxRange = Math.Max(bandAtr, atr) * _rangeAtrFactor;

        if (extendedHigh - extendedLow <= maxRange)
        {
            _band = _band with
            {
                High = extendedHigh,
                Low = extendedLow,
                Count = _band.Count + 1,
                AtrSum = _band.AtrSum + atr
            };
            return;
        }

        if (_band.Count >= _minBars)
        {
            var isUp = candle.Close > _band.High;
            var isDown = candle.Close < _band.Low;
            if (isUp || isDown)
            {
                EmitBreakout(isUp, _band, candle.Time);
            }
        }

        _band = new Band(candle.Time, index, candle.High, candle.Low, 1, atr);
    }

    private void TrackOutcome(Candle candle, double atr)
    {
        if (_outcome is null)
            return;

        var outside = _outcome.IsUp
            ? candle.Close >= _outcome.Level
            : candle.Close <= _outcome.Level;

        if (outside)
        {
            _outcome.BarsOutside++;
            if (_outcome.BarsOutside >= _holdBars)
            {
                Emit(Concept.Breakout, _outcome.IsUp ? "Bullish" : "Bearish", candle.Time, "Held");
                _outcome = null;
            }
        }
        else
        {
            Emit(Concept.Breakout, _outcome.IsUp ? "Bullish" : "Bearish", candle.Time, "Failed");
            _outcome = null;
        }
    }

    private void EmitBreakout(bool isUp, Band band, long time)
    {
        Emit(Concept.Breakout, isUp ? "Bullish" : "Bearish", time, "");

        _zones.Add(new Zone
        {
            StartTime = band.StartTime,
            EndTime = time,
            Top = band.High,
            Bottom = band.Low,
            Kind = ZoneKind.Consolidation,
            Direction = isUp ? "Bullish" : "Bearish"
        });

        _outcome = new BreakoutOutcome(isUp, isUp ? band.High : band.Low);
    }
}