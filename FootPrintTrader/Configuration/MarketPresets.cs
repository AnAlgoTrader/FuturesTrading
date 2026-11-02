namespace FootPrintTrader.Configuration;

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
        DivergenceWindowBars = 20,
        DivergenceMinCvdGapRatio = 0.10,

        Signals = new SignalSettings
        {
            MinDeltaAbsorption = 250m,
            SwingLookback = 10,
            ToleranceTicks = 1,
            CvdDivergenceLookback = 15,
            BreakoutLookback = 10,
            MinDeltaBreakout = 100m,
            MinBuyImbalancesBreakout = 3,
            ImbalanceRatio = 3.0m,
            ImbalanceMinVolume = 10m,
            FastThresholdRatio = 0.70,
            SlowdownThresholdRatio = 1.50,
            SpeedMedianWindow = 20,
            SpeedMinBars = 8,
            RepeatControlBars = 3
        },

        OrderQuantity = 1m,
        CommissionPerSide = 0.7,
        CurrencySign = "$"
    };
}
