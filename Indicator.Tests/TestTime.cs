namespace Indicator.Tests;

/// <summary>Unix-second helpers for building candles at known US Eastern local times.</summary>
internal static class TestTime
{
    private static readonly TimeSpan Est = TimeSpan.FromHours(-5);
    private static readonly TimeSpan Edt = TimeSpan.FromHours(-4);

    /// <summary>Winter (EST) timestamp.</summary>
    public static long Et(int year, int month, int day, int hour, int minute, int second = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, second, Est).ToUnixTimeSeconds();

    /// <summary>Winter (EST) timestamp for a date.</summary>
    public static long Et(DateOnly date, int hour, int minute, int second = 0) =>
        Et(date.Year, date.Month, date.Day, hour, minute, second);

    /// <summary>Summer (EDT) timestamp.</summary>
    public static long EtEdt(int year, int month, int day, int hour, int minute, int second = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, second, Edt).ToUnixTimeSeconds();
}
