using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FootPrintTrader.Model;

public class MarketProfile
{
    public string Key { get; set; } = "ESMES";
    public string DisplayName { get; set; } = "E-mini S&P 500 / Micro E-mini S&P 500";
    public string DataProduct { get; set; }
    public string TradeProduct { get; set; }

    public string DataSymbol { get; set; } = "ES";
    public string TradeSymbol { get; set; } = "MES";
    public string SecurityType { get; set; } = "FUT";
    public string Currency { get; set; } = "USD";
    public string Exchange { get; set; } = "CME";

    public string DataMultiplier { get; set; } = "50";
    public string TradeMultiplier { get; set; } = "5";

    public double TickSize { get; set; } = 0.25;
    public int PriceDecimals { get; set; } = 2;
    public int TradesPerBar { get; set; } = 1000;

    public decimal OrderQuantity { get; set; } = 1m;
    public double CommissionPerSide { get; set; } = 0.7;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 4001;
    public int ClientId { get; set; } = 0;

    public string DataDirectory { get; set; }

    public string EffectiveDataProduct => string.IsNullOrWhiteSpace(DataProduct) ? DataSymbol : DataProduct;
    public string EffectiveTradeProduct => string.IsNullOrWhiteSpace(TradeProduct) ? TradeSymbol : TradeProduct;
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? $"{EffectiveDataProduct}/{EffectiveTradeProduct}" : DisplayName;
    public double TradeMultiplierValue
    {
        get
        {
            if (double.TryParse(TradeMultiplier, NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                return m;
            if (double.TryParse(DataMultiplier, NumberStyles.Float, CultureInfo.InvariantCulture, out m))
                return m;
            return 1.0;
        }
    }

    /// <summary>
    /// Commission charged per side, round trip = 2x.
    /// </summary>
    public double RoundTripCommission
    {
        get => CommissionPerSide * 2.0;
        set { }
    }

    /// <summary>
    /// The one market configuration the whole application runs on.
    /// Change <see cref="MarketProfiles.Default"/> to switch markets, or override it with
    /// marketprofile.json / the FOOTPRINT_* environment variables.
    /// </summary>
    public static MarketProfile Current { get; } = Load();

    public static MarketProfile Load()
    {
        var profilePath = ResolveProfilePath();
        if (File.Exists(profilePath))
        {
            try
            {
                var json = File.ReadAllText(profilePath);
                var profile = JsonSerializer.Deserialize<MarketProfile>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (profile != null)
                {
                    profile.EnsureDefaults();
                    return profile;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MARKET] Failed to load profile from {profilePath}: {ex.Message}");
            }
        }

        var profileFromEnv = LoadFromEnvironment();
        if (profileFromEnv != null)
        {
            profileFromEnv.EnsureDefaults();
            return profileFromEnv;
        }

        var defaults = MarketProfiles.Default;
        defaults.EnsureDefaults();
        return defaults;
    }

    private void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(Key))
            Key = $"{DataSymbol}{TradeSymbol}";
        if (string.IsNullOrWhiteSpace(DisplayName))
            DisplayName = $"{DataSymbol} / {TradeSymbol}";
        if (string.IsNullOrWhiteSpace(DataDirectory))
            DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FootPrintTrader", Key);
        PriceDecimals = GetDecimals(TickSize);
        Directory.CreateDirectory(DataDirectory);
    }

