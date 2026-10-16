using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace FootPrintTrader.Helper;

public class OrderFlowSignal
{
    public string Type { get; init; } = ""; // "stacked_imbalance", "absorption", "trapped_traders", "divergence", "unfinished_auction", "exhaustion"
    public string Title { get; init; } = "";
    public string Tone { get; init; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
}

public class OrderFlowZone
{
    public long BarStartTime { get; init; }
    public bool IsBuy { get; init; } // true = Buy Imbalance, false = Sell Imbalance
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
    public double CurrentDelta { get; init; }
    public double DeltaPercent { get; init; }
    public decimal CurrentVolume { get; init; }
    public string PocPosition { get; init; } = "-";
    public string PocMigration { get; init; } = "-";

    // Multi-bar / Session Structure
    public double CumulativeDelta { get; init; }
    public string DeltaDivergence { get; init; } = "None";

    // Active Signals
    public List<OrderFlowSignal> Signals { get; init; } = new();
}

public static class FootprintAnalyzer
{
    // ============================================================
    // TUNABLE SETTINGS
    // ------------------------------------------------------------
    // Ratio-based detection (industry standard, kept fixed).
    // Magnitude-based filters (relative, adaptive to market activity).
    // ============================================================

    // --- Diagonal imbalance (relative by nature — adjacent levels) ---
    private const decimal ImbalanceRatio = 3.0m; // 300% — Bookmap / GoCharting / FC Footprint default
    private const int StackedLevelsRequired = 3; // 3+ consecutive diagonal imbalances

    // --- Volume floors (relative to bar activity) ---
    private const double MinImbalanceVolRatio = 0.05; // level must carry >= 5% of bar's avg level vol
    // (noise filter only — NOT a signal threshold)

    // --- Absorption (relative to bar's own volume + baseline) ---
    private const double MinAbsorptionDeltaRatio = 0.25; // |delta| >= 25% of bar volume
    private const double MinAbsorptionVolumeRatio = 1.00; // bar vol >= 100% of 20-bar baseline

    // --- Rolling baseline ---
    private const int BaselineWindowBars = 20;

    // --- Debounce / structural (kept fixed — not magnitude thresholds) ---
    private const int MinLiveTradesForSignal = 250;
    private const int MaxZoneAgeBars = 2;

    // ============================================================
    // STATE
    // ============================================================
    private static readonly Lock _syncLock = new();
    private static readonly List<OrderFlowZone> _activeZones = new();
    private static readonly HashSet<long> _processedBars = new();
    private static readonly Queue<decimal> _recentBarVolumes = new();
    private static OrderFlowContext _lastContext;
    private static decimal _sessionCumulativeDelta;
    private static decimal _baselineBarVolume;

    /// <summary>
    /// Clears all session-scoped state. Call once at app startup.
    /// (Single-session app: process restart is sufficient, but explicit is safer.)
    /// </summary>
    public static void ResetSession()
    {
        lock (_syncLock)
        {
            _activeZones.Clear();
            _processedBars.Clear();
            _recentBarVolumes.Clear();
            _sessionCumulativeDelta = 0m;
            _baselineBarVolume = 0m;
            _lastContext = null;
        }
    }

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
        var evalPrice = currentPrice > 0 ? currentPrice : bar.Close;

        // POC Calculation & Zone Placement for current bar
        var levels = bar.Levels ?? Array.Empty<FootprintLevel>();
        var pocPrice = bar.Close;

        if (levels.Length > 0)
        {
            var pocLevel = levels.OrderByDescending(l => l.Total).First();
            pocPrice = pocLevel.Price;
        }

        // Live Bar Metrics
        var currentDelta = (double)bar.Delta;
        var currentVolume = bar.Volume;
        var deltaPercent = currentVolume > 0 ? currentDelta / (double)currentVolume * 100.0 : 0.0;

        var barRange = bar.High - bar.Low;
        string pocPosition;
        if (barRange > 1e-9)
        {
            var pos = (pocPrice - bar.Low) / barRange;
            pocPosition = pos >= 0.66 ? "High" : pos <= 0.33 ? "Low" : "Middle";
        }
        else pocPosition = "-";

