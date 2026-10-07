namespace Indicator.Tests;

/// <summary>
/// Standard fixture used by level/signal tests.
///
/// Previous session (Mon 2026-01-12 RTH): PDH 5100 / PDL 5000 / PDC 5050.
/// Overnight of Tue 2026-01-13: ONH 5090 / ONL 5010.
/// Initial balance (09:30-10:30): IBH 5070 / IBL 5030.
/// 20 identical warmup bars (10:30-11:55): avg spread 10, avg volume 1000.
/// Session extreme so far: high 5090, low 5010.
/// </summary>
internal static class Fixture
{
    public static readonly DateOnly PrevDate = new(2026, 1, 12);
    public static readonly DateOnly SessionDate = new(2026, 1, 13);

    public const double Pdh = 5100;
    public const double Pdl = 5000;
    public const double Pdc = 5050;
    public const double Onh = 5090;
    public const double Onl = 5010;
    public const double Ibh = 5070;
    public const double Ibl = 5030;

    public const double AvgSpread = 10;
    public const double AvgVolume = 1000;

    public static List<Candle> Build()
    {
        var bars = new List<Candle>
        {
            Bar(PrevDate, 9, 30, 5010, 5060, 5000, 5050, 1000),
            Bar(PrevDate, 9, 31, 5050, 5100, 5040, 5080, 1000),
            Bar(PrevDate, 9, 32, 5080, 5090, 5020, 5050, 1000),

            Bar(PrevDate, 18, 5, 5050, 5090, 5040, 5070, 1000),
            Bar(SessionDate, 3, 0, 5070, 5080, 5010, 5020, 1000),

            Bar(SessionDate, 9, 30, 5030, 5070, 5030, 5060, 1000),
            Bar(SessionDate, 9, 40, 5060, 5070, 5030, 5050, 1000)
        };

        var warmupStart = new TimeOnly(10, 30);
        for (var i = 0; i < 20; i++)
        {
            var time = warmupStart.AddMinutes(5 * i);
            bars.Add(Bar(SessionDate, time.Hour, time.Minute, 5050, 5055, 5045, 5052, 1000));
        }

        return bars;
    }

    public static Candle Bar(double open, double high, double low, double close, double volume) =>
        Bar(SessionDate, 12, 30, open, high, low, close, volume);

    public static Candle Bar(DateOnly date, int hour, int minute,
        double open, double high, double low, double close, double volume) =>
        new(TestTime.Et(date, hour, minute), open, high, low, close, volume);

    public static IndicatorEngine EngineWithFixture()
    {
        var engine = new IndicatorEngine();
        foreach (var bar in Build())
            engine.ProcessCandle(bar);
        return engine;
    }
}
