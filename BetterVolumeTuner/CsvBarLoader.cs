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
        var lines = File.ReadLines(csvPath);

        bool headerSkipped = false;
        foreach (var line in lines)
        {
            if (!headerSkipped) { headerSkipped = true; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 6) continue;

            try
            {
                // timestamp ET, open, high, low, close, volume
                var timestamp = DateTime.ParseExact(
                    parts[0].Trim(),
                    "MM/dd/yyyy HH:mm",
                    CultureInfo.InvariantCulture);

                double open = double.Parse(parts[1], CultureInfo.InvariantCulture);
                double high = double.Parse(parts[2], CultureInfo.InvariantCulture);
                double low = double.Parse(parts[3], CultureInfo.InvariantCulture);
                double close = double.Parse(parts[4], CultureInfo.InvariantCulture);
                decimal volume = decimal.Parse(parts[5], CultureInfo.InvariantCulture);

                long epochSeconds = new DateTimeOffset(timestamp).ToUnixTimeSeconds();

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
}
