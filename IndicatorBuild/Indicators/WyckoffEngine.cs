using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

/// <summary>
/// Wyckoff accumulation/distribution range detection.
/// A range is typed from the prior-trend context (down-swing before the band => accumulation,
/// up-swing => distribution). The recipe labels SC/BC, AR, ST, Spring/UTAD, SOS/SOW, LPS/LPSY
/// and the Phase E breakout as price moves through the range — swing-based labels use their own
/// swing time (historical, non-repainting; confirmed data only).
/// </summary>
public class WyckoffEngine : BarEngine
{
    private readonly int _minSwings;
    private readonly int _minBars;
    private readonly double _minHeightAtr;
    private readonly double _maxHeightAtr;
    private readonly double _priorTrendAtr;
    private readonly double _sosVolumeFactor;
    private readonly double _sosDisplacementAtr;
    private const int PriorSwings = 6;

    private readonly List<SwingPoint> _recentSwings = new();
    private readonly List<SwingPoint> _overflow = new();
    private readonly List<Zone> _zones = new();
    private readonly List<ConceptEvent> _pendingEvents = new();
    private Band? _band;
    private int _stage;             // 0=Extremum 1=Reaction 2=Test 3=Strength 4=LastSupport 5=PhaseE
    private int _dir;               // +1 accumulation, -1 distribution, 0 untyped
    private double _extreme;        // SC low (accum) / BC high (dist)
    private double _sosLevel;       // breakout level after SOS/SOW
    private double? _piercePrice;
    private long? _pierceTime;

    public override string Name => "Wyckoff";
    public override IReadOnlyList<Zone> Zones => _zones;

    public WyckoffEngine(
        int minSwings = IndicatorConfig.WyckoffRangeMinSwings,
        int minBars = IndicatorConfig.WyckoffRangeMinBars,
        double minHeightAtr = IndicatorConfig.WyckoffRangeMinHeightAtr,
        double maxHeightAtr = IndicatorConfig.WyckoffRangeMaxHeightAtr,
        double priorTrendAtr = IndicatorConfig.WyckoffPriorTrendAtr,
        double sosVolumeFactor = IndicatorConfig.WyckoffSosVolumeFactor,
        double sosDisplacementAtr = IndicatorConfig.WyckoffSosDisplacementAtr)
    {
        _minSwings = minSwings;
        _minBars = minBars;
        _minHeightAtr = minHeightAtr;
        _maxHeightAtr = maxHeightAtr;
        _priorTrendAtr = priorTrendAtr;
        _sosVolumeFactor = sosVolumeFactor;
        _sosDisplacementAtr = sosDisplacementAtr;
    }

    public override void OnBar(BarContext context)
    {
        var candle = context.Candle;
        var atr = context.Atr ?? 0;
        var avgVol = context.AvgVolume ?? 0;

        if (atr <= 0) return;

        var preBandHigh = _band?.High;
        var preBandLow = _band?.Low;

        foreach (var swing in context.NewSwings)
        {
            AddToBand(swing, atr);
            HandleSwing(swing);
        }

        foreach (var swing in context.NewSwings)
            AddRecent(swing);

        HandleBars(candle, atr, avgVol, preBandHigh, preBandLow);
    }

    private void AddRecent(SwingPoint swing)
    {
        _recentSwings.Add(swing);
        if (_recentSwings.Count > 16)
            _recentSwings.RemoveRange(0, _recentSwings.Count - 16);
    }

    private void AddToBand(SwingPoint swing, double atr)
    {
        if (_band is null)
        {
            _dir = ClassifyPriorTrend(swing, atr);
            if (_dir == 0) return; // no prior-trend context => do not start a range

            _band = new Band
            {
                StartTime = swing.Time,
                LastTime = swing.Time,
                FirstIndex = swing.Index,
                LastIndex = swing.Index,
                Atr = atr,
                SwingCount = 1,
                FirstSwingIsHigh = swing.IsHigh
            };
            if (swing.IsHigh) _band.High = swing.Price;
            else _band.Low = swing.Price;

            _stage = 0;
            Queue(_dir == 1 ? Concept.Accumulation : Concept.Distribution,
                _dir == 1 ? "Bullish" : "Bearish", swing.Time, "PhaseStart");
            return;
        }

        var nextHigh = swing.IsHigh
            ? (_band.High.HasValue ? System.Math.Max(_band.High.Value, swing.Price) : swing.Price)
            : _band.High;
        var nextLow = swing.IsHigh
            ? _band.Low
            : (_band.Low.HasValue ? System.Math.Min(_band.Low.Value, swing.Price) : swing.Price);

        var height = nextHigh.HasValue && nextLow.HasValue ? nextHigh.Value - nextLow.Value : 0;

        // Poke tolerance: a swing can overshoot the band by up to 2x the range limit
        // (SOS pushes, UT/spring pokes) without ending the range.
        if (height <= _maxHeightAtr * atr * 2)
        {
            _band.High = nextHigh;
            _band.Low = nextLow;
            _band.LastIndex = swing.Index;
            _band.LastTime = swing.Time;
            _band.SwingCount++;
            _overflow.Clear();
            return;
        }

        // A decisive overshoot of a typed band keeps the overshoot in a pending buffer for
        // Phase E / reset resolution.
        _overflow.Add(swing);
    }

