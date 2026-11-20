using System.Globalization;
using SupportingData.Model;

namespace SupportingData.Helper;

public class SupportingDataHelper
{
    public const string LastMonthVolumeFile = "/home/oce/LocalDB/logs/footprint/lastMonthVolume.csv";

    private static readonly TimeZoneInfo MadridTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    /// <summary>
    /// Reads the previously gathered supporting data CSV (Date, Open, High, Low, Close, Volume)
    /// and converts each epoch timestamp to Madrid time.
    /// </summary>
    public List<CandleSupportingData> GetCandleSupportingData()
    {
        if (!File.Exists(LastMonthVolumeFile))
            throw new FileNotFoundException($"Supporting data CSV not found: {LastMonthVolumeFile}", LastMonthVolumeFile);

        var candles = new List<CandleSupportingData>();
        var lineNumber = 0;

        foreach (var line in File.ReadLines(LastMonthVolumeFile))
        {
            lineNumber++;

            // Skip the header and any blank line.
            if (lineNumber == 1 || string.IsNullOrWhiteSpace(line))
                continue;

            var values = line.Split(',');

            if (values.Length < 6)
                throw new FormatException($"Malformed row at {LastMonthVolumeFile}:{lineNumber}: '{line}'");

            var utcTimestamp = long.Parse(values[0].Trim(), CultureInfo.InvariantCulture);

            candles.Add(new CandleSupportingData
            {
                UtcTimestamp = utcTimestamp,
                Time = ToMadridTime(utcTimestamp),
                Open = double.Parse(values[1].Trim(), CultureInfo.InvariantCulture),
                High = double.Parse(values[2].Trim(), CultureInfo.InvariantCulture),
                Low = double.Parse(values[3].Trim(), CultureInfo.InvariantCulture),
                Close = double.Parse(values[4].Trim(), CultureInfo.InvariantCulture),
                Volume = double.Parse(values[5].Trim(), CultureInfo.InvariantCulture),
            });
        }

        return candles;
    }

    /// <summary>
    /// Reads the supporting data CSV and averages the volume at each time of day across all days.
    /// </summary>
    public IReadOnlyList<AverageVolumePerTimeframe> GetAverageVolumePerTimeframe()
    {
        return GetCandleSupportingData()
            .GroupBy(c => new TimeOnly(c.Time.Hour, c.Time.Minute))
            .Select(group => new AverageVolumePerTimeframe
            {
                Time = group.Key,
                AverageVolume = group.Average(c => c.Volume)
            })
            .OrderBy(result => result.Time)
            .ToList();
    }

    private static DateTime ToMadridTime(long utcTimestamp)
    {
        var utcTime = DateTimeOffset.FromUnixTimeSeconds(utcTimestamp).UtcDateTime;
        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, MadridTimeZone);
    }
}
