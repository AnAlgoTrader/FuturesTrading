namespace BetterVolumeTuner;

/// <summary>
/// Classifies a bar's timestamp into market session based on US/Eastern time.
/// RTH (regular trading hours): Mon-Fri 09:30-16:00 ET.
/// Everything else is overnight / Globex session.
/// </summary>
public static class MarketSession
{
    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static bool IsRth(long unixSeconds)
    {
        var dt = TimeZoneInfo.ConvertTimeFromUtc(
            DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime,
            Eastern);

        // Weekend
        if (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday)
            return false;

        var timeOfDay = dt.TimeOfDay;
        return timeOfDay >= new TimeSpan(9, 30, 0) && timeOfDay <= new TimeSpan(16, 0, 0);
    }
}
