using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace FootPrintTrader.Domain.Footprint;

/// <summary>
/// Diagnostic counters. Read with <see cref="FootprintClassifier.GetStats"/>.
/// On a one-tick-spread contract (ES, NQ, ...) MidRule should be close to zero; a visible share
/// there means quotes and trades are out of sync.
/// </summary>
public readonly record struct ClassifierStats(
    long Aggressor,      // price at/through the current bid or ask
    long PrevQuote,      // price inside current spread but at/through the PREVIOUS quote (late-quote fix)
    long MidRule,        // inside the spread, classified by side of mid
    long TickRule,       // no usable quote information, price vs previous trade price
    long Carry,          // same price as previous trade, previous side carried forward
    long Unreported,     // excluded: IBKR "unreported" prints
    long RejectedQuotes, // excluded: zero / one-sided / crossed quotes
    long RejectedTrades) // excluded: non-positive price or size
{
    public long Trades => Aggressor + PrevQuote + MidRule + TickRule + Carry;

    private static string Pct(long n, long d) =>
        (d == 0 ? 0.0 : 100.0 * n / d).ToString("F1", CultureInfo.InvariantCulture) + "%";

    public override string ToString() =>
        $"trades={Trades} | at/through quote={Aggressor} ({Pct(Aggressor, Trades)}) | " +
        $"prev-quote={PrevQuote} ({Pct(PrevQuote, Trades)}) | mid={MidRule} ({Pct(MidRule, Trades)}) | " +
        $"tick={TickRule} ({Pct(TickRule, Trades)}) | carry={Carry} ({Pct(Carry, Trades)}) | " +
        $"unreported={Unreported} badQuotes={RejectedQuotes} badTrades={RejectedTrades}";
}

/// <summary>
/// Builds tick-count footprint bars. Each trade is classified against the last valid quote that was
/// RECEIVED before it (arrival order), with no waiting and no re-ordering by IBKR's 1-second timestamps.
/// IBKR timestamps are only used for the bar's StartTime/EndTime.
///
/// Requirement: OnQuote and OnTrade must be called in the order the callbacks arrive from IBKR
/// (ideally from the same thread / the same message loop).
/// </summary>
public sealed class FootprintClassifier
{
    private enum Side { AggressiveBuy, AggressiveSell }
    private enum Via { Aggressor, PrevQuote, MidRule, TickRule, Carry }

    private const double Eps = 1e-9;

    private sealed class Level { public decimal Bid, Ask; }

    /// <param name="ChangedAt">Stopwatch timestamp of when this bid/ask PRICE pair first appeared
    /// (size-only updates do not change it).</param>
    private readonly record struct Q(double Bid, double Ask, long ChangedAt);

    private readonly Lock _lock = new();
    private readonly double _tickSize;
    private readonly int _tradesPerBar;
    private readonly int _maxHistory;
    private readonly bool _usePrevQuoteRule;
    private readonly long _prevQuoteWindowTs;

    private readonly List<FootprintBarData> _completed = new();
    private readonly Queue<FootprintBarData> _completedQueue = new();

    // Quote state
    private Q? _quote;      // latest valid quote
    private Q? _prevQuote;  // the quote that was in force before the latest PRICE change

    // Trade-side state
    private double _lastPrice = double.NaN;
    private Side _lastSide = Side.AggressiveBuy;

    // Counters
    private readonly long[] _via = new long[5];
    private long _unreported, _rejectedQuotes, _rejectedTrades;

    // In-progress bar
    private Dictionary<long, Level> _levels = new();
    private int _count;
    private long _nextBarId = 1;
    private long _start, _end;
    private long _startArrival, _endArrival;
    private double _open, _high, _low, _close;

    /// <summary>
    /// Raised when a bar reaches tradesPerBar. Invoked OUTSIDE the internal lock,
    /// on the thread that called OnTrade. Handlers may safely call back into this class.
    /// Exceptions propagate to the caller.
    /// </summary>
    public event Action<FootprintBarData> BarCompleted;

    /// <param name="usePrevQuoteRule">
    /// If a trade prints strictly inside the current spread but at/through the previous quote, and the
    /// current quote changed within <paramref name="prevQuoteWindow"/> before the trade, treat the trade as
    /// having hit the previous quote (the quote update that the trade caused arrived first).
    /// </param>
    /// <param name="prevQuoteWindow">Default 50 ms.</param>
    public FootprintClassifier(double tickSize, int tradesPerBar, int maxHistory = 200,
                               bool usePrevQuoteRule = true, TimeSpan? prevQuoteWindow = null)
    {
        _tickSize = tickSize;
        _tradesPerBar = tradesPerBar;
        _maxHistory = maxHistory;
        _usePrevQuoteRule = usePrevQuoteRule;
        _prevQuoteWindowTs = (long)((prevQuoteWindow ?? TimeSpan.FromMilliseconds(50)).TotalSeconds * Stopwatch.Frequency);
    }

    /// <summary>
    /// Call from the tick-by-tick BidAsk callback, in arrival order.
    /// <paramref name="time"/> is accepted for call-site compatibility but is not used for ordering.
    /// Zero, one-sided and crossed quotes are ignored.
    /// </summary>
    public void OnQuote(long time, double bid, double ask)
    {
        lock (_lock)
        {
            if (double.IsNaN(bid) || double.IsNaN(ask) || bid <= 0 || ask <= 0 || ask < bid)
            {
                _rejectedQuotes++;
                return;
            }

            var now = Stopwatch.GetTimestamp();

            if (_quote is { } cur)
            {
                bool priceChanged = Math.Abs(cur.Bid - bid) > Eps || Math.Abs(cur.Ask - ask) > Eps;
                if (priceChanged)
                {
                    _prevQuote = cur;
                    _quote = new Q(bid, ask, now);
                }
                // size-only update: nothing to do (keep ChangedAt of the price change)
            }
            else
            {
                _quote = new Q(bid, ask, now);
            }
        }
    }

