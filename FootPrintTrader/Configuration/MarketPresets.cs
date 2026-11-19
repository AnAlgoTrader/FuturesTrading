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

        BarIntervalSeconds = 30,
        DivergenceWindowBars = 20,
        DivergenceMinCvdGapRatio = 0.10,

        Signals = new SignalSettings
        {
            SwingLookback = 10,
            ToleranceTicks = 1,
            CvdDivergenceLookback = 15,
            BreakoutLookback = 10,
            MinBuyImbalancesBreakout = 3,
            ImbalanceRatio = 3.0m,
            FastThresholdRatio = 0.70,
            SlowdownThresholdRatio = 1.50,
            SpeedMedianWindow = 20,
            SpeedMinBars = 8,
            RepeatControlBars = 3,

            // All size/volatility thresholds are relative to the session's rolling baselines.
            DeltaFloorRatio = 2.0,
            AbsorptionWickRatio = 0.20,
            ExtremeVolumeRatio = 0.001,
            ExhaustionVolumeRatio = 1.2,
            ExhaustionRangeRatio = 0.8,
            BreakoutMinVolumeRatio = 0.8,
            FadeWindowBars = 3
        },

        OrderQuantity = 1m,
        CommissionPerSide = 0.7,
        CurrencySign = "$"
    };
}
