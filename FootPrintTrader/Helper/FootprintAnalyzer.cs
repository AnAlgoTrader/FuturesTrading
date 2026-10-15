using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FootPrintTrader.Helper;

public class OrderFlowSignal
{
    public string Type { get; set; } = "";        // "stacked_imbalance", "absorption", "trapped_traders", "divergence", "unfinished_auction", "exhaustion"
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Tone { get; set; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
    public string Price { get; set; } = "";
    public string Time { get; set; } = "";
}

public class OrderFlowZone
{
    public long BarStartTime { get; set; }
    public string TimeLabel { get; set; } = "";
    public bool IsBuy { get; set; }               // true = Buy Imbalance, false = Sell Imbalance
    public double LowPrice { get; set; }
    public double HighPrice { get; set; }
    public int Levels { get; set; }
    public bool IsExhaustionAtBirth { get; set; } // Formed at bar extreme (top for buy, bottom for sell)
    public int AgeBars { get; set; }
    public bool IsTrapped { get; set; }
}

public class OrderFlowContext
{
    public bool HasBias { get; set; }
    public string Bias { get; set; } = "Neutral";
    public string BiasTone { get; set; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
    public string Narrative { get; set; } = "";

    // Live Bar Metrics
    public decimal CurrentDelta { get; set; }
    public decimal CurrentVolume { get; set; }
    public double DeltaPercent { get; set; }
    public double PocPrice { get; set; }
    public string PocPosition { get; set; } = "Middle";
    public string PocMigration { get; set; } = "Leveled";

    // Multi-bar / Session Structure
    public decimal CumulativeDelta { get; set; }
    public string DeltaDivergence { get; set; } = "None";
    public string AuctionStatus { get; set; } = "Balanced";

    // Active Signals
    public List<OrderFlowSignal> Signals { get; set; } = new();
}

public static class FootprintAnalyzer
{
    public const decimal MinSignalVolume = 100m;
    public const decimal MinAbsorptionDelta = 40m;
    public const decimal ImbalanceRatio = 3.0m;
    public const decimal MinImbalanceVol = 10m;
    public const decimal MinDeltaDiff = 5m;
    public const int StackedLevelsRequired = 3;
    public const int MinLiveTradesForSignal = 250;
    public const int MaxZoneAgeBars = 8;

    private static readonly object _syncLock = new();
    private static readonly List<OrderFlowZone> _activeZones = new();
    private static readonly HashSet<long> _processedBars = new();
    private static OrderFlowContext _lastContext;

