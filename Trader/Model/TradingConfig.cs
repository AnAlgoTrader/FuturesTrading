namespace Trader.Model;

public static class TradingConfig
{
    public const string DataSymbol = "ES";
    public const string TradeSymbol = "MES";
    public const string SecurityType = "FUT";
    public const string Currency = "USD";
    public const string Exchange = "CME";

    /// <summary>
    /// Number of trade ticks aggregated into one candle.
    /// </summary>
    public const int TicksPerBucket = 1000;
}
