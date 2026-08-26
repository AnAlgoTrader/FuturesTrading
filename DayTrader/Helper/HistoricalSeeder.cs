using System;
using System.Collections.Generic;
using System.Globalization;

namespace DayTrader.Helper;

public static class HistoricalSeeder
{
    /// <summary>
    /// Aggregates 1-second historical bars (IBApi.Bar) into fixed-tick bars based on trade Count.
    /// </summary>
    public static List<Bar> BuildTickBars(List<IBApi.Bar> ibBars, int ticksPerBar = 1000)
    {
        var result = new List<Bar>();
        if (ibBars == null || ibBars.Count == 0)
            return result;

        Bar current = null;
        long lastChartTime = 0;

        foreach (var ibBar in ibBars)
        {
            if (ibBar.Count <= 0 && ibBar.Volume <= 0)
                continue;

            var time = ParseIbTime(ibBar.Time);

            if (current == null)
            {
                current = new Bar
                {
                    StartTime = time,
                    Open = ibBar.Open,
                    High = ibBar.High,
                    Low = ibBar.Low,
                    Close = ibBar.Close,
                    Volume = ibBar.Volume,
                    TickCount = Math.Max(ibBar.Count, 1),
                    ChartTime = Math.Max(time, lastChartTime + 1)
                };
                lastChartTime = current.ChartTime;
            }
            else
            {
                current.High = Math.Max(current.High, ibBar.High);
                current.Low = Math.Min(current.Low, ibBar.Low);
                current.Close = ibBar.Close;
                current.EndTime = time;
                current.Volume += ibBar.Volume;
                current.TickCount += Math.Max(ibBar.Count, 1);
            }

            if (current.TickCount >= ticksPerBar)
            {
                current.IsFinal = true;
                result.Add(current);
                current = null;
            }
        }

        if (current != null && current.TickCount > 0)
        {
            current.IsFinal = true;
            result.Add(current);
        }

        return result;
    }

    public static long ParseIbTime(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr))
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (long.TryParse(timeStr, out var epoch))
            return epoch;

        var cleaned = timeStr.Trim();
        string[] formats =
        [
            "yyyyMMdd  HH:mm:ss",
            "yyyyMMdd HH:mm:ss",
            "yyyyMMdd-HH:mm:ss",
            "yyyyMMdd"
        ];

        if (DateTime.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return new DateTimeOffset(dt).ToUnixTimeSeconds();
        }

        if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt2))
        {
            return new DateTimeOffset(dt2).ToUnixTimeSeconds();
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
