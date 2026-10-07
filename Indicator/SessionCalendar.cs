using System;

namespace Indicator;

/// <summary>
/// Resolves session boundaries for a local (exchange) date/time using <see cref="IndicatorOptions"/>.
/// </summary>
public static class SessionCalendar
{
    public static DateTime ToLocal(long unixSeconds, TimeZoneInfo timeZone)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
    }

    /// <summary>
    /// The trading session a local time belongs to. The session runs from <see cref="IndicatorOptions.EthStart"/>
    /// on the previous calendar day through 18:00 on the current day, so candles at or after 18:00 ET
    /// roll into the next session's date.
    /// </summary>
    public static DateOnly GetSessionDate(DateTime local, IndicatorOptions options)
    {
        var date = DateOnly.FromDateTime(local);
        return TimeOnly.FromDateTime(local) >= options.EthStart ? date.AddDays(1) : date;
    }

    public static bool IsRth(DateTime local, IndicatorOptions options)
    {
        var t = TimeOnly.FromDateTime(local);
        return t >= options.RthStart && t < options.RthEnd;
    }

    public static bool IsEth(DateTime local, IndicatorOptions options)
    {
        var t = TimeOnly.FromDateTime(local);
        return t >= options.EthStart || t < options.EthEnd;
    }

    public static bool IsInitialBalance(DateTime local, IndicatorOptions options)
    {
        var t = TimeOnly.FromDateTime(local);
        return t >= options.RthStart && t < options.RthStart.Add(options.InitialBalanceDuration);
    }
}
