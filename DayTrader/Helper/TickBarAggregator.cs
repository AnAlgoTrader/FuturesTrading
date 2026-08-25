using System;
using Ib.Csharp.Api.Client.Implementation.Helper;

namespace DayTrader.Helper;

/// <summary>
/// A bar built from a fixed number of classified trades. Instances are
/// mutated in place while forming (see Apply) -- always call Snapshot()
/// before handing a Bar to code outside the aggregator (UI, websocket,
/// queues), so the receiver gets an immutable copy rather than a
/// reference that keeps changing underneath it.
/// </summary>
public class Bar
{
    public long StartTime { get; set; } // Unix epoch seconds, first trade in the bar
    public long EndTime { get; set; } // Unix epoch seconds, last trade in the bar
    public double Open { get; set; }
    public double High { get; set; } = double.MinValue;
    public double Low { get; set; } = double.MaxValue;
    public double Close { get; set; }
    public decimal Volume { get; set; }
    public decimal BuyVolume { get; set; }
    public decimal SellVolume { get; set; }
    public int TickCount { get; set; }
    public bool IsFinal { get; set; } // true only on the event that closes the bar
    public long ChartTime { get; set; } // assigned once at bar open -- see TickBarAggregator

    public double Spread => High - Low;
    public decimal Delta => BuyVolume - SellVolume;
    public decimal DeltaRatio => Volume == 0 ? 0 : Delta / Volume;

    public void Apply(ClassifiedTrade trade)
    {
        if (TickCount == 0)
        {
            Open = trade.Price;
            StartTime = trade.Time;
        }

        High = Math.Max(High, trade.Price);
        Low = Math.Min(Low, trade.Price);
        Close = trade.Price;
        EndTime = trade.Time;
        Volume += trade.Size;
        TickCount++;

        if (trade.Side == TradeSide.Buy) BuyVolume += trade.Size;
        else if (trade.Side == TradeSide.Sell) SellVolume += trade.Size;
    }

    /// <summary>Shallow copy -- safe to hand off, won't be mutated by the aggregator afterward.</summary>
    public Bar Snapshot() => (Bar)MemberwiseClone();
}

/// <summary>
/// Aggregates classified trades into fixed-count tick bars (e.g. 1000 trades/bar).
/// Feed it directly from BufferedLeeReadyClassifier.OnTradeClassified.
///
/// Two events:
///   OnBarUpdated - fires on every trade with a snapshot of the currently
///                  forming bar (IsFinal = false). Subscribe the UI to this
///                  for a "live moving candle" effect.
///   OnBarClosed  - fires once, when the bar reaches ticksPerBar
///                  (IsFinal = true). Use this to know when to start a new
///                  candle on the chart rather than keep updating the last one.
/// </summary>
public class TickBarAggregator
{
    private readonly int _ticksPerBar;
    private readonly TimeSpan? _minUpdateInterval;
    private Bar _current;
    private DateTime _lastUpdateEmitted = DateTime.MinValue;
    private long _lastChartTime = 0;

    public event Action<Bar> OnBarUpdated;
    public event Action<Bar> OnBarClosed;

    /// <param name="ticksPerBar">Number of classified trades per bar.</param>
    /// <param name="minUpdateInterval">
    /// Optional throttle for OnBarUpdated. If your tick rate is high (fast
    /// markets, thin ticksPerBar), firing on literally every trade can
    /// flood a UI/websocket faster than it needs. Set e.g. 100ms to cap
    /// update frequency; the closing update always fires regardless.
    /// Leave null to emit on every single trade.
    /// </param>
    public TickBarAggregator(int ticksPerBar = 1000, TimeSpan? minUpdateInterval = null)
    {
        if (ticksPerBar <= 0)
            throw new ArgumentException("ticksPerBar must be positive.");
        _ticksPerBar = ticksPerBar;
        _minUpdateInterval = minUpdateInterval;
    }

    /// <summary>Wire this to classifier.OnTradeClassified.</summary>
    public void OnTrade(ClassifiedTrade trade)
    {
        bool isNewBar = _current == null;
        _current ??= new Bar();
        _current.Apply(trade);

        if (isNewBar)
        {
            // Assign the chart-facing time once, at bar open, and keep it
            // fixed for every subsequent update to this same bar -- this
            // is what makes Lightweight Charts update the candle in place
            // rather than shifting it on every tick. Nudge forward from
            // the previous bar's chart time to guarantee strict ordering,
            // since tick bars can open within the same second as the
            // previous bar closed (real time-based bars never can).
            _current.ChartTime = Math.Max(_current.StartTime, _lastChartTime + 1);
            _lastChartTime = _current.ChartTime;
        }

        bool isClosing = _current.TickCount >= _ticksPerBar;

        if (isClosing)
        {
            _current.IsFinal = true;
            var finalSnapshot = _current.Snapshot();
            OnBarClosed?.Invoke(finalSnapshot);
            _current = null;
            _lastUpdateEmitted = DateTime.MinValue;
            return;
        }

        // Throttle in-progress updates if configured; the final update
        // above is never throttled, so the UI always gets the true close.
        var now = DateTime.UtcNow;
        if (_minUpdateInterval.HasValue && now - _lastUpdateEmitted < _minUpdateInterval.Value)
            return;

        _lastUpdateEmitted = now;
        OnBarUpdated?.Invoke(_current.Snapshot());
    }
}