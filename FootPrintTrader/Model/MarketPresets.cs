namespace FootPrintTrader.Model;

public static class MarketPresets
{
    /// <summary>
    /// E-mini S&amp;P 500 market data, Micro E-mini S&amp;P 500 order routing.
    /// </summary>
    public static readonly MarketProfile EsMes = new()
    {
        DisplayName = "E-mini S&P 500 / Micro E-mini S&P 500",
        DataSymbol = "ES",
        TradeSymbol = "MES",
        SecurityType = "FUT",
        Currency = "USD",
        Exchange = "CME",
        DataMultiplier = 50,
        TradeMultiplier = 5,

        TickSize = 0.25,
        PriceDecimals = 2,

        TradesPerBar = 1000,
        MinLiveTradesForSignal = 250,
        AbsorptionDepthTicks = 2,
        MinRangeTicksForTrade = 10,

        ImbalanceRatio = 3.0m,
        StackedLevelsRequired = 3,
        MinImbalanceVolRatio = 0.05,
        MinBarVolumeBaselineRatio = 0.25,
        AbsorptionLevelSigma = 3.0,
        AbsorptionSideDominance = 0.6,
        DivergenceWindowBars = 20,
        DivergenceMinCvdGapRatio = 0.10,
        MaxUnfinishedAuctions = 4,
        BaselineWindowBars = 20,
        LevelVolumeWindow = 2000,
        MaxZoneAgeBars = 8,

        OrderQuantity = 1m,
        CommissionPerSide = 0.7
    };
}