    private static string ResolveProfilePath()
    {
        var envPath = Environment.GetEnvironmentVariable("FOOTPRINT_MARKET_PROFILE");
        if (!string.IsNullOrWhiteSpace(envPath))
            return Path.GetFullPath(envPath);

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "marketprofile.json"),
            Path.Combine(baseDir, "market-profile.json"),
            Path.Combine(baseDir, "config", "marketprofile.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "marketprofile.json")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static MarketProfile LoadFromEnvironment()
    {
        var dataSymbol = Environment.GetEnvironmentVariable("FOOTPRINT_DATA_SYMBOL");
        var tradeSymbol = Environment.GetEnvironmentVariable("FOOTPRINT_TRADE_SYMBOL");
        if (string.IsNullOrWhiteSpace(dataSymbol) && string.IsNullOrWhiteSpace(tradeSymbol))
            return null;

        var profile = new MarketProfile
        {
            DataSymbol = dataSymbol ?? "ES",
            TradeSymbol = tradeSymbol ?? dataSymbol ?? "MES",
            Exchange = Environment.GetEnvironmentVariable("FOOTPRINT_EXCHANGE") ?? "CME",
            SecurityType = Environment.GetEnvironmentVariable("FOOTPRINT_SECTYPE") ?? "FUT",
            Currency = Environment.GetEnvironmentVariable("FOOTPRINT_CURRENCY") ?? "USD",
            DataMultiplier = Environment.GetEnvironmentVariable("FOOTPRINT_DATA_MULTIPLIER") ?? "50",
            TradeMultiplier = Environment.GetEnvironmentVariable("FOOTPRINT_TRADE_MULTIPLIER") ?? "5",
            TickSize = double.TryParse(Environment.GetEnvironmentVariable("FOOTPRINT_TICKSIZE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ts) ? ts : 0.25,
            TradesPerBar = int.TryParse(Environment.GetEnvironmentVariable("FOOTPRINT_TRADES_PER_BAR"), out var tpb) ? tpb : 1000,
            OrderQuantity = decimal.TryParse(Environment.GetEnvironmentVariable("FOOTPRINT_ORDER_QTY"), NumberStyles.Float, CultureInfo.InvariantCulture, out var qty) ? qty : 1m,
            CommissionPerSide = double.TryParse(Environment.GetEnvironmentVariable("FOOTPRINT_COMMISSION"), NumberStyles.Float, CultureInfo.InvariantCulture, out var comm) ? comm : 0.7,
            Host = Environment.GetEnvironmentVariable("IB_HOST") ?? "127.0.0.1",
            Port = int.TryParse(Environment.GetEnvironmentVariable("IB_PORT"), out var port) ? port : 4001,
            ClientId = int.TryParse(Environment.GetEnvironmentVariable("IB_CLIENT_ID"), out var cid) ? cid : 0
        };

        profile.PriceDecimals = GetDecimals(profile.TickSize);
        return profile;
    }

    private static int GetDecimals(double tickSize)
    {
        if (tickSize >= 1) return 0;
        var s = tickSize.ToString("0.######", CultureInfo.InvariantCulture);
        var idx = s.IndexOf('.');
        if (idx < 0) return 0;
        return s.Length - idx - 1;
    }

    public MarketInfo ToMarketInfo()
    {
        return new MarketInfo
        {
            Key = Key,
            DisplayName = EffectiveDisplayName,
            DataSymbol = DataSymbol,
            TradeSymbol = TradeSymbol,
            DataProduct = EffectiveDataProduct,
            TradeProduct = EffectiveTradeProduct,
            Exchange = Exchange,
            TickSize = TickSize,
            PriceDecimals = PriceDecimals,
            TradesPerBar = TradesPerBar
        };
    }
}

public class MarketInfo
{
    public string Key { get; set; }
    public string DisplayName { get; set; }
    public string DataSymbol { get; set; }
    public string TradeSymbol { get; set; }
    public string DataProduct { get; set; }
    public string TradeProduct { get; set; }
    public string Exchange { get; set; }
    public double TickSize { get; set; }
    public int PriceDecimals { get; set; }
    public int TradesPerBar { get; set; }
}

public static class MarketProfiles
{
    // ---- Single place to choose the active market ----
    public static MarketProfile Default => EsMes;

    public static MarketProfile EsMes => new()
    {
        Key = "ESMES",
        DisplayName = "E-mini S&P 500 / Micro E-mini S&P 500",
        DataSymbol = "ES",
        TradeSymbol = "MES",
        SecurityType = "FUT",
        Currency = "USD",
        Exchange = "CME",
        DataMultiplier = "50",
        TradeMultiplier = "5",
        TickSize = 0.25,
        PriceDecimals = 2,
        TradesPerBar = 1000,
        OrderQuantity = 1m,
        CommissionPerSide = 0.7
    };

    public static MarketProfile NqMnq => new()
    {
        Key = "NQMNQ",
        DisplayName = "E-mini Nasdaq-100 / Micro E-mini Nasdaq-100",
        DataSymbol = "NQ",
        TradeSymbol = "MNQ",
        SecurityType = "FUT",
        Currency = "USD",
        Exchange = "CME",
        DataMultiplier = "20",
        TradeMultiplier = "2",
        TickSize = 0.25,
        PriceDecimals = 2,
        TradesPerBar = 1000,
        OrderQuantity = 1m,
        CommissionPerSide = 0.7
    };

    public static MarketProfile ClMcl => new()
    {
        Key = "CLMCL",
        DisplayName = "Crude Oil / Micro Crude Oil",
        DataSymbol = "CL",
        TradeSymbol = "MCL",
        SecurityType = "FUT",
        Currency = "USD",
        Exchange = "NYMEX",
        DataMultiplier = "1000",
        TradeMultiplier = "100",
        TickSize = 0.01,
        PriceDecimals = 2,
        TradesPerBar = 1000,
        OrderQuantity = 1m,
        CommissionPerSide = 0.75
    };

    public static MarketProfile YmMym => new()
    {
        Key = "YMMYM",
        DisplayName = "E-mini Dow / Micro E-mini Dow",
        DataSymbol = "YM",
        TradeSymbol = "MYM",
        SecurityType = "FUT",
        Currency = "USD",
        Exchange = "CBOT",
        DataMultiplier = "5",
        TradeMultiplier = "0.5",
        TickSize = 1.0,
        PriceDecimals = 0,
        TradesPerBar = 1000,
        OrderQuantity = 1m,
        CommissionPerSide = 0.7
    };
}
