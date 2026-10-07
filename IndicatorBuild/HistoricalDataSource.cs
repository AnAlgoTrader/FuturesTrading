using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IndicatorBuild.Model;

namespace IndicatorBuild;

public static class HistoricalDataSource
{
    public static List<Candle> LoadFromCsv(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Historical data file not found: {path}", path);

        var candles = new List<Candle>();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(',');
            if (parts.Length < 6) continue;

            if (!long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var time))
                continue;

            if (!TryParseDouble(parts[1], out var open) ||
                !TryParseDouble(parts[2], out var high) ||
                !TryParseDouble(parts[3], out var low) ||
                !TryParseDouble(parts[4], out var close) ||
                !TryParseDouble(parts[5], out var volume))
                continue;

            candles.Add(new Candle(time, open, high, low, close, volume));
        }

        return candles;
    }

    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