        var pocMigration = "-";
        if (prevBar != null)
        {
            var prevPoc = prevBar.Close;
            var prevLevels = prevBar.Levels ?? Array.Empty<FootprintLevel>();
            if (prevLevels.Length > 0)
                prevPoc = prevLevels.OrderByDescending(l => l.Total).First().Price;
            if (pocPrice > prevPoc + tickSize * 0.5) pocMigration = "▲ Up";
            else if (pocPrice < prevPoc - tickSize * 0.5) pocMigration = "▼ Down";
            else pocMigration = "→ Flat";
        }

        // Multi-bar / Session Structure
        var liveDelta = current?.Delta ?? 0m;
        var cumulativeDelta = (double)(_sessionCumulativeDelta + liveDelta);

        // Update Stateful Order Flow Zones from Completed Bars
        lock (_syncLock)
        {
            // Register newly completed bars
            foreach (var compBar in completed)
            {
                if (_processedBars.Add(compBar.StartTime))
                {
                    _sessionCumulativeDelta += compBar.Delta;

                    // Maintain rolling baseline bar volume (20-bar average)
                    _recentBarVolumes.Enqueue(compBar.Volume);
                    while (_recentBarVolumes.Count > BaselineWindowBars)
                        _recentBarVolumes.Dequeue();
                    _baselineBarVolume = _recentBarVolumes.Count > 0
                        ? _recentBarVolumes.Average()
                        : compBar.Volume;

                    var barImbalances = DetectStackedImbalances(compBar, _baselineBarVolume);
                    var bRange = compBar.High - compBar.Low;

                    foreach (var imb in barImbalances)
                    {
                        bool isExhaustion;
                        if (imb.IsBuy)
                        {
                            isExhaustion = bRange > 1e-6 &&
                                           (imb.EndP >= compBar.High - tickSize * 0.5 ||
                                            (imb.EndP - compBar.Low) / bRange >= 0.75 ||
                                            compBar.Close < imb.EndP);
                        }
                        else
                        {
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

            // NOTE on _processedBars: IntersectWith(completed) can cause double-counting
            // if the window slides and a bar re-enters. Safer to only prune bars older
            // than the newest completed bar minus a generous lookback.
            if (completed.Length > 0)
            {
                var oldestInWindow = completed.Min(c => c.StartTime);
                _processedBars.RemoveWhere(t => t < oldestInWindow);
            }

            // Evaluate current price interaction with active zones
            foreach (var zone in _activeZones.OrderByDescending(z => z.BarStartTime))
            {
                if (zone.IsBuy)
                {
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
            var liveImbalances = DetectStackedImbalances(current, _baselineBarVolume);
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

        // ============================================================
        // Passive Absorption Detection (relative to bar's own volume + baseline)
        // ============================================================
        var priceChange = bar.Close - bar.Open;
        var barDelta = bar.Delta;
        var sellerAbsorption = false;
        var buyerAbsorption = false;

        var deltaRatio = bar.Volume > 0
            ? (double)Math.Abs(barDelta) / (double)bar.Volume
            : 0.0;

        var barVolumeRatio = _baselineBarVolume > 0
            ? (double)bar.Volume / (double)_baselineBarVolume
            : 1.0;

        var absorptionCandidate =
            barVolumeRatio >= MinAbsorptionVolumeRatio &&
            deltaRatio >= MinAbsorptionDeltaRatio;

        if (absorptionCandidate)
        {
            // Seller absorption: strong positive delta, price fails to rise / closes weak
            if (barDelta > 0 && (priceChange < 0 ||
                                 (barRange > 0 && bar.Close <= bar.High - barRange * 0.40)))
            {
                sellerAbsorption = true;
                signals.Add(new OrderFlowSignal
                {
                    Type = "absorption",
                    Title = $"Seller Absorption ({P(bar.High)})",
                    Tone = "bearish"
                });
            }
            // Buyer absorption: strong negative delta, price fails to fall / closes strong
            else if (barDelta < 0 && (priceChange > 0 ||
                                      (barRange > 0 && bar.Close >= bar.Low + barRange * 0.40)))
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

        // ============================================================
        // Trapped zone selection — most recent FIRST, nearest to price SECOND.
        // Rationale: a trapped zone is only actionable while it is the freshest
        // structural event AND price is still interacting with it. Picking the
        // oldest matching zone (the old FirstOrDefault) could surface a stale
        // trap from 2 bars ago while a fresher, opposite-side trap is in play.
        // ============================================================
        OrderFlowZone trappedBuy;
        OrderFlowZone trappedSell;
        lock (_syncLock)
        {
            trappedBuy = _activeZones
                .Where(z => z.IsBuy && z.IsTrapped)
                .OrderByDescending(z => z.BarStartTime)
                .ThenBy(z => Math.Abs(evalPrice - z.LowPrice))
                .FirstOrDefault();

            trappedSell = _activeZones
                .Where(z => !z.IsBuy && z.IsTrapped)
                .OrderByDescending(z => z.BarStartTime)
                .ThenBy(z => Math.Abs(evalPrice - z.HighPrice))
                .FirstOrDefault();
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
            Bias = bias,
            BiasTone = biasTone,
            Action = action,
            PocPrice = pocPrice,
            CurrentDelta = currentDelta,
            DeltaPercent = deltaPercent,
            CurrentVolume = currentVolume,
            PocPosition = pocPosition,
            PocMigration = pocMigration,
            CumulativeDelta = cumulativeDelta,
            DeltaDivergence = deltaDivergence,
            Signals = distinctSignals
        };

        _lastContext = result;
        return result;
    }

    // ============================================================
    // IMBALANCE DETECTION
    // ------------------------------------------------------------
    // Diagonal imbalance: Ask@N+1 vs Bid@N (buy) or Bid@N vs Ask@N+1 (sell).
    // Ratio gate is fixed at 3:1 (industry standard).
    // Volume floor scales with the bar's average level volume — a noise filter,
    // not a signal threshold.
    // ============================================================
    private static List<(bool IsBuy, double StartP, double EndP, int Run)>
        DetectStackedImbalances(FootprintBarData bar, decimal baselineBarVolume)
    {
        var list = new List<(bool IsBuy, double StartP, double EndP, int Run)>();
        var levels = bar?.Levels;
        if (levels == null || levels.Length < 2) return list;

        // Defensive: footprint feeds occasionally deliver unsorted levels.
        // Sort ascending by price so the diagonal comparison is valid.
        if (!IsSortedAscending(levels))
            levels = levels.OrderBy(l => l.Price).ToArray();

        // Minimum level volume = X% of the bar's average level volume.
        // This only filters out near-empty rows; the 3:1 ratio does the real work.
        var avgLevelVol = bar.Volume / levels.Length;
        var minLevelVol = avgLevelVol * (decimal)MinImbalanceVolRatio;

        var buyImb = new bool[levels.Length];
        var sellImb = new bool[levels.Length];

        for (var i = 0; i < levels.Length - 1; i++)
        {
            var lLow = levels[i];
            var lHigh = levels[i + 1];

            // Buy imbalance at i+1 (Ask at high vs Bid at low)
            if (lHigh.AskVolume >= minLevelVol &&
                (lLow.BidVolume == 0 ||
                 lHigh.AskVolume >= lLow.BidVolume * ImbalanceRatio))
            {
                buyImb[i + 1] = true;
            }

            // Sell imbalance at i (Bid at low vs Ask at high)
            if (lLow.BidVolume >= minLevelVol &&
                (lHigh.AskVolume == 0 ||
                 lLow.BidVolume >= lHigh.AskVolume * ImbalanceRatio))
            {
                sellImb[i] = true;
            }
        }

        // Detect Stacked Buy
        var run = 0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (buyImb[i]) run++;
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
            if (sellImb[i]) run++;
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

    private static bool IsSortedAscending(FootprintLevel[] levels)
    {
        for (var i = 1; i < levels.Length; i++)
            if (levels[i].Price < levels[i - 1].Price)
                return false;
        return true;
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