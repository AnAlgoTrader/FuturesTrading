namespace FootPrintTrader.Model;

/// <summary>
/// Plain market configuration. Values are supplied by a preset.
/// </summary>
public class MarketProfile
{
    // Contract
    public string DisplayName { get; set; }
    public string DataSymbol { get; set; }
    public string TradeSymbol { get; set; }
    public string SecurityType { get; set; }
    public string Currency { get; set; }
    public string Exchange { get; set; }
    public double DataMultiplier { get; set; }
    public double TradeMultiplier { get; set; }

    // Underlying IB symbol & trading classes (for markets where IB root symbol differs or requires trading class)
    public string UnderlyingSymbol { get; set; }
    public string DataTradingClass { get; set; }
    public string TradeTradingClass { get; set; }
    public string CurrencySign { get; set; }

    // Price / chart
    public double TickSize { get; set; }
    public int PriceDecimals { get; set; }

    // Footprint analysis
    public int TradesPerBar { get; set; }

    // Divergence
    public int DivergenceWindowBars { get; set; }
    public double DivergenceMinCvdGapRatio { get; set; }    // CVD gap >= 10% of prior extreme

    // Signals
    public SignalSettings Signals { get; set; } = new();

    // Trading
    public decimal OrderQuantity { get; set; }
    public double CommissionPerSide { get; set; }
}

public class MarketInfo
{
    public string DisplayName { get; set; }
    public string DataSymbol { get; set; }
    public string TradeSymbol { get; set; }
    public string Exchange { get; set; }
    public int PriceDecimals { get; set; }
    public int TradesPerBar { get; set; }
    public string CurrencySign { get; set; }
}