    /// <summary>
    /// Call from the tick-by-tick Last/AllLast callback, in arrival order.
    /// Pass <paramref name="unreported"/> from IBKR's TickAttribLast if your wrapper exposes it.
    /// </summary>
    public void OnTrade(long time, double price, decimal size, bool unreported = false)
    {
        lock (_lock)
        {
            if (double.IsNaN(price) || price <= 0 || size <= 0)
            {
                _rejectedTrades++;
            }
            else if (unreported)
            {
                _unreported++;
            }
            else
            {
                var arrival = Stopwatch.GetTimestamp();
                AddTrade(time, price, size, arrival, Classify(price, arrival));
            }
        }
        DrainCompleted();
    }

    public FootprintSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new FootprintSnapshot(_completed.ToArray(),
                                         _count > 0 ? BuildBar() : null);
        }
    }

    public ClassifierStats GetStats()
    {
        lock (_lock)
        {
            return new ClassifierStats(
                _via[(int)Via.Aggressor], _via[(int)Via.PrevQuote], _via[(int)Via.MidRule],
                _via[(int)Via.TickRule], _via[(int)Via.Carry],
                _unreported, _rejectedQuotes, _rejectedTrades);
        }
    }

    public void ResetStats()
    {
        lock (_lock)
        {
            Array.Clear(_via);
            _unreported = _rejectedQuotes = _rejectedTrades = 0;
        }
    }

    private void DrainCompleted()
    {
        while (true)
        {
            FootprintBarData bar;
            lock (_lock)
            {
                if (_completedQueue.Count == 0) break;
                bar = _completedQueue.Dequeue();
            }
            BarCompleted?.Invoke(bar);
        }
    }

    private Side Classify(double price, long arrival)
    {
        if (_quote is { } q)
        {
            // 1. At or through the current best bid/ask: unambiguous aggressor.
            if (price >= q.Ask - Eps) return Remember(price, Side.AggressiveBuy, Via.Aggressor);
            if (price <= q.Bid + Eps) return Remember(price, Side.AggressiveSell, Via.Aggressor);

            // 2. Inside the current spread. If the quote moved just before this print, the trade most
            //    likely hit the PREVIOUS quote (the quote update caused by the trade arrived first).
            if (_usePrevQuoteRule && _prevQuote is { } p && arrival - q.ChangedAt <= _prevQuoteWindowTs)
            {
                if (price >= p.Ask - Eps) return Remember(price, Side.AggressiveBuy, Via.PrevQuote);
                if (price <= p.Bid + Eps) return Remember(price, Side.AggressiveSell, Via.PrevQuote);
            }

            // 3. Genuinely inside the spread: side of mid.
            double mid = (q.Bid + q.Ask) / 2.0;
            if (price > mid + Eps) return Remember(price, Side.AggressiveBuy, Via.MidRule);
            if (price < mid - Eps) return Remember(price, Side.AggressiveSell, Via.MidRule);
        }

        // 4. Tick rule (also used when no quote has been received yet).
        if (!double.IsNaN(_lastPrice))
        {
            if (price > _lastPrice) return Remember(price, Side.AggressiveBuy, Via.TickRule);
            if (price < _lastPrice) return Remember(price, Side.AggressiveSell, Via.TickRule);
        }

        // 5. Same price as the previous trade: carry the previous side.
        return Remember(price, _lastSide, Via.Carry);
    }

    private Side Remember(double price, Side side, Via via)
    {
        _via[(int)via]++;
        _lastPrice = price;
        _lastSide = side;
        return side;
    }

    private void AddTrade(long time, double price, decimal size, long arrival, Side side)
    {
        if (_count == 0)
        {
            _start = time;
            _startArrival = arrival;
            _open = price;
            _high = double.MinValue;
            _low = double.MaxValue;
        }
        _end = time;
        _endArrival = arrival;
        _close = price;
        if (price > _high) _high = price;
        if (price < _low) _low = price;
        _count++;

        long ticks = (long)Math.Round(price / _tickSize, MidpointRounding.AwayFromZero);
        if (!_levels.TryGetValue(ticks, out var lvl)) _levels[ticks] = lvl = new Level();
        if (side == Side.AggressiveBuy) lvl.Ask += size; else lvl.Bid += size;

        if (_count >= _tradesPerBar)
        {
            var bar = BuildBar();
            _completed.Add(bar);
            if (_completed.Count > _maxHistory) _completed.RemoveAt(0);

            _levels = new Dictionary<long, Level>();
            _count = 0;
            _nextBarId++;
            _completedQueue.Enqueue(bar);
        }
    }

    private FootprintBarData BuildBar()
    {
        var levels = _levels
            .OrderBy(kv => kv.Key)
            .Select(kv => new FootprintLevel(kv.Key * _tickSize, kv.Value.Bid, kv.Value.Ask))
            .ToArray();

        // Duration from LOCAL arrival times (sub-millisecond), not IBKR's 1-second timestamps.
        var duration = (_endArrival - _startArrival) / (double)Stopwatch.Frequency;

        return new FootprintBarData(_count, _start, _end, _open, _high, _low, _close, levels,
                                    Id: _nextBarId, DurationSeconds: duration);
    }
}
