using System;
using System.Collections.Generic;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;

namespace IndicatorBuild.Streaming;

/// <summary>
/// Incremental counterpart of <see cref="ConfluenceScorer"/>. Live mode cannot know a zone
/// before its engine created it, so zones enter the "significant level" set at creation
/// (never earlier) and swings only after confirmation — the honest no-lookahead version of
/// the batch scorer. Same 4 factors and IndicatorConfig constants, so scores converge to the
/// batch ones as soon as no still-forming zones would have influenced an earlier bar.
/// </summary>
public sealed class StreamingConfluence
{
    private readonly int _gate;
    private readonly EmaCalculator _ema;
    private readonly AtrCalculator _atr;
    private readonly SmaCalculator _avgVol;
    private readonly List<(long End, double Top, double Bottom)> _activeZones = new();
    private readonly List<SwingPoint> _swings = new();

    public StreamingConfluence(int gate = IndicatorConfig.ConfluenceGate)
    {
        _gate = gate;
        _ema = new EmaCalculator(IndicatorConfig.ConfluenceEmaPeriod);
        _atr = new AtrCalculator(IndicatorConfig.AtrPeriod);
        _avgVol = new SmaCalculator(IndicatorConfig.VolumeAveragePeriod);
    }

    /// <summary>Scores the events on this bar right after the pipeline processed it.</summary>
    public void ProcessBar(
        Candle candle,
        CandleAnalysis analysis,
        IReadOnlyList<Zone> newZones,
        IReadOnlyList<SwingPoint> newSwings,
        int barIndex)
    {
        var emaValue = _ema.Next(candle.Close);
        var atrValue = _atr.Next(candle);
        var avg = _avgVol.Next(candle.Volume);

        foreach (var zone in newZones)
        {
            if (zone.EndTime >= 0 && zone.StartTime >= 0 && zone.Top > zone.Bottom)
                _activeZones.Add((zone.EndTime, zone.Top, zone.Bottom));
        }
        _activeZones.RemoveAll(z => z.End < candle.Time);

        if (newSwings is not null)
        {
            _swings.AddRange(newSwings);
            // bar-index window: resolution-independent (unlike a time window on gapped/daily bars)
            _swings.RemoveAll(s => s.Index < barIndex - IndicatorConfig.ConfluenceSwingWindowBars);
        }

        var tol = atrValue is { } av && av > 0 ? IndicatorConfig.ConfluenceLevelToleranceAtr * av : 0;

        foreach (var evt in analysis.Events)
        {
            evt.Score = 0;
            if (!ConfluenceScorer.ColoredGated.Contains(evt.Type)) continue;
            if (evt.Direction != "Bullish" && evt.Direction != "Bearish")
            {
                evt.Qualified = false;
                continue;
            }

            var dir = evt.Direction == "Bullish" ? 1 : -1;
            var score = 0;

            if (emaValue is { } e && dir * (candle.Close - e) > 0) score++;
            if (avg is { } a && a > 0 && candle.Volume >= IndicatorConfig.ConfluenceVolumeFactor * a) score++;
            if (atrValue is { } at && at > 0 && (candle.High - candle.Low) >= IndicatorConfig.ConfluenceDisplaceAtr * at) score++;
            if (tol > 0 && NearLevel(candle.Close, tol)) score++;

            evt.Score = score;
            evt.Qualified = score >= _gate;
        }
    }

    private bool NearLevel(double close, double tol)
    {
        foreach (var z in _activeZones)
        {
            if (Math.Abs(close - z.Top) <= tol || Math.Abs(close - z.Bottom) <= tol)
                return true;
        }

        for (var k = _swings.Count - 1; k >= 0; k--)
        {
            if (Math.Abs(close - _swings[k].Price) <= tol)
                return true;
        }

        return false;
    }
}