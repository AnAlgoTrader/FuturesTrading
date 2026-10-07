using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IndicatorBuild.Model;

namespace IndicatorBuild.Streaming;

/// <summary>
/// A pull-based closed-candle feed. The pipeline only ever sees a bar once it has closed,
/// so a live adapter (e.g. a websocket hooking historicalData or a Trader bridge) merely
/// advances <see cref="Next"/> as each new bar prints. Returning null = feed exhausted.
/// </summary>
public interface IBarSource
{
    Candle? Next();
}

/// <summary>In-memory feed (tests, replays of small samples).</summary>
public sealed class ArrayBarSource : IBarSource
{
    private readonly IReadOnlyList<Candle> _candles;
    private int _index;

    public ArrayBarSource(IReadOnlyList<Candle> candles)
    {
        _candles = candles;
    }

    public Candle? Next() => _index < _candles.Count ? _candles[_index++] : null;
}

/// <summary>
/// Lazy replay of the historical CSV: yields one closed candle per Next() exactly as a
/// recording would print a live feed. Uses the same parse rules as <see cref="HistoricalDataSource"/>.
/// </summary>
public sealed class CsvBarSource : IBarSource
{
    private readonly IEnumerator<string> _lines;

    public CsvBarSource(string path)
    {
        _lines = File.ReadLines(path).GetEnumerator();
    }

    public int BarsRead { get; private set; }

    public Candle? Next()
    {
        while (_lines.MoveNext())
        {
            var raw = _lines.Current;
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(',');
            if (parts.Length < 6) continue;

            if (!long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var time)) continue;
            if (!TryParseDouble(parts[1], out var open) ||
                !TryParseDouble(parts[2], out var high) ||
                !TryParseDouble(parts[3], out var low) ||
                !TryParseDouble(parts[4], out var close) ||
                !TryParseDouble(parts[5], out var volume)) continue;

            BarsRead++;
            return new Candle(time, open, high, low, close, volume);
        }

        return null;
    }

    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}

/// <summary>Stops forwarding after N bars — for short live runs / tests.</summary>
public sealed class LimitBarSource : IBarSource
{
    private readonly IBarSource _inner;
    private readonly int _limit;
    private int _count;

    public LimitBarSource(IBarSource inner, int limit)
    {
        _inner = inner;
        _limit = limit;
    }

    public Candle? Next() => _count++ < _limit ? _inner.Next() : null;
}