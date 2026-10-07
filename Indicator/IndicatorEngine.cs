using System;
using System.Collections.Generic;

namespace Indicator;

/// <summary>
/// Stateful engine implementing the ES OHLCV indicator specification.
/// Feed candles chronologically with <see cref="ProcessCandle"/> (commits them to history);
/// use <see cref="EvaluateForming"/> to preview a still-forming candle without mutating history.
/// All public members are thread-safe.
/// </summary>
public sealed class IndicatorEngine
{
    private readonly IndicatorOptions _options;
    private readonly object _lock = new();
    private readonly SwingDetector _swings;
    private readonly List<Candle> _bars = new();
    private readonly Dictionary<DateOnly, SessionAgg> _sessions = new();
    private readonly VwapAnchored _vwap = new();
    private readonly int _maxBars;

    private DateOnly? _levelsDate;
    private SessionLevels _levels = new();

    public IndicatorEngine(IndicatorOptions? options = null)
    {
        _options = options ?? new IndicatorOptions();
        _swings = new SwingDetector(_options.PivotLeft, _options.PivotRight);
        _maxBars = Math.Max(_options.MinHistoryBars, _options.PivotLeft + _options.PivotRight + 1) + 50;
    }

    /// <summary>
    /// Commits a candle to history and returns the result computed against the state that preceded it.
    /// Candle times must be strictly increasing.
    /// </summary>
    public IndicatorResult ProcessCandle(Candle candle)
    {
        Validate(candle);

        lock (_lock)
        {
            if (_bars.Count > 0 && candle.Time <= _bars[^1].Time)
                throw new ArgumentException(
                    $"Candle time {candle.Time} must be greater than the last committed time {_bars[^1].Time}.");

            var (local, sessionDate, isRth, isEth, isIb) = Classify(candle);
            EnsureLevels(sessionDate);

            var signal = EvaluateSignal(candle, sessionDate);
            var vwap = CommitVwap(candle, sessionDate, isRth);

            ApplySession(candle, sessionDate, isRth, isEth, isIb);
            _levels = ApplyBreaks(RefreshRunning(_levels, sessionDate, null, false, false), candle, local);

            _bars.Add(candle);
            TrimBars();
            _swings.Rebuild(_bars);
            PruneSessions(sessionDate);

            return Build(signal, vwap, _levels);
        }
    }

    /// <summary>
    /// Evaluates a still-forming candle without mutating any candle-derived state.
    /// (Session rollover is idempotent and depends only on the candle's session date.)
    /// </summary>
    public IndicatorResult EvaluateForming(Candle candle)
    {
        Validate(candle);

        lock (_lock)
        {
            if (_bars.Count > 0 && candle.Time < _bars[^1].Time)
                throw new ArgumentException(
                    $"Candle time {candle.Time} must not be older than the last committed time {_bars[^1].Time}.");

            var (local, sessionDate, isRth, isEth, isIb) = Classify(candle);
            EnsureLevels(sessionDate);

            var signal = EvaluateSignal(candle, sessionDate);
            var vwap = FormingVwap(candle, sessionDate, isRth);
            var levels = ApplyBreaks(RefreshRunning(_levels, sessionDate, candle, isEth, isIb), candle, local);

            return Build(signal, vwap, levels);
        }
    }

