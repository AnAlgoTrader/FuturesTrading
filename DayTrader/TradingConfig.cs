namespace DayTrader;

/// <summary>
/// In case of trading stocks, provide both the Chart and the Trade configs with the same values
/// </summary>
public static class TradingConfig
{
    public const string ChartSymbol = "SPY";
    public const string ChartSecurityType = "STK";
    public const string ChartCurrency = "USD";
    public const string ChartExchange = "SMART";

    public const string TradeSymbol = "MES";
    public const string TradeSecurityType = "FUT";
    public const string TradeCurrency = "USD";
    public const string TradeExchange = "CME";

    public static bool IsSingleInstrument =>
        ChartSymbol == TradeSymbol &&
        ChartSecurityType == TradeSecurityType &&
        ChartCurrency == TradeCurrency &&
        ChartExchange == TradeExchange;
}