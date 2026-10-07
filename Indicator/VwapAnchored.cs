using System;

namespace Indicator;

/// <summary>
/// Anchored VWAP (spec module D): VWAP = Sum(TypicalPrice * Volume) / Sum(Volume),
/// reset at the start of each RTH session, with population standard-deviation bands.
/// </summary>
internal sealed class VwapAnchored
{
    private DateOnly? _session;
    private double _sumVol;
    private double _sumPV;
    private double _sumPV2;

    public DateOnly? Session => _session;

    public void Clear()
    {
        _session = null;
        _sumVol = 0;
        _sumPV = 0;
        _sumPV2 = 0;
    }

    public void Reset(DateOnly session)
    {
        _session = session;
        _sumVol = 0;
        _sumPV = 0;
        _sumPV2 = 0;
    }

    public void Add(double typicalPrice, double volume)
    {
        if (volume <= 0 || typicalPrice <= 0) return;
        _sumVol += volume;
        _sumPV += typicalPrice * volume;
        _sumPV2 += typicalPrice * typicalPrice * volume;
    }

    public (double Vwap, double? Upper1, double? Upper2, double? Lower1, double? Lower2) Snapshot()
        => Compute(_sumVol, _sumPV, _sumPV2);

    public (double Vwap, double? Upper1, double? Upper2, double? Lower1, double? Lower2) SnapshotWith(
        double typicalPrice, double volume)
    {
        double vol = _sumVol, pv = _sumPV, pv2 = _sumPV2;
        if (volume > 0 && typicalPrice > 0)
        {
            vol += volume;
            pv += typicalPrice * volume;
            pv2 += typicalPrice * typicalPrice * volume;
        }

        return Compute(vol, pv, pv2);
    }

    public static double TypicalPrice(Candle candle) => (candle.High + candle.Low + candle.Close) / 3.0;

    internal static (double, double?, double?, double?, double?) Compute(double sumVol, double sumPV, double sumPV2)
    {
        if (sumVol <= 0) return (0, null, null, null, null);

        var vwap = sumPV / sumVol;
        var variance = Math.Max(0, sumPV2 / sumVol - vwap * vwap);
        var stdDev = Math.Sqrt(variance);
        return (vwap, vwap + stdDev, vwap + 2 * stdDev, vwap - stdDev, vwap - 2 * stdDev);
    }
}
