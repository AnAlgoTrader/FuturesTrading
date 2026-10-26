using System;
using System.Globalization;
using System.IO;

namespace FootPrintTrader.Model;

/// <summary>
/// Every market-specific setting for the application.
/// Change the values below to run a different market.
/// </summary>
public class MarketProfile
{
    // Contract
    public string DisplayName { get; set; } = "E-mini S&P 500 / Micro E-mini S&P 500";
    public string DataSymbol { get; set; } = "ES";
    public string TradeSymbol { get; set; } = "MES";
    public string SecurityType { get; set; } = "FUT";
    public string Currency { get; set; } = "USD";
    public string Exchange { get; set; } = "CME";
    public string DataMultiplier { get; set; } = "50";
    public string TradeMultiplier { get; set; } = "5";

    // Price / chart
    public double TickSize { get; set; } = 0.25;
    public int PriceDecimals { get; set; } = 2;
    public int TradesPerBar { get; set; } = 1000;

    // Trading
    public decimal OrderQuantity { get; set; } = 1m;
    public double CommissionPerSide { get; set; } = 0.7;

    // Persistence
    public string DataDirectory { get; set; }

    public double RoundTripCommission => CommissionPerSide * 2;

    public double TradeMultiplierValue =>
        double.TryParse(TradeMultiplier, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 1;

    public static MarketProfile Current { get; } = new();

    public MarketProfile()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FootPrintTrader", DataSymbol + TradeSymbol);
        Directory.CreateDirectory(DataDirectory);
    }

    public MarketInfo ToMarketInfo() => new()
    {
        DisplayName = DisplayName,
        DataSymbol = DataSymbol,
        TradeSymbol = TradeSymbol,
        Exchange = Exchange,
        PriceDecimals = PriceDecimals,
        TradesPerBar = TradesPerBar
    };
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