    private void HandleSwing(SwingPoint swing)
    {
        if (_band is null || _dir == 0) return;

        switch (_stage)
        {
            case 0: // first swing of the band sets the extreme
                if (_dir == 1)
                {
                    if (_band.FirstSwingIsHigh)
                    {
                        Queue(Concept.AutomaticRally, "Bullish", _band.StartTime, "AR-first");
                        _extreme = LastPrePrice(true) ?? _band.Low ?? 0;
                        _stage = 2;
                    }
                    else
                    {
                        Queue(Concept.SellingClimax, "Bearish", _band.StartTime);
                        _extreme = _band.Low ?? 0;
                        _stage = 1;
                    }
                }
                else
                {
                    if (!_band.FirstSwingIsHigh)
                    {
                        Queue(Concept.AutomaticRally, "Bearish", _band.StartTime, "AR-first");
                        _extreme = LastPrePrice(false) ?? _band.High ?? 0;
                        _stage = 2;
                    }
                    else
                    {
                        Queue(Concept.BuyingClimax, "Bullish", _band.StartTime);
                        _extreme = _band.High ?? 0;
                        _stage = 1;
                    }
                }
                break;

            case 1: // Automatic Rally / Reaction after the climax
                if ((_dir == 1 && swing.IsHigh) || (_dir == -1 && !swing.IsHigh))
                {
                    Queue(Concept.AutomaticRally, _dir == 1 ? "Bullish" : "Bearish", swing.Time);
                    _stage = 2;
                }
                break;

            case 2: // Secondary Test or Spring/UTAD pierce
                if (_dir == 1 && !swing.IsHigh)
                {
                    if (swing.Price > _extreme)
                    {
                        Queue(Concept.SecondaryTest, "Bullish", swing.Time);
                        _stage = 3;
                    }
                    else
                    {
                        _piercePrice = swing.Price;
                        _pierceTime = swing.Time;
                    }
                }
                else if (_dir == 1 && swing.IsHigh && _piercePrice is not null && swing.Price > _extreme)
                {
                    Queue(Concept.Spring, "Bullish", _pierceTime!.Value);
                    _piercePrice = null;
                    _pierceTime = null;
                    _stage = 3;
                }
                else if (_dir == -1 && swing.IsHigh)
                {
                    if (swing.Price < _extreme)
                    {
                        Queue(Concept.SecondaryTest, "Bearish", swing.Time);
                        _stage = 3;
                    }
                    else
                    {
                        _piercePrice = swing.Price;
                        _pierceTime = swing.Time;
                    }
                }
                else if (_dir == -1 && !swing.IsHigh && _piercePrice is not null && swing.Price < _extreme)
                {
                    Queue(Concept.UpthrustAfterDistribution, "Bearish", _pierceTime!.Value);
                    _piercePrice = null;
                    _pierceTime = null;
                    _stage = 3;
                }
                break;

            case 4: // Last Point of Support (accum) / Last Point of Supply (dist)
                if (_dir == 1 && !swing.IsHigh && swing.Price >= _sosLevel)
                {
                    Queue(Concept.LastPointOfSupport, "Bullish", swing.Time);
                    _stage = 5;
                }
                else if (_dir == -1 && swing.IsHigh && swing.Price <= _sosLevel)
                {
                    Queue(Concept.LastPointOfSupply, "Bearish", swing.Time);
                    _stage = 5;
                }
                break;
        }
    }

