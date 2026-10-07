using Xunit;

namespace Indicator.Tests;

public class SessionCalendarTests
{
    private static readonly IndicatorOptions Options = new();

    [Theory]
    [InlineData(17, 59, "2026-01-13")]
    [InlineData(18, 0, "2026-01-14")]
    [InlineData(9, 29, "2026-01-13")]
    [InlineData(9, 30, "2026-01-13")]
    [InlineData(0, 0, "2026-01-13")]
    public void SessionDate_RollsAtEthStart(int hour, int minute, string expected)
    {
        var local = SessionCalendar.ToLocal(TestTime.Et(2026, 1, 13, hour, minute), Options.TimeZone);
        Assert.Equal(DateOnly.Parse(expected), SessionCalendar.GetSessionDate(local, Options));
    }

    [Fact]
    public void SessionDate_RollsAtEthStart_EdtSummer()
    {
        var local = SessionCalendar.ToLocal(TestTime.EtEdt(2026, 7, 13, 18, 0), Options.TimeZone);
        Assert.Equal(new DateOnly(2026, 7, 14), SessionCalendar.GetSessionDate(local, Options));
    }

    [Theory]
    [InlineData(9, 29, false)]
    [InlineData(9, 30, true)]
    [InlineData(16, 14, true)]
    [InlineData(16, 15, false)]
    public void Rth_WindowBoundaries(int hour, int minute, bool expected)
    {
        var local = SessionCalendar.ToLocal(TestTime.Et(2026, 1, 13, hour, minute), Options.TimeZone);
        Assert.Equal(expected, SessionCalendar.IsRth(local, Options));
    }

    [Theory]
    [InlineData(5, 0, true)]
    [InlineData(16, 15, false)]
    [InlineData(17, 59, false)]
    [InlineData(18, 0, true)]
    [InlineData(23, 59, true)]
    public void Eth_WindowBoundaries(int hour, int minute, bool expected)
    {
        var local = SessionCalendar.ToLocal(TestTime.Et(2026, 1, 13, hour, minute), Options.TimeZone);
        Assert.Equal(expected, SessionCalendar.IsEth(local, Options));
    }

    [Theory]
    [InlineData(9, 29, false)]
    [InlineData(9, 30, true)]
    [InlineData(10, 29, true)]
    [InlineData(10, 30, false)]
    public void InitialBalance_WindowBoundaries(int hour, int minute, bool expected)
    {
        var local = SessionCalendar.ToLocal(TestTime.Et(2026, 1, 13, hour, minute), Options.TimeZone);
        Assert.Equal(expected, SessionCalendar.IsInitialBalance(local, Options));
    }

    [Fact]
    public void Dst_SpringForward()
    {
        // US DST 2026 starts Sunday 2026-03-08 at 02:00 local.
        var beforeInstant = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero);
        var afterInstant = new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero);
        var before = SessionCalendar.ToLocal(beforeInstant.ToUnixTimeSeconds(), Options.TimeZone);
        var after = SessionCalendar.ToLocal(afterInstant.ToUnixTimeSeconds(), Options.TimeZone);

        Assert.Equal(new DateTime(2026, 3, 8, 1, 30, 0), before);
        Assert.Equal(new DateTime(2026, 3, 8, 3, 0, 0), after);
        Assert.Equal(TimeSpan.FromHours(-5), Options.TimeZone.GetUtcOffset(beforeInstant));
        Assert.Equal(TimeSpan.FromHours(-4), Options.TimeZone.GetUtcOffset(afterInstant));
    }

    [Fact]
    public void Dst_FallBack()
    {
        // US DST 2026 ends Sunday 2026-11-01 at 02:00 local (back to 01:00).
        var beforeInstant = new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);
        var afterInstant = new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero);
        var before = SessionCalendar.ToLocal(beforeInstant.ToUnixTimeSeconds(), Options.TimeZone);
        var after = SessionCalendar.ToLocal(afterInstant.ToUnixTimeSeconds(), Options.TimeZone);

        Assert.Equal(new DateTime(2026, 11, 1, 1, 30, 0), before);
        Assert.Equal(new DateTime(2026, 11, 1, 1, 30, 0), after);
        Assert.Equal(TimeSpan.FromHours(-4), Options.TimeZone.GetUtcOffset(beforeInstant));
        Assert.Equal(TimeSpan.FromHours(-5), Options.TimeZone.GetUtcOffset(afterInstant));
    }
}
