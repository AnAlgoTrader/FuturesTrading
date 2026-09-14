namespace DayTrader.Models;

/// <summary>
/// One row of SPX daily OHLC -- the rawest price series the VRP model consumes. It is the source
/// for the range-based realized variance (Parkinson), the overnight-gap term, and the leverage /
/// asymmetry input (sign of the prior day's return). Backfilled from Yahoo (^GSPC, interval=1d)
/// and appended by DataGatherer's daily collector. This is spec item #2 of the VRP build spec.
/// </summary>
public class SpxDailyRecord
{
    public string CollectionDay { get; set; } = "";

    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
}