using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace FootPrintTrader.Helper;

public class OrderFlowSignal
{
    public string Type { get; init; } = "";        // "stacked_imbalance", "absorption", "trapped_traders", "divergence", "unfinished_auction", "exhaustion"
    public string Title { get; init; } = "";
    public string Tone { get; init; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
}

public class OrderFlowZone
{
    public long BarStartTime { get; init; }
    public bool IsBuy { get; init; }               // true = Buy Imbalance, false = Sell Imbalance
    public double LowPrice { get; init; }
    public double HighPrice { get; init; }
    public bool IsExhaustionAtBirth { get; init; } // Formed at bar extreme (top for buy, bottom for sell)
    public int AgeBars { get; set; }
    public bool IsTrapped { get; set; }
}

public class OrderFlowContext
{
    public string Bias { get; init; } = "Neutral";
    public string BiasTone { get; init; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
    public string Action { get; init; } = "";

    // Live Bar Metrics
    public double PocPrice { get; init; }

    // Multi-bar / Session Structure
    public string DeltaDivergence { get; init; } = "None";

    // Active Signals
    public List<OrderFlowSignal> Signals { get; init; } = new();
}

public static class FootprintAnalyzer
{
    private const decimal MinSignalVolume = 100m;
    private const decimal MinAbsorptionDelta = 40m;
    private const decimal ImbalanceRatio = 3.0m;
    private const decimal MinImbalanceVol = 10m;
    private const decimal MinDeltaDiff = 5m;
    private const int StackedLevelsRequired = 3;
    private const int MinLiveTradesForSignal = 250;
    private const int MaxZoneAgeBars = 2;

    private static readonly Lock _syncLock = new();
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
        var bar = current ?? completed.LastOrDefault();
        FootprintBarData prevBar = current != null
            ? completed.LastOrDefault()
            : (completed.Length > 1 ? completed[^2] : null);

        if (bar == null)
            return _lastContext ?? new OrderFlowContext();

        var dec = Decimals(tickSize);
        string P(double v) => v.ToString("F" + dec, CultureInfo.InvariantCulture);
        var signals = new List<OrderFlowSignal>();
        FootprintRenderBuilder.FormatTime(bar.StartTime);
        var evalPrice = currentPrice > 0 ? currentPrice : bar.Close;

        // POC Calculation & Zone Placement for current bar
        var levels = bar.Levels ?? Array.Empty<FootprintLevel>();
        var pocPrice = bar.Close;

        if (levels.Length > 0)
        {
            var pocLevel = levels.OrderByDescending(l => l.Total).First();
            pocPrice = pocLevel.Price;
        }

