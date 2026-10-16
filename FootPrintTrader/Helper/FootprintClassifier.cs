using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FootPrintTrader.Helper;

using System.Diagnostics;

public readonly record struct FootprintLevel(double Price, decimal BidVolume, decimal AskVolume)
{
    public decimal Total => BidVolume + AskVolume;
    public decimal Delta => AskVolume - BidVolume;
}

public sealed record FootprintBarData(
    int TradeCount, long StartTime, long EndTime,
    double Open, double High, double Low, double Close,
    FootprintLevel[] Levels)          // ascending price
{
    public decimal Delta => Levels.Sum(l => l.Delta);
    public decimal Volume => Levels.Sum(l => l.Total);
}

public sealed record FootprintSnapshot(
    FootprintBarData[] Completed,     // capped history, oldest first
    FootprintBarData Current);       // in-progress bar (maybe null)

public sealed class FootprintClassifier
{
    private enum Side { AggressiveBuy, AggressiveSell }

    private sealed class Level { public decimal Bid, Ask; }

    private readonly record struct Q(long Time, double Bid, double Ask, long Arrival);
    private readonly record struct T(long Time, double Price, decimal Size, long Arrival);

    private readonly Lock _lock = new();
    private readonly double _tickSize;
    private readonly int _tradesPerBar;
    private readonly int _maxHistory;
    private readonly long _maxHoldTs;
    private readonly bool _quotesFirstOnTie;

    private readonly Queue<Q> _quotes = new();
    private readonly Queue<T> _trades = new();
    private readonly List<FootprintBarData> _completed = new();
    private readonly Queue<FootprintBarData> _completedQueue = new();

    private Q? _quote;
    private long _quoteTime = long.MinValue;
    private double _lastPrice = double.NaN;
    private Side _lastSide = Side.AggressiveBuy;

    // In-progress bar
    private Dictionary<long, Level> _levels = new();
    private int _count;
    private long _start, _end;
    private double _open, _high, _low, _close;

    /// <summary>
    /// Raised when a bar reaches tradesPerBar. Invoked OUTSIDE the internal lock,
    /// on the thread that called OnQuote / OnTrade / GetSnapshot. Handlers may safely
    /// call back into this class. Exceptions propagate to the caller.
    /// </summary>
    public event Action<FootprintBarData> BarCompleted;

    public FootprintClassifier(double tickSize, int tradesPerBar = 2000, int maxHistory = 200,
                               TimeSpan? maxHold = null, bool quotesFirstOnTie = true)
    {
        _tickSize = tickSize;
        _tradesPerBar = tradesPerBar;
        _maxHistory = maxHistory;
        _maxHoldTs = (long)((maxHold ?? TimeSpan.FromMilliseconds(50)).TotalSeconds * Stopwatch.Frequency);
        _quotesFirstOnTie = quotesFirstOnTie;
    }

    // ---- Call from your two callbacks (any thread) ----

    public void OnQuote(long time, double bid, double ask)
    {
        lock (_lock)
        {
            _quotes.Enqueue(new Q(time, bid, ask, Stopwatch.GetTimestamp()));
            Process();
        }
        DrainCompleted();
    }

    public void OnTrade(long time, double price, decimal size)
    {
        lock (_lock)
        {
            _trades.Enqueue(new T(time, price, size, Stopwatch.GetTimestamp()));
            Process();
        }
        DrainCompleted();
    }

    // ---- Call from your UI timer (every 1 s) ----

    public FootprintSnapshot GetSnapshot()
    {
        FootprintSnapshot snap;
        lock (_lock)
        {
            Process();
            snap = new FootprintSnapshot(_completed.ToArray(),
                                         _count > 0 ? BuildBar() : null);
        }
        DrainCompleted();
        return snap;
    }

    // ---- Internals (all under lock) ----

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

    private void Process()
    {
        long now = Stopwatch.GetTimestamp();

        while (_trades.Count > 0)
        {
            var t = _trades.Peek();

            while (_quotes.Count > 0 &&
                   (_quotes.Peek().Time < t.Time ||
                    (_quotesFirstOnTie && _quotes.Peek().Time == t.Time)))
            {
                _quote = _quotes.Dequeue();
                _quoteTime = _quote.Value.Time;
            }

            bool quotesCaughtUp = _quoteTime >= t.Time;
            bool timedOut = now - t.Arrival >= _maxHoldTs;
            if (!quotesCaughtUp && !timedOut) break;

            _trades.Dequeue();
            AddTrade(t, Classify(t));
        }

        if (_trades.Count == 0)
            while (_quotes.Count > 0 && now - _quotes.Peek().Arrival >= _maxHoldTs)
            {
                _quote = _quotes.Dequeue();
                _quoteTime = _quote.Value.Time;
            }
    }

    private Side Classify(T t)
    {
        const double eps = 1e-9;

        if (_quote is { } q)
        {
            if (t.Price >= q.Ask - eps) return Remember(t, Side.AggressiveBuy);
            if (t.Price <= q.Bid + eps) return Remember(t, Side.AggressiveSell);

            double mid = (q.Bid + q.Ask) / 2.0;
            if (t.Price > mid + eps) return Remember(t, Side.AggressiveBuy);
            if (t.Price < mid - eps) return Remember(t, Side.AggressiveSell);
        }

        if (!double.IsNaN(_lastPrice))
        {
            if (t.Price > _lastPrice) return Remember(t, Side.AggressiveBuy);
            if (t.Price < _lastPrice) return Remember(t, Side.AggressiveSell);
        }
        return Remember(t, _lastSide);
    }

    private Side Remember(T t, Side side)
    {
        _lastPrice = t.Price;
        _lastSide = side;
        return side;
    }

    private void AddTrade(T t, Side side)
    {
        if (_count == 0) { _start = t.Time; _open = t.Price; _high = double.MinValue; _low = double.MaxValue; }
        _end = t.Time;
        _close = t.Price;
        if (t.Price > _high) _high = t.Price;
        if (t.Price < _low) _low = t.Price;
        _count++;

        long ticks = (long)Math.Round(t.Price / _tickSize, MidpointRounding.AwayFromZero);
        if (!_levels.TryGetValue(ticks, out var lvl)) _levels[ticks] = lvl = new Level();
        if (side == Side.AggressiveBuy) lvl.Ask += t.Size; else lvl.Bid += t.Size;

        if (_count >= _tradesPerBar)
        {
            var bar = BuildBar();
            _completed.Add(bar);
            if (_completed.Count > _maxHistory) _completed.RemoveAt(0);

            _levels = new Dictionary<long, Level>();
            _count = 0;
            _completedQueue.Enqueue(bar);
        }
    }

    private FootprintBarData BuildBar()
    {
        var levels = _levels
            .OrderBy(kv => kv.Key)
            .Select(kv => new FootprintLevel(kv.Key * _tickSize, kv.Value.Bid, kv.Value.Ask))
            .ToArray();
        return new FootprintBarData(_count, _start, _end, _open, _high, _low, _close, levels);
    }
}