    private void HandleBars(Candle candle, double atr, double avgVol, double? preBandHigh, double? preBandLow)
    {
        if (_band is null || _dir == 0) return;

        var breakHigh = preBandHigh ?? _band.High ?? double.PositiveInfinity;
        var breakLow = preBandLow ?? _band.Low ?? double.NegativeInfinity;

        if (_overflow.Count > 0)
        {
            if (_dir == 1 && candle.Close > breakHigh)
            {
                EndRange(candle.Time, true);
                return;
            }
            if (_dir == -1 && candle.Close < breakLow)
            {
                EndRange(candle.Time, false);
                return;
            }
            if (_overflow.Count >= 2)
            {
                // Two consecutive decisive poke-outs: the band was violated before an orderly
                // Phase E. Discard it (no zone / no labels) and restart on the newest overshoot.
                var seed = _overflow[^1];
                _overflow.Clear();
                _pendingEvents.Clear();
                _band = null;
                ResetStage();
                AddToBand(seed, atr);
                return;
            }
        }

        var displaced = (candle.High - candle.Low) >= _sosDisplacementAtr * atr;
        var volConfirmed = avgVol > 0 && candle.Volume >= _sosVolumeFactor * avgVol;

        if (_stage == 3)
        {
            if (_dir == 1 && candle.Close > breakHigh && (volConfirmed || displaced))
            {
                Queue(Concept.SignOfStrength, "Bullish", candle.Time);
                _sosLevel = breakHigh;
                _stage = 4;
                return;
            }
            if (_dir == -1 && candle.Close < breakLow && (volConfirmed || displaced))
            {
                Queue(Concept.SignOfWeakness, "Bearish", candle.Time);
                _sosLevel = breakLow;
                _stage = 4;
                return;
            }
        }

        if (_stage == 5)
        {
            if (_dir == 1 && candle.Close > breakHigh)
                EndRange(candle.Time, true);
            else if (_dir == -1 && candle.Close < breakLow)
                EndRange(candle.Time, false);
        }
    }

    private void Queue(Concept type, string direction, long time, string detail = "")
    {
        _pendingEvents.Add(new ConceptEvent { Time = time, Type = type, Direction = direction, Detail = detail });
    }

    private void EndRange(long endTime, bool isAccumulation)
    {
        if (Qualified(_band!))
        {
            if (isAccumulation)
            {
                Queue(Concept.AccumulationBreakout, "Bullish", endTime);
                Queue(Concept.Markup, "Bullish", endTime);
            }
            else
            {
                Queue(Concept.DistributionBreakout, "Bearish", endTime);
                Queue(Concept.Markdown, "Bearish", endTime);
            }
        }

        CloseBand(endTime);
    }

    private void CloseBand(long endTime)
    {
        if (_band is not null && _dir != 0 && Qualified(_band))
        {
            _zones.Add(new Zone
            {
                StartTime = _band.StartTime,
                EndTime = endTime,
                Top = _band.High ?? _band.Low ?? 0,
                Bottom = _band.Low ?? _band.High ?? 0,
                Kind = _dir == 1 ? ZoneKind.AccumulationRange : ZoneKind.DistributionRange,
                Direction = _dir == 1 ? "Bullish" : "Bearish"
            });

            // Wyckoff labels are only revealed once the range has been confirmed end-to-end;
            // markers still render at their original (historical) event times.
            foreach (var evt in _pendingEvents)
                Emit(evt.Type, evt.Direction, evt.Time, evt.Detail);
        }

        _pendingEvents.Clear();
        _band = null;
        ResetStage();
    }

    private void ResetStage()
    {
        _stage = 0;
        _dir = 0;
        _piercePrice = null;
        _pierceTime = null;
        _overflow.Clear();
    }

    private bool Qualified(Band b) =>
        b.SwingCount >= _minSwings &&
        (b.LastIndex - b.FirstIndex) >= _minBars &&
        b.High.HasValue && b.Low.HasValue &&
        (b.High.Value - b.Low.Value) >= _minHeightAtr * b.Atr;

    /// <summary>Direction of the market just before the band: recent drift in price.</summary>
    private int ClassifyPriorTrend(SwingPoint swing, double atr)
    {
        var pre = _recentSwings.Where(s => s.Index < swing.Index).TakeLast(6).ToList();
        if (pre.Count < PriorSwings) return 0;

        var drift = pre[^1].Price - pre[0].Price;
        if (drift <= -_priorTrendAtr * atr) return 1;
        if (drift >= _priorTrendAtr * atr) return -1;
        return 0;
    }

    private double? LastPrePrice(bool wantLow)
    {
        if (_band is null) return null;
        var pre = _recentSwings.Where(s => s.Index < _band.FirstIndex);
        var list = wantLow ? pre.Where(s => !s.IsHigh).ToList() : pre.Where(s => s.IsHigh).ToList();
        return list.Count == 0 ? null : list[^1].Price;
    }

    private sealed class Band
    {
        public long StartTime;
        public long LastTime;
        public int FirstIndex;
        public int LastIndex;
        public double? High;
        public double? Low;
        public double Atr;
        public int SwingCount;
        public bool FirstSwingIsHigh;
    }
}