        // Update Stateful Order Flow Zones from Completed Bars
        lock (_syncLock)
        {
            // Register newly completed bars
            foreach (var compBar in completed)
            {
                if (_processedBars.Add(compBar.StartTime))
                {
                    var barImbalances = DetectStackedImbalances(compBar);
                    var bRange = compBar.High - compBar.Low;

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
                            IsBuy = imb.IsBuy,
                            LowPrice = imb.StartP,
                            HighPrice = imb.EndP,
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
                            Title = zone.IsExhaustionAtBirth ? $"Trapped Longs ({P(zone.LowPrice)})" : $"Trapped Buyers ({P(zone.LowPrice)})",
                            Tone = "bearish"
                        });
                    }
                    // Price testing the zone from above -> Potential support
                    else if (Math.Abs(evalPrice - zone.LowPrice) <= tickSize * 1.5 ||
                             (evalPrice >= zone.LowPrice && evalPrice <= zone.HighPrice + tickSize * 0.5))
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Retesting Buy Zone ({P(zone.LowPrice)})",
                            Tone = "bullish"
                        });
                    }
                    // Fresh initiative buy zone holding
                    else if (evalPrice > zone.HighPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Buyer Support ({P(zone.LowPrice)})",
                            Tone = "bullish"
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
                            Title = zone.IsExhaustionAtBirth ? $"Trapped Shorts ({P(zone.HighPrice)})" : $"Trapped Sellers ({P(zone.HighPrice)})",
                            Tone = "bullish"
                        });
                    }
                    // Price testing the zone from below -> Potential resistance
                    else if (Math.Abs(evalPrice - zone.HighPrice) <= tickSize * 1.5 ||
                             (evalPrice <= zone.HighPrice && evalPrice >= zone.LowPrice - tickSize * 0.5))
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Retesting Sell Zone ({P(zone.HighPrice)})",
                            Tone = "bearish"
                        });
                    }
                    // Fresh initiative sell zone holding
                    else if (evalPrice < zone.LowPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Seller Resistance ({P(zone.HighPrice)})",
                            Tone = "bearish"
                        });
                    }
                }
            }
        }

        // Developing Live Bar Signals (Debounced: only when live bar has >= MinLiveTradesForSignal)
        if (current is { TradeCount: >= MinLiveTradesForSignal })
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
                            Title = $"Buying Exhaustion at High ({P(imb.EndP)})",
                            Tone = "bearish"
                        });
                    }
                    else if (evalPrice >= imb.EndP)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Stacked Buy Imbalance ({P(imb.EndP)})",
                            Tone = "bullish"
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
                            Title = $"Selling Exhaustion at Low ({P(imb.StartP)})",
                            Tone = "bullish"
                        });
                    }
                    else if (evalPrice <= imb.StartP)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Stacked Sell Imbalance ({P(imb.StartP)})",
                            Tone = "bearish"
                        });
                    }
                }
            }
        }

        // Passive Absorption Detection (subject bar)
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
                    Title = $"Seller Absorption ({P(bar.High)})",
                    Tone = "bearish"
                });
            }
            else if (barDelta <= -MinAbsorptionDelta && (priceChange > 0 || (barRange > 0 && bar.Close >= bar.Low + barRange * 0.40)))
            {
                buyerAbsorption = true;
                signals.Add(new OrderFlowSignal
                {
                    Type = "absorption",
                    Title = $"Buyer Absorption ({P(bar.Low)})",
                    Tone = "bullish"
                });
            }
        }

        // Delta Divergence Detection & Signal
        var deltaDivergence = "None";
        if (prevBar != null)
        {
            if (bar.High >= prevBar.High && bar.Delta < 0 && prevBar.Delta > 0)
            {
                deltaDivergence = "Bearish";
                signals.Add(new OrderFlowSignal
                {
                    Type = "divergence",
                    Title = $"Bearish Divergence ({P(bar.High)})",
                    Tone = "bearish"
                });
            }
            else if (bar.Low <= prevBar.Low && bar.Delta > 0 && prevBar.Delta < 0)
            {
                deltaDivergence = "Bullish";
                signals.Add(new OrderFlowSignal
                {
                    Type = "divergence",
                    Title = $"Bullish Divergence ({P(bar.Low)})",
                    Tone = "bullish"
                });
            }
        }

        // Deduplicate and Prioritize Signals (Trapped > Absorption > Divergence > Retest > Developing)
        var distinctSignals = new List<OrderFlowSignal>();
        var seenKeys = new HashSet<string>();
        foreach (var sig in signals)
        {
            var key = $"{sig.Type}:{sig.Tone}:{sig.Title}";
            if (seenKeys.Add(key))
            {
                distinctSignals.Add(sig);
            }
        }
        if (distinctSignals.Count > 5)
        {
            distinctSignals = distinctSignals.Take(5).ToList();
        }

        // Synthesize Market Bias and the Action (what to do, at what level)
        string bias;
        string biasTone;
        string action;

        OrderFlowZone trappedBuy;
        OrderFlowZone trappedSell;
        lock (_syncLock)
        {
            trappedBuy = _activeZones.FirstOrDefault(z => z.IsBuy && z.IsTrapped);
            trappedSell = _activeZones.FirstOrDefault(z => !z.IsBuy && z.IsTrapped);
        }

        if (trappedBuy != null)
        {
            bias = "Trapped Buyers";
            biasTone = "bearish";
            action = $"Fade at {trappedBuy.LowPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else if (trappedSell != null)
        {
            bias = "Trapped Sellers";
            biasTone = "bullish";
            action = $"Squeeze at {trappedSell.HighPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else if (sellerAbsorption)
        {
            bias = "Seller Absorption";
            biasTone = "bearish";
            action = $"Fade at {bar.High.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else if (buyerAbsorption)
        {
            bias = "Buyer Absorption";
            biasTone = "bullish";
            action = $"Squeeze at {bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else if (deltaDivergence == "Bearish")
        {
            bias = "Bearish Divergence";
            biasTone = "bearish";
            action = $"Fade at {bar.High.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else if (deltaDivergence == "Bullish")
        {
            bias = "Bullish Divergence";
            biasTone = "bullish";
            action = $"Squeeze at {bar.Low.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else if (distinctSignals.Any(s => s.Tone == "bullish" && s.Type == "stacked_imbalance"))
        {
            bias = "Bullish Initiative";
            biasTone = "bullish";
            action = $"Buy at {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else if (distinctSignals.Any(s => s.Tone == "bearish" && s.Type == "stacked_imbalance"))
        {
            bias = "Bearish Initiative";
            biasTone = "bearish";
            action = $"Sell at {pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture)}";
        }
        else
        {
            bias = "Rotational / Balance";
            biasTone = "neutral";
            action = "Stand Aside";
        }

        var result = new OrderFlowContext
        {
            // Keep side card visible with active state
            Bias = bias,
            BiasTone = biasTone,
            Action = action,
            PocPrice = pocPrice,
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

        if (!string.IsNullOrEmpty(ctx.Action))
        {
            lines.Add(new FootprintRenderLine
            {
                Text = $"{ctx.Bias}: {ctx.Action}",
                Tone = ctx.BiasTone == "bullish" ? "up" : ctx.BiasTone == "bearish" ? "down" : "text"
            });
        }

        foreach (var sig in ctx.Signals)
        {
            lines.Add(new FootprintRenderLine
            {
                Text = sig.Title,
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