    public static OrderFlowContext AnalyzeContext(FootprintSnapshot snapshot, double tickSize, double currentPrice = 0)
    {
        if (snapshot == null)
            return _lastContext ?? new OrderFlowContext();

        var completed = snapshot.Completed ?? Array.Empty<FootprintBarData>();
        var current = snapshot.Current;

        // Use live bar for real-time live metrics (delta, POC, volume), fallback to last completed
        FootprintBarData bar = current ?? completed.LastOrDefault();
        FootprintBarData prevBar = current != null
            ? completed.LastOrDefault()
            : (completed.Length > 1 ? completed[^2] : null);

        if (bar == null)
            return _lastContext ?? new OrderFlowContext();

        var dec = Decimals(tickSize);
        var signals = new List<OrderFlowSignal>();
        var timeLabel = FootprintRenderBuilder.FormatTime(bar.StartTime);
        var evalPrice = currentPrice > 0 ? currentPrice : bar.Close;

        // 1. Cumulative Delta
        var cumDelta = completed.Sum(b => b.Delta) + (current?.Delta ?? 0m);

        // 2. POC Calculation & Zone Placement for current bar
        var levels = bar.Levels ?? Array.Empty<FootprintLevel>();
        var pocPrice = bar.Close;
        var pocPosition = "Middle (Value Area)";

        if (levels.Length > 0)
        {
            var pocLevel = levels.OrderByDescending(l => l.Total).First();
            pocPrice = pocLevel.Price;

            var range = bar.High - bar.Low;
            if (range > 1e-6)
            {
                var ratio = (pocPrice - bar.Low) / range;
                if (ratio >= 0.70) pocPosition = "Top";
                else if (ratio <= 0.30) pocPosition = "Bottom";
                else pocPosition = "Middle";
            }
        }

        // 3. POC Migration
        var pocMigration = "Leveled ◄►";
        if (prevBar != null && prevBar.Levels != null && prevBar.Levels.Length > 0)
        {
            var prevPoc = prevBar.Levels.OrderByDescending(l => l.Total).First().Price;
            var diff = pocPrice - prevPoc;
            if (diff >= tickSize * 0.75) pocMigration = "Rising ▲";
            else if (diff <= -tickSize * 0.75) pocMigration = "Falling ▼";
            else pocMigration = "Leveled ◄►";
        }

        // 4. Update Stateful Order Flow Zones from Completed Bars
        lock (_syncLock)
        {
            // Register newly completed bars
            foreach (var compBar in completed)
            {
                if (_processedBars.Add(compBar.StartTime))
                {
                    var barImbalances = DetectStackedImbalances(compBar);
                    var bRange = compBar.High - compBar.Low;
                    var bTimeLabel = FootprintRenderBuilder.FormatTime(compBar.StartTime);

                    foreach (var imb in barImbalances)
                    {
                        bool isExhaustion;
                        if (imb.IsBuy)
                        {
                            // Buy imbalance at extreme high of bar or bar closed below it
                            isExhaustion = bRange > 1e-6 &&
                                           (imb.EndP >= compBar.High - tickSize * 0.5 ||
                                            (imb.EndP - compBar.Low) / bRange >= 0.75 ||
                                            compBar.Close < imb.EndP);
                        }
                        else
                        {
                            // Sell imbalance at extreme low of bar or bar closed above it
                            isExhaustion = bRange > 1e-6 &&
                                           (imb.StartP <= compBar.Low + tickSize * 0.5 ||
                                            (imb.StartP - compBar.Low) / bRange <= 0.25 ||
                                            compBar.Close > imb.StartP);
                        }

                        _activeZones.Add(new OrderFlowZone
                        {
                            BarStartTime = compBar.StartTime,
                            TimeLabel = bTimeLabel,
                            IsBuy = imb.IsBuy,
                            LowPrice = imb.StartP,
                            HighPrice = imb.EndP,
                            Levels = imb.Run,
                            IsExhaustionAtBirth = isExhaustion,
                            AgeBars = 0,
                            IsTrapped = false
                        });
                    }
                }
            }

            // Update zone age
            foreach (var zone in _activeZones)
            {
                zone.AgeBars = completed.Count(c => c.StartTime > zone.BarStartTime);
            }

            // Prune expired zones & obsolete bar cache
            _activeZones.RemoveAll(z => z.AgeBars > MaxZoneAgeBars);
            if (completed.Length > 0)
            {
                _processedBars.IntersectWith(completed.Select(c => c.StartTime));
            }

            // Evaluate current price interaction with active zones
            foreach (var zone in _activeZones.OrderByDescending(z => z.BarStartTime))
            {
                if (zone.IsBuy)
                {
                    // Price broke below stacked buy imbalance -> Trapped Buyers (FADE setup)
                    if (evalPrice < zone.LowPrice - tickSize * 0.5)
                    {
                        zone.IsTrapped = true;
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "trapped_traders",
                            Title = zone.IsExhaustionAtBirth ? "Trapped Longs (Exhaustion Fade)" : "Trapped Buyers (Fade Short)",
                            Description = $"Price ({evalPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}) broke below stacked buy zone {zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}-{zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}; buyers trapped offside.",
                            Tone = "bearish",
                            Price = $"{zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)} - {zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = zone.TimeLabel
                        });
                    }
                    // Price testing the zone from above -> Potential support
                    else if (Math.Abs(evalPrice - zone.LowPrice) <= tickSize * 1.5 ||
                             (evalPrice >= zone.LowPrice && evalPrice <= zone.HighPrice + tickSize * 0.5))
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = "Retesting Buy Zone (Support)",
                            Description = $"Pullback testing buyer support at {zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}-{zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}.",
                            Tone = "bullish",
                            Price = $"{zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)} - {zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = zone.TimeLabel
                        });
                    }
                    // Fresh initiative buy zone holding
                    else if (evalPrice > zone.HighPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Buyer Support ({zone.Levels} lvls)",
                            Description = $"Stacked buy imbalance {zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}-{zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)} holding as support.",
                            Tone = "bullish",
                            Price = $"{zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)} - {zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = zone.TimeLabel
                        });
                    }
                }
                else
                {
                    // Price pushed above stacked sell imbalance -> Trapped Sellers (SQUEEZE setup)
                    if (evalPrice > zone.HighPrice + tickSize * 0.5)
                    {
                        zone.IsTrapped = true;
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "trapped_traders",
                            Title = zone.IsExhaustionAtBirth ? "Trapped Shorts (Exhaustion Squeeze)" : "Trapped Sellers (Squeeze Long)",
                            Description = $"Price ({evalPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}) pushed above stacked sell zone {zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}-{zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}; sellers trapped offside.",
                            Tone = "bullish",
                            Price = $"{zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)} - {zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = zone.TimeLabel
                        });
                    }
                    // Price testing the zone from below -> Potential resistance
                    else if (Math.Abs(evalPrice - zone.HighPrice) <= tickSize * 1.5 ||
                             (evalPrice <= zone.HighPrice && evalPrice >= zone.LowPrice - tickSize * 0.5))
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = "Retesting Sell Zone (Resistance)",
                            Description = $"Rally testing seller resistance at {zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}-{zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}.",
                            Tone = "bearish",
                            Price = $"{zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)} - {zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = zone.TimeLabel
                        });
                    }
                    // Fresh initiative sell zone holding
                    else if (evalPrice < zone.LowPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Seller Resistance ({zone.Levels} lvls)",
                            Description = $"Stacked sell imbalance {zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}-{zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)} holding as resistance.",
                            Tone = "bearish",
                            Price = $"{zone.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)} - {zone.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = zone.TimeLabel
                        });
                    }
                }
            }
        }

        // 5. Developing Live Bar Signals (Debounced: only when live bar has >= MinLiveTradesForSignal)
        if (current != null && current.TradeCount >= MinLiveTradesForSignal)
        {
            var liveImbalances = DetectStackedImbalances(current);
            var liveRange = current.High - current.Low;

            foreach (var imb in liveImbalances)
            {
                if (imb.IsBuy)
                {
                    bool isAtHigh = liveRange > 1e-6 &&
                                    (imb.EndP >= current.High - tickSize * 0.5 ||
                                     (imb.EndP - current.Low) / liveRange >= 0.75);

                    if (isAtHigh && (evalPrice < imb.EndP || current.Close < imb.EndP))
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "exhaustion",
                            Title = "[Developing] Buying Exhaustion at High",
                            Description = $"Aggressive buyers ({imb.Run} lvls) at bar high struggling to advance; fade candidate.",
                            Tone = "bearish",
                            Price = $"{imb.StartP.ToString("F" + dec, CultureInfo.InvariantCulture)} - {imb.EndP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = timeLabel
                        });
                    }
                    else if (evalPrice >= imb.EndP)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"[Developing] Stacked Buy Imbalance ({imb.Run} lvls)",
                            Description = $"Aggressive buyers lifting offers from {imb.StartP.ToString("F" + dec, CultureInfo.InvariantCulture)} to {imb.EndP.ToString("F" + dec, CultureInfo.InvariantCulture)}.",
                            Tone = "bullish",
                            Price = $"{imb.StartP.ToString("F" + dec, CultureInfo.InvariantCulture)} - {imb.EndP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = timeLabel
                        });
                    }
                }
                else
                {
                    bool isAtLow = liveRange > 1e-6 &&
                                   (imb.StartP <= current.Low + tickSize * 0.5 ||
                                    (imb.StartP - current.Low) / liveRange <= 0.25);

                    if (isAtLow && (evalPrice > imb.StartP || current.Close > imb.StartP))
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "exhaustion",
                            Title = "[Developing] Selling Exhaustion at Low",
                            Description = $"Aggressive sellers ({imb.Run} lvls) at bar low struggling to breakdown; squeeze candidate.",
                            Tone = "bullish",
                            Price = $"{imb.StartP.ToString("F" + dec, CultureInfo.InvariantCulture)} - {imb.EndP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = timeLabel
                        });
                    }
                    else if (evalPrice <= imb.StartP)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"[Developing] Stacked Sell Imbalance ({imb.Run} lvls)",
                            Description = $"Aggressive sellers hitting bids from {imb.StartP.ToString("F" + dec, CultureInfo.InvariantCulture)} to {imb.EndP.ToString("F" + dec, CultureInfo.InvariantCulture)}.",
                            Tone = "bearish",
                            Price = $"{imb.StartP.ToString("F" + dec, CultureInfo.InvariantCulture)} - {imb.EndP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = timeLabel
                        });
                    }
                }
            }
        }

        // 6. Passive Absorption Detection (subject bar)
        var barRange = bar.High - bar.Low;
        var priceChange = bar.Close - bar.Open;
        var barDelta = bar.Delta;
        var sellerAbsorption = false;
        var buyerAbsorption = false;

        if (bar.Volume >= MinSignalVolume)
        {
            if (barDelta >= MinAbsorptionDelta && (priceChange < 0 || (barRange > 0 && bar.Close <= bar.High - barRange * 0.40)))
            {
                sellerAbsorption = true;
                signals.Add(new OrderFlowSignal
                {
                    Type = "absorption",
                    Title = "Passive Seller Absorption (Fade)",
                    Description = $"Aggressive buyers lifted +Δ {FmtVol(barDelta)} into limit sellers at {bar.High.ToString("F" + dec, CultureInfo.InvariantCulture)}; buying absorbed.",
                    Tone = "bearish",
                    Price = bar.High.ToString("F" + dec, CultureInfo.InvariantCulture),
                    Time = timeLabel
                });
            }
            else if (barDelta <= -MinAbsorptionDelta && (priceChange > 0 || (barRange > 0 && bar.Close >= bar.Low + barRange * 0.40)))
            {
                buyerAbsorption = true;
                signals.Add(new OrderFlowSignal
                {
                    Type = "absorption",
                    Title = "Passive Buyer Absorption (Squeeze)",
                    Description = $"Aggressive sellers hit −Δ {FmtVol(-barDelta)} into limit buyers at {bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture)}; selling absorbed.",
                    Tone = "bullish",
                    Price = bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture),
                    Time = timeLabel
                });
            }
        }

        // 7. Delta Divergence Detection & Signal
        var deltaDivergence = "None";
        if (prevBar != null)
        {
            if (bar.High >= prevBar.High && bar.Delta < 0 && prevBar.Delta > 0)
            {
                deltaDivergence = "Bearish Divergence";
                signals.Add(new OrderFlowSignal
                {
                    Type = "divergence",
                    Title = "Bearish Delta Divergence (Fade)",
                    Description = $"New high reached at {bar.High.ToString("F" + dec, CultureInfo.InvariantCulture)} on negative delta ({FmtVol(bar.Delta)}); lack of buyer conviction at highs.",
                    Tone = "bearish",
                    Price = bar.High.ToString("F" + dec, CultureInfo.InvariantCulture),
                    Time = timeLabel
                });
            }
            else if (bar.Low <= prevBar.Low && bar.Delta > 0 && prevBar.Delta < 0)
            {
                deltaDivergence = "Bullish Divergence";
                signals.Add(new OrderFlowSignal
                {
                    Type = "divergence",
                    Title = "Bullish Delta Divergence (Squeeze)",
                    Description = $"New low reached at {bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture)} on positive delta (+{FmtVol(bar.Delta)}); lack of seller conviction at lows.",
                    Tone = "bullish",
                    Price = bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture),
                    Time = timeLabel
                });
            }
        }

        // 8. Deduplicate and Prioritize Signals (Trapped > Absorption > Divergence > Retest > Developing)
        var distinctSignals = new List<OrderFlowSignal>();
        var seenKeys = new HashSet<string>();
        foreach (var sig in signals)
        {
            var key = $"{sig.Type}:{sig.Tone}:{sig.Price}";
            if (seenKeys.Add(key))
            {
                distinctSignals.Add(sig);
            }
        }
        if (distinctSignals.Count > 5)
        {
            distinctSignals = distinctSignals.Take(5).ToList();
        }

        // 9. Synthesize Market Bias, Auction Status, and Context Narrative
        string bias;
        string biasTone;
        string auctionStatus;
        string narrative;

        var deltaPct = bar.Volume > 0 ? (double)(bar.Delta / bar.Volume) * 100.0 : 0.0;

        OrderFlowZone trappedBuy = null;
        OrderFlowZone trappedSell = null;
        lock (_syncLock)
        {
            trappedBuy = _activeZones.FirstOrDefault(z => z.IsBuy && z.IsTrapped);
            trappedSell = _activeZones.FirstOrDefault(z => !z.IsBuy && z.IsTrapped);
        }

        if (trappedBuy != null)
        {
            bias = "Trapped Buyers / Fade";
            biasTone = "bearish";
            auctionStatus = "Liquidity Trap / Order Flow Reversal";
            narrative = $"Buyers trapped above {trappedBuy.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}; price at {evalPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}. Look to fade.";
        }
        else if (trappedSell != null)
        {
            bias = "Trapped Sellers / Squeeze";
            biasTone = "bullish";
            auctionStatus = "Liquidity Trap / Order Flow Reversal";
            narrative = $"Sellers trapped below {trappedSell.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}; price at {evalPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}. Look to squeeze.";
        }
        else if (sellerAbsorption)
        {
            bias = "Seller Absorption (Fade)";
            biasTone = "bearish";
            auctionStatus = "Passive Liquidity Absorption";
            narrative = $"Seller absorption at {bar.High.ToString("F" + dec, CultureInfo.InvariantCulture)}, +Δ {FmtVol(bar.Delta)}. Buyers rejected.";
        }
        else if (buyerAbsorption)
        {
            bias = "Buyer Absorption (Squeeze)";
            biasTone = "bullish";
            auctionStatus = "Passive Liquidity Absorption";
            narrative = $"Buyer absorption at {bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture)}, −Δ {FmtVol(-bar.Delta)}. Sellers rejected.";
        }
        else if (deltaDivergence == "Bearish Divergence")
        {
            bias = "Bearish Divergence (Fade)";
            biasTone = "bearish";
            auctionStatus = "Exhaustion at Highs";
            narrative = $"New high at {bar.High.ToString("F" + dec, CultureInfo.InvariantCulture)} on negative delta. Lack of buyer conviction.";
        }
        else if (deltaDivergence == "Bullish Divergence")
        {
            bias = "Bullish Divergence (Squeeze)";
            biasTone = "bullish";
            auctionStatus = "Exhaustion at Lows";
            narrative = $"New low at {bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture)} on positive delta. Lack of seller conviction.";
        }
        else if (distinctSignals.Any(s => s.Tone == "bullish" && s.Type == "stacked_imbalance"))
        {
            bias = "Bullish Initiative";
            biasTone = "bullish";
            auctionStatus = "Aggressive Initiative Buying";
            narrative = $"Buyers in control holding above support, +Δ {FmtVol(bar.Delta)} at POC {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else if (distinctSignals.Any(s => s.Tone == "bearish" && s.Type == "stacked_imbalance"))
        {
            bias = "Bearish Initiative";
            biasTone = "bearish";
            auctionStatus = "Aggressive Initiative Selling";
            narrative = $"Sellers in control holding below resistance, −Δ {FmtVol(-bar.Delta)} at POC {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else
        {
            bias = "Rotational / Balance";
            biasTone = "neutral";
            auctionStatus = "Two-Way Auction Balance";
            narrative = $"Balanced market around POC {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }

        var result = new OrderFlowContext
        {
            HasBias = true, // Keep side card visible with active state
            Bias = bias,
            BiasTone = biasTone,
            AuctionStatus = auctionStatus,
            Narrative = narrative,
            CurrentDelta = bar.Delta,
            CurrentVolume = bar.Volume,
            DeltaPercent = deltaPct,
            PocPrice = pocPrice,
            PocPosition = pocPosition,
            PocMigration = pocMigration,
            CumulativeDelta = cumDelta,
            DeltaDivergence = deltaDivergence,
            Signals = distinctSignals
        };

        _lastContext = result;
        return result;
    }

    private static List<(bool IsBuy, double StartP, double EndP, int Run)> DetectStackedImbalances(FootprintBarData bar)
    {
        var list = new List<(bool IsBuy, double StartP, double EndP, int Run)>();
        var levels = bar?.Levels;
        if (levels == null || levels.Length < 2) return list;

        var buyImb = new bool[levels.Length];
        var sellImb = new bool[levels.Length];

        for (var i = 0; i < levels.Length - 1; i++)
        {
            var lLow = levels[i];
            var lHigh = levels[i + 1];

            // Buy Imbalance at i+1 (Ask at high vs Bid at low)
            if (lHigh.AskVolume >= MinImbalanceVol &&
                (lLow.BidVolume == 0 ||
                 (lHigh.AskVolume >= lLow.BidVolume * ImbalanceRatio && lHigh.AskVolume - lLow.BidVolume >= MinDeltaDiff)))
            {
                buyImb[i + 1] = true;
            }

            // Sell Imbalance at i (Bid at low vs Ask at high)
            if (lLow.BidVolume >= MinImbalanceVol &&
                (lHigh.AskVolume == 0 ||
                 (lLow.BidVolume >= lHigh.AskVolume * ImbalanceRatio && lLow.BidVolume - lHigh.AskVolume >= MinDeltaDiff)))
            {
                sellImb[i] = true;
            }
        }

        // Detect Stacked Buy
        var run = 0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (buyImb[i])
            {
                run++;
            }
            else
            {
                if (run >= StackedLevelsRequired)
                    list.Add((true, levels[i - run].Price, levels[i - 1].Price, run));
                run = 0;
            }
        }
        if (run >= StackedLevelsRequired)
            list.Add((true, levels[levels.Length - run].Price, levels[^1].Price, run));

        // Detect Stacked Sell
        run = 0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (sellImb[i])
            {
                run++;
            }
            else
            {
                if (run >= StackedLevelsRequired)
                    list.Add((false, levels[i - run].Price, levels[i - 1].Price, run));
                run = 0;
            }
        }
        if (run >= StackedLevelsRequired)
            list.Add((false, levels[levels.Length - run].Price, levels[^1].Price, run));

        return list;
    }

    /// <summary>
    /// Legacy compatibility helper mapping context into render lines.
    /// </summary>
    public static List<FootprintRenderLine> Analyze(FootprintSnapshot snapshot, double tickSize)
    {
        var ctx = AnalyzeContext(snapshot, tickSize);
        var lines = new List<FootprintRenderLine>();

        if (!string.IsNullOrEmpty(ctx.Narrative))
        {
            lines.Add(new FootprintRenderLine
            {
                Text = ctx.Narrative,
                Tone = ctx.BiasTone == "bullish" ? "up" : ctx.BiasTone == "bearish" ? "down" : "text"
            });
        }

        foreach (var sig in ctx.Signals)
        {
            lines.Add(new FootprintRenderLine
            {
                Text = $"{sig.Title}: {sig.Description}",
                Tone = sig.Tone == "bullish" ? "up" : sig.Tone == "bearish" ? "down" : "text"
            });
        }

        return lines;
    }

    public static string FmtVol(decimal v) =>
        Math.Abs(v) >= 1000
            ? (v / 1000m).ToString("0.0", CultureInfo.InvariantCulture) + "k"
            : v.ToString(CultureInfo.InvariantCulture);

    public static string Signed(double v, int dec) =>
        (v >= 0 ? "+" : "-") + Math.Abs(v).ToString("F" + dec, CultureInfo.InvariantCulture);

    public static int Decimals(double tickSize)
    {
        var s = tickSize.ToString(CultureInfo.InvariantCulture);
        var i = s.IndexOf('.');
        return i < 0 ? 0 : s.Length - i - 1;
    }
}
