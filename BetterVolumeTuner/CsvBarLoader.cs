using System.Globalization;
using DayTrader.Helper;

namespace BetterVolumeTuner;

/// <summary>
/// Loads NQ 1-minute OHLCV bars from CSV and converts them to DayTrader Bar objects.
/// Delta is approximated using the close-vs-open volume split:
///   buyVol = volume * (close - open) / (high - low)
///   sellVol = volume - buyVol
/// Inside bars (open == close or high == low) get a 50/50 split.
/// </summary>
public static class CsvBarLoader
{
    public static List<Bar> Load(string csvPath)
    {
        var bars = new List<Bar>();

        // Auto-detect timestamp format from the first data line.
        //   NQ: "12/26/2022 18:01"   ET local, no timezone
        //   ES: "2025-10-02T13:14:00Z"  ISO8601 UTC
        bool formatUtc = PeekFormat(csvPath);

        foreach (var line in File.ReadLines(csvPath))
        {
            // Skip header (first line) and any empty lines
            if (line.Length == 0) continue;
            bool isHeader = line.StartsWith("time", StringComparison.OrdinalIgnoreCase)
                         || line.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase);
            if (isHeader) continue;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 6) continue;

            try
            {
                long epochSeconds = ParseTimestamp(parts[0].Trim(), formatUtc: formatUtc);

                double open = double.Parse(parts[1], CultureInfo.InvariantCulture);
                double high = double.Parse(parts[2], CultureInfo.InvariantCulture);
                double low = double.Parse(parts[3], CultureInfo.InvariantCulture);
                double close = double.Parse(parts[4], CultureInfo.InvariantCulture);
                decimal volume = decimal.Parse(parts[5], CultureInfo.InvariantCulture);

                // Approximate delta via close-vs-open split
                decimal buyVolume, sellVolume;
                double spread = high - low;
                if (spread > 0 && open != close)
                {
                    double ratio = (close - open) / spread;
                    if (ratio > 0)
                    {
                        buyVolume = volume * (decimal)ratio;
                        sellVolume = volume - buyVolume;
                    }
                    else
                    {
                        sellVolume = volume * (decimal)(-ratio);
                        buyVolume = volume - sellVolume;
                    }
                }
                else
                {
                    // Inside bar or zero spread — 50/50 split
                    buyVolume = volume / 2m;
                    sellVolume = volume - buyVolume;
                }

                var bar = new Bar
                {
                    StartTime = epochSeconds,
                    EndTime = epochSeconds + 60,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = volume,
                    BuyVolume = buyVolume,
                    SellVolume = sellVolume,
                    TickCount = 1,
                    IsFinal = true,
                    ChartTime = epochSeconds
                };

                bars.Add(bar);
            }
            catch
            {
                // Skip malformed lines
            }
        }

        return bars;
    }

    /// <summary>
    /// Reads the first data line of the CSV and determines whether timestamps
    /// are ISO8601 UTC (ES) or ET local (NQ).
    /// </summary>
    private static bool PeekFormat(string csvPath)
    {
        using var reader = new StreamReader(csvPath);
        reader.ReadLine(); // header
        string? first = reader.ReadLine(); // first data line
        if (string.IsNullOrEmpty(first)) return true;

        // ISO8601 with 'T' separator and 'Z' suffix => UTC
        bool isUtc = first.Contains('T') && first.Contains('Z');
        return isUtc;
    }

    private static long ParseTimestamp(string ts, bool formatUtc)
    {
        if (formatUtc)
        {
            var dt = DateTimeOffset.Parse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            return dt.ToUnixTimeSeconds();
        }

        // NQ format: "MM/dd/yyyy HH:mm" — ET local (America/New_York), no timezone
        var local = DateTime.ParseExact(ts, "MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture);
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), eastern);
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }
}
