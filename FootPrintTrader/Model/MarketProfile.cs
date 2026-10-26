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

    // Price / chart
    public double TickSize { get; set; }
    public int PriceDecimals { get; set; }

    // Footprint analysis
    public int TradesPerBar { get; set; }
    public int MinLiveTradesForSignal { get; set; }   // ignore live-bar signals until this many trades
    public int AbsorptionDepthTicks { get; set; }     // ticks price must reject after absorption
    public int MinRangeTicksForTrade { get; set; }    // prior-bar range below this = stand aside

    // Diagonal imbalance
    public decimal ImbalanceRatio { get; set; }       // 300% = Bookmap / GoCharting default
    public int StackedLevelsRequired { get; set; }    // consecutive imbalances to call a stack

    // Volume floors
    public double MinImbalanceVolRatio { get; set; }        // level >= 5% of bar's average level volume
    public double MinBarVolumeBaselineRatio { get; set; }   // bar >= 25% of 20-bar average volume

    // Absorption
    public double AbsorptionLevelSigma { get; set; }        // level volume >= mean + 3 sigma
    public double AbsorptionSideDominance { get; set; }     // aggressor side >= 60% of level volume

    // Divergence
    public int DivergenceWindowBars { get; set; }
    public double DivergenceMinCvdGapRatio { get; set; }    // CVD gap >= 10% of prior extreme

    // Unfinished auctions
    public int MaxUnfinishedAuctions { get; set; }

    // Rolling baselines / zone lifetime
    public int BaselineWindowBars { get; set; }
    public int LevelVolumeWindow { get; set; }
    public int MaxZoneAgeBars { get; set; }

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
}