    /// <summary>Clears all accumulated state.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _bars.Clear();
            _sessions.Clear();
            _swings.Rebuild(_bars);
            _vwap.Clear();
            _levelsDate = null;
            _levels = new SessionLevels();
        }
    }

    private SignalType EvaluateSignal(Candle candle, DateOnly sessionDate)
    {
        if (!GetStats(candle.Time, out var volumeSma, out var averageSpread))
            return SignalType.None;

        GetSessionExtreme(sessionDate, candle, out var sessionHigh, out var sessionLow);
        return SignalDetector.Evaluate(candle, volumeSma, averageSpread, sessionHigh, sessionLow,
            _levels, _swings.Swings, _options);
    }

    private bool GetStats(long excludeTime, out double volumeSma, out double averageSpread)
    {
        var count = _bars.Count;
        if (count > 0 && _bars[count - 1].Time == excludeTime) count--;

        if (count < _options.MinHistoryBars)
        {
            volumeSma = 0;
            averageSpread = 0;
            return false;
        }

        var length = Math.Min(_options.VolumeSmaLength, count);
        var start = count - length;
        double sumVolume = 0;
        double sumSpread = 0;
        for (var i = start; i < count; i++)
        {
            sumVolume += _bars[i].Volume;
            sumSpread += _bars[i].High - _bars[i].Low;
        }

        volumeSma = sumVolume / length;
        averageSpread = sumSpread / length;
        return volumeSma > 0 && averageSpread > 0;
    }

    private void GetSessionExtreme(DateOnly sessionDate, Candle candle, out double high, out double low)
    {
        if (_sessions.TryGetValue(sessionDate, out var agg) && agg.HasSession)
        {
            high = Math.Max(agg.SessionHigh, candle.High);
            low = Math.Min(agg.SessionLow, candle.Low);
        }
        else
        {
            high = candle.High;
            low = candle.Low;
        }
    }

    private (double Vwap, double? Upper1, double? Upper2, double? Lower1, double? Lower2) CommitVwap(
        Candle candle, DateOnly sessionDate, bool isRth)
    {
        if (!isRth) return _vwap.Snapshot();

        if (_vwap.Session != sessionDate) _vwap.Reset(sessionDate);
        _vwap.Add(VwapAnchored.TypicalPrice(candle), candle.Volume);
        return _vwap.Snapshot();
    }

    private (double Vwap, double? Upper1, double? Upper2, double? Lower1, double? Lower2) FormingVwap(
        Candle candle, DateOnly sessionDate, bool isRth)
    {
        if (!isRth) return _vwap.Snapshot();
        if (_vwap.Session == sessionDate) return _vwap.SnapshotWith(VwapAnchored.TypicalPrice(candle), candle.Volume);

        double volume = 0;
        double pv = 0;
        double pv2 = 0;
        var typical = VwapAnchored.TypicalPrice(candle);
        if (candle.Volume > 0 && typical > 0)
        {
            volume = candle.Volume;
            pv = typical * volume;
            pv2 = typical * typical * volume;
        }

        return VwapAnchored.Compute(volume, pv, pv2);
    }

    private void EnsureLevels(DateOnly sessionDate)
    {
        if (_levelsDate == sessionDate) return;

        _levelsDate = sessionDate;
        _sessions.TryGetValue(sessionDate.AddDays(-1), out var previous);
        _levels = new SessionLevels
        {
            SessionDate = sessionDate,
            PreviousDayHigh = previous.HasRth ? previous.RthHigh : null,
            PreviousDayLow = previous.HasRth ? previous.RthLow : null,
            PreviousDayClose = previous.HasRth ? previous.RthClose : null
        };
    }

    private SessionLevels RefreshRunning(SessionLevels levels, DateOnly sessionDate, Candle? extra,
        bool extraIsEth, bool extraIsIb)
    {
        _sessions.TryGetValue(sessionDate, out var agg);

        double? overnightHigh = agg.HasOn ? agg.OnHigh : null;
        double? overnightLow = agg.HasOn ? agg.OnLow : null;
        double? ibHigh = agg.HasIb ? agg.IbHigh : null;
        double? ibLow = agg.HasIb ? agg.IbLow : null;

        if (extra is { } candle)
        {
            if (extraIsEth)
            {
                overnightHigh = overnightHigh is double oh ? Math.Max(oh, candle.High) : candle.High;
                overnightLow = overnightLow is double ol ? Math.Min(ol, candle.Low) : candle.Low;
            }

            if (extraIsIb)
            {
                ibHigh = ibHigh is double ih ? Math.Max(ih, candle.High) : candle.High;
                ibLow = ibLow is double il ? Math.Min(il, candle.Low) : candle.Low;
            }
        }

        return levels with
        {
            OvernightHigh = overnightHigh,
            OvernightLow = overnightLow,
            InitialBalanceHigh = ibHigh,
            InitialBalanceLow = ibLow
        };
    }

    private SessionLevels ApplyBreaks(SessionLevels levels, Candle candle, DateTime local)
    {
        var time = TimeOnly.FromDateTime(local);
        var rthStarted = time >= _options.RthStart;
        var ibEnded = time >= _options.RthStart.Add(_options.InitialBalanceDuration);

        return levels with
        {
            PreviousDayHighBroken = levels.PreviousDayHighBroken
                                    || levels.PreviousDayHigh is double pdh && candle.High > pdh,
            PreviousDayLowBroken = levels.PreviousDayLowBroken
                                   || levels.PreviousDayLow is double pdl && candle.Low < pdl,
            OvernightHighBroken = levels.OvernightHighBroken
                                  || rthStarted && levels.OvernightHigh is double onh && candle.High > onh,
            OvernightLowBroken = levels.OvernightLowBroken
                                 || rthStarted && levels.OvernightLow is double onl && candle.Low < onl,
            InitialBalanceHighBroken = levels.InitialBalanceHighBroken
                                       || ibEnded && levels.InitialBalanceHigh is double ibh && candle.High > ibh,
            InitialBalanceLowBroken = levels.InitialBalanceLowBroken
                                      || ibEnded && levels.InitialBalanceLow is double ibl && candle.Low < ibl
        };
    }

    private void ApplySession(Candle candle, DateOnly sessionDate, bool isRth, bool isEth, bool isIb)
    {
        if (!_sessions.TryGetValue(sessionDate, out var agg)) agg = default;

        if (agg.HasSession)
        {
            agg.SessionHigh = Math.Max(agg.SessionHigh, candle.High);
            agg.SessionLow = Math.Min(agg.SessionLow, candle.Low);
        }
        else
        {
            agg.SessionHigh = candle.High;
            agg.SessionLow = candle.Low;
            agg.HasSession = true;
        }

        if (isRth)
        {
            if (agg.HasRth)
            {
                agg.RthHigh = Math.Max(agg.RthHigh, candle.High);
                agg.RthLow = Math.Min(agg.RthLow, candle.Low);
            }
            else
            {
                agg.RthHigh = candle.High;
                agg.RthLow = candle.Low;
                agg.HasRth = true;
            }

            agg.RthClose = candle.Close;
        }

        if (isEth)
        {
            if (agg.HasOn)
            {
                agg.OnHigh = Math.Max(agg.OnHigh, candle.High);
                agg.OnLow = Math.Min(agg.OnLow, candle.Low);
            }
            else
            {
                agg.OnHigh = candle.High;
                agg.OnLow = candle.Low;
                agg.HasOn = true;
            }
        }

        if (isIb)
        {
            if (agg.HasIb)
            {
                agg.IbHigh = Math.Max(agg.IbHigh, candle.High);
                agg.IbLow = Math.Min(agg.IbLow, candle.Low);
            }
            else
            {
                agg.IbHigh = candle.High;
                agg.IbLow = candle.Low;
                agg.HasIb = true;
            }
        }

        _sessions[sessionDate] = agg;
    }

    private (DateTime Local, DateOnly SessionDate, bool IsRth, bool IsEth, bool IsIb) Classify(Candle candle)
    {
        var local = SessionCalendar.ToLocal(candle.Time, _options.TimeZone);
        return (local,
            SessionCalendar.GetSessionDate(local, _options),
            SessionCalendar.IsRth(local, _options),
            SessionCalendar.IsEth(local, _options),
            SessionCalendar.IsInitialBalance(local, _options));
    }

    private IndicatorResult Build(SignalType signal,
        (double Vwap, double? Upper1, double? Upper2, double? Lower1, double? Lower2) vwap,
        SessionLevels levels)
    {
        var color = signal switch
        {
            SignalType.BullAbsorption => _options.BullAbsorptionColor,
            SignalType.BearAbsorption => _options.BearAbsorptionColor,
            SignalType.BullSweep => _options.BullSweepColor,
            SignalType.BearSweep => _options.BearSweepColor,
            SignalType.VolumeExpansion => _options.VolumeExpansionColor,
            _ => _options.NeutralColor
        };

        return new IndicatorResult(signal, color, vwap.Vwap,
            vwap.Upper1, vwap.Upper2, vwap.Lower1, vwap.Lower2,
            levels, new List<SwingPoint>(_swings.Swings));
    }

    private void TrimBars()
    {
        while (_bars.Count > _maxBars)
            _bars.RemoveAt(0);
    }

    private void PruneSessions(DateOnly sessionDate)
    {
        var oldest = sessionDate.AddDays(-2);
        List<DateOnly>? stale = null;
        foreach (var key in _sessions.Keys)
        {
            if (key >= oldest) continue;
            stale ??= new List<DateOnly>();
            stale.Add(key);
        }

        if (stale == null) return;
        foreach (var key in stale) _sessions.Remove(key);
    }

    private static void Validate(Candle candle)
    {
        if (candle.Open <= 0 || candle.High <= 0 || candle.Low <= 0 || candle.Close <= 0)
            throw new ArgumentException("Candle prices must be greater than zero.");
        if (candle.High < candle.Low || candle.High < candle.Open || candle.High < candle.Close
            || candle.Low > candle.Open || candle.Low > candle.Close)
            throw new ArgumentException("Candle OHLC values are inconsistent.");
        if (candle.Volume < 0)
            throw new ArgumentException("Candle volume must not be negative.");
    }

    private struct SessionAgg
    {
        public bool HasSession;
        public double SessionHigh;
        public double SessionLow;

        public bool HasRth;
        public double RthHigh;
        public double RthLow;
        public double RthClose;

        public bool HasOn;
        public double OnHigh;
        public double OnLow;

        public bool HasIb;
        public double IbHigh;
        public double IbLow;
    }
}
