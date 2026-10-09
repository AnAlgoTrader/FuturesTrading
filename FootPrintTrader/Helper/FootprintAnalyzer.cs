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
    public string PocMigration { get; set; } = "Overlapping";

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

    private static OrderFlowContext _lastContext;

    public static OrderFlowContext AnalyzeContext(FootprintSnapshot snapshot, double tickSize)
    {
        if (snapshot == null)
            return _lastContext ?? new OrderFlowContext();

        var completed = snapshot.Completed ?? Array.Empty<FootprintBarData>();
        var current = snapshot.Current;

        // Choose subject bar: live bar if active enough, otherwise last completed bar
        FootprintBarData bar = null;
        FootprintBarData prevBar = null;

        if (current != null && current.Volume >= 20)
        {
            bar = current;
            prevBar = completed.LastOrDefault();
        }
        else if (completed.Length > 0)
        {
            bar = completed[^1];
            prevBar = completed.Length > 1 ? completed[^2] : null;
        }

        if (bar == null)
            return _lastContext ?? new OrderFlowContext();

        var dec = Decimals(tickSize);
        var signals = new List<OrderFlowSignal>();
        var timeLabel = FootprintRenderBuilder.FormatTime(bar.StartTime);

        // 1. Cumulative Delta
        var cumDelta = completed.Sum(b => b.Delta) + (current?.Delta ?? 0m);

        // 2. POC Calculation & Zone Placement
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
                if (ratio >= 0.70) pocPosition = "Top (Upper Range)";
                else if (ratio <= 0.30) pocPosition = "Bottom (Lower Range)";
                else pocPosition = "Middle (Value Area)";
            }
        }

        // 3. POC Migration
        var pocMigration = "Overlapping ◄►";
        if (prevBar != null && prevBar.Levels != null && prevBar.Levels.Length > 0)
        {
            var prevPoc = prevBar.Levels.OrderByDescending(l => l.Total).First().Price;
            var diff = pocPrice - prevPoc;
            if (diff >= tickSize * 0.75) pocMigration = "Rising ▲";
            else if (diff <= -tickSize * 0.75) pocMigration = "Falling ▼";
            else pocMigration = "Overlapping ◄►";
        }

        // 4. Diagonal Imbalances & Stacked Imbalances
        var hasStackedBuy = false;
        var hasStackedSell = false;

        if (levels.Length >= 2)
        {
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

            // Detect Stacked Buying Imbalances (runs >= StackedLevelsRequired)
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
                    {
                        var startP = levels[i - run].Price;
                        var endP = levels[i - 1].Price;
                        hasStackedBuy = true;

                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Stacked Buy Imbalance ({run} lvls)",
                            Description = $"Aggressive buyers lifting offers from {startP.ToString("F" + dec, CultureInfo.InvariantCulture)} to {endP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Tone = "bullish",
                            Price = $"{startP.ToString("F" + dec, CultureInfo.InvariantCulture)} - {endP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = timeLabel
                        });
                    }
                    run = 0;
                }
            }
            if (run >= StackedLevelsRequired)
            {
                var startP = levels[levels.Length - run].Price;
                var endP = levels[^1].Price;
                hasStackedBuy = true;

                signals.Add(new OrderFlowSignal
                {
                    Type = "stacked_imbalance",
                    Title = $"Stacked Buy Imbalance ({run} lvls)",
                    Description = $"Aggressive buyers lifting offers from {startP.ToString("F" + dec, CultureInfo.InvariantCulture)} to {endP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                    Tone = "bullish",
                    Price = $"{startP.ToString("F" + dec, CultureInfo.InvariantCulture)} - {endP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                    Time = timeLabel
                });
            }

            // Detect Stacked Selling Imbalances
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
                    {
                        var startP = levels[i - run].Price;
                        var endP = levels[i - 1].Price;
                        hasStackedSell = true;

                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Stacked Sell Imbalance ({run} lvls)",
                            Description = $"Aggressive sellers hitting bids from {startP.ToString("F" + dec, CultureInfo.InvariantCulture)} to {endP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Tone = "bearish",
                            Price = $"{startP.ToString("F" + dec, CultureInfo.InvariantCulture)} - {endP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                            Time = timeLabel
                        });
                    }
                    run = 0;
                }
            }
            if (run >= StackedLevelsRequired)
            {
                var startP = levels[levels.Length - run].Price;
                var endP = levels[^1].Price;
                hasStackedSell = true;

                signals.Add(new OrderFlowSignal
                {
                    Type = "stacked_imbalance",
                    Title = $"Stacked Sell Imbalance ({run} lvls)",
                    Description = $"Aggressive sellers hitting bids from {startP.ToString("F" + dec, CultureInfo.InvariantCulture)} to {endP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                    Tone = "bearish",
                    Price = $"{startP.ToString("F" + dec, CultureInfo.InvariantCulture)} - {endP.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                    Time = timeLabel
                });
            }
        }

        // 5. Passive Absorption
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
                    Title = "Passive Seller Absorption",
                    Description = $"Aggressive buyers lifted +Δ {FmtVol(barDelta)} but failed to advance price; sellers absorbing at {bar.High.ToString("F" + dec, CultureInfo.InvariantCulture)}",
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
                    Title = "Passive Buyer Absorption",
                    Description = $"Aggressive sellers hit −Δ {FmtVol(-barDelta)} but failed to breakdown price; buyers absorbing at {bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture)}",
                    Tone = "bullish",
                    Price = bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture),
                    Time = timeLabel
                });
            }
        }

        // 6. Delta Divergence
        var deltaDivergence = "None";
        if (prevBar != null)
        {
            if (bar.High >= prevBar.High && bar.Delta < 0 && prevBar.Delta > 0)
                deltaDivergence = "Bearish Delta Divergence";
            else if (bar.Low <= prevBar.Low && bar.Delta > 0 && prevBar.Delta < 0)
                deltaDivergence = "Bullish Delta Divergence";
        }

        // 9. Synthesize Market Bias, Auction Status, and Context Narrative
        string bias;
        string biasTone;
        string auctionStatus;
        string narrative;

        var deltaPct = bar.Volume > 0 ? (double)(bar.Delta / bar.Volume) * 100.0 : 0.0;

        if (sellerAbsorption)
        {
            bias = "Seller Absorption";
            biasTone = "bearish";
            auctionStatus = "Passive Liquidity Absorption";
            narrative = $"Buyers aggressively lifted offers near {bar.High.ToString("F" + dec, CultureInfo.InvariantCulture)} (+Δ {FmtVol(bar.Delta)}), but met heavy passive limit resistance. Price rejected downward.";
        }
        else if (buyerAbsorption)
        {
            bias = "Buyer Absorption";
            biasTone = "bullish";
            auctionStatus = "Passive Liquidity Absorption";
            narrative = $"Sellers hit bids near {bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture)} (−Δ {FmtVol(-bar.Delta)}), but were absorbed by passive limit bids. Price rotated higher.";
        }
        else if (hasStackedBuy && hasStackedSell)
        {
            bias = "Two-Sided Imbalance";
            biasTone = "warning";
            auctionStatus = "Conflicting Order Flow";
            narrative = $"Both stacked buying and selling imbalances present around POC {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}. Directional conviction is low.";
        }
        else if (hasStackedBuy && bar.Close >= bar.Open && bar.Delta > 0)
        {
            bias = "Bullish Initiative";
            biasTone = "bullish";
            auctionStatus = "Aggressive Initiative Buying";
            narrative = $"Aggressive buyers driving price higher with stacked buying imbalances and +Δ {FmtVol(bar.Delta)} ({deltaPct:+0.0}%). POC migrating up to {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}.";
        }
        else if (hasStackedSell && bar.Close <= bar.Open && bar.Delta < 0)
        {
            bias = "Bearish Initiative";
            biasTone = "bearish";
            auctionStatus = "Aggressive Initiative Selling";
            narrative = $"Aggressive sellers in control with stacked selling imbalances and −Δ {FmtVol(-bar.Delta)} ({deltaPct:+0.0}%). POC migrating down to {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}.";
        }
        else
        {
            bias = "Rotational / Balance";
            biasTone = "neutral";
            auctionStatus = "Two-Way Auction Balance";
            narrative = $"No stacked imbalance on the current bar. Two-way auction in balance around POC {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}.";
        }

        var result = new OrderFlowContext
        {
            HasBias = hasStackedBuy || hasStackedSell || sellerAbsorption || buyerAbsorption,
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
            Signals = signals
        };

        _lastContext = result;
        return result;
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
