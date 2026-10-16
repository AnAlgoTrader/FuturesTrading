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
    public double Price { get; init; }         // actionable level the signal refers to
    public int Priority { get; init; } = 5;    // lower = more important (see FootprintAnalyzer.Priority*)
    public string Description { get; init; } = ""; // one-line "why" shown under the title
    public string Time { get; init; } = "";    // HH:mm of the bar the signal came from
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
    public double ActionPrice { get; init; } // single source of truth for the actionable level

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

    // --- Absolute volume floors (quiet-market gate; from the v1/v2 analyzer) ---
    // Ratios alone normalize thin data into looking meaningful. A bar/level must clear
    // an absolute contract minimum before any signal is emitted from it.
    private const decimal MinSignalVolume = 100m; // bar volume >= 100 contracts
    private const decimal MinImbalanceVol = 10m;  // a level must carry >= 10 contracts

    // --- Absorption (real-time, per price level; Bookmap / MZpack style) ---
    // A level is absorbing when one side's aggressor volume is an outlier versus the
    // recent per-level volume distribution, price stalls at the extreme, and price
    // then rejects by AbsorptionDepthTicks. Evaluated on the developing bar.
    private const double AbsorptionLevelSigma = 3.0;  // level vol >= mean + 3σ of recent level volumes
    private const int AbsorptionDepthTicks = 2;       // price must reject >= 2 ticks from the level

    // --- Divergence (windowed CVD vs price; confirmed at bar close) ---
    private const int DivergenceWindowBars = 20;

    // --- Unfinished auction (per-bar extreme; both sides printed == unfinished) ---
    // Majority footprint convention (DeepCharts / LuxAlgo / OrderVane / Quantower): a
    // completed bar's high is "finished" when only aggressive buyers printed there (bid
    // volume == 0); if sellers also hit bids at the high the auction is unfinished — an
    // unfinished-business reference price the market tends to revisit. Mirror for the low
    // (ask volume > 0). Evaluated on closed bars only. (An inverted convention exists that
    // calls the one-sided cell unfinished; we follow the majority.) The min-side filter
    // ignores one-lot dust; candidates persist until a later bar trades back through them.
    // Ledger keeps up to 4 unrepaired references, but only the one nearest the current
    // price is emitted, at PriorityUnfinished — it is a reference/magnet, never a bias.
    private const double UnfinishedAuctionMinSideRatio = 0.05; // opposite side >= 5% of avg level vol
    private const int MaxUnfinishedAuctions = 4;

    // --- Rolling baselines ---
    private const int BaselineWindowBars = 20;
    private const int LevelVolumeWindow = 2000;       // rolling per-level volume samples for mean/σ

    // --- Signal priority (lower = more important) ---
    public const int PriorityTrapped = 1;
    public const int PriorityAbsorption = 2;
    public const int PriorityDivergence = 3;
    public const int PriorityRetest = 4;
    public const int PriorityDeveloping = 5;
    public const int PriorityUnfinished = 6;          // reference only — never drives bias

    // --- Debounce / structural (kept fixed — not magnitude thresholds) ---
    private const int MinLiveTradesForSignal = 250;
    private const int MaxZoneAgeBars = 8;

    // ============================================================
    // STATE
    // ============================================================
    private static readonly Lock _syncLock = new();
    private static readonly List<OrderFlowZone> _activeZones = new();
    private static readonly HashSet<long> _processedBars = new();
    private static readonly Queue<decimal> _recentBarVolumes = new();
    private static readonly Queue<decimal> _recentLevelVolumes = new();
    private static OrderFlowContext _lastContext;
    private static decimal _sessionCumulativeDelta;
    private static decimal _baselineBarVolume;
    private static double _levelVolMean;
    private static double _levelVolStd;

    // Absorption candidates detected on a developing/recent bar; confirmed when price rejects.
    private static readonly List<AbsorptionCandidate> _absorptionCandidates = new();

    // Persistent unfinished-auction references (unrepaired bar extremes where both sides traded).
    private static readonly List<UnfinishedAuction> _unfinishedAuctions = new();

    // Rolling window of completed bars (price extremes + cumulative delta at close) for divergence.
    private static readonly List<DivergencePoint> _divergenceWindow = new();
    private static string _divergence = "None";
    private static double _divergencePrice;
    private static long _divergenceBarStart;

    private sealed class AbsorptionCandidate
    {
        public bool SellerAbsorption; // true = aggressive buying absorbed at a high (bearish)
        public double Price;
        public long BarStart;
    }

    private sealed class UnfinishedAuction
    {
        public bool IsHigh;   // true = unfinished high (both sides at the high), false = unfinished low
        public double Price;
        public long BarStart;
    }

    private readonly record struct DivergencePoint(double High, double Low, decimal Cvd, decimal Delta, long Start);

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
        string T(long unixSeconds) => FootprintRenderBuilder.FormatTime(unixSeconds);
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

                    // Maintain rolling per-level volume distribution (mean/σ) for absorption.
                    foreach (var lvl in compBar.Levels)
                        _recentLevelVolumes.Enqueue(lvl.Total);
                    while (_recentLevelVolumes.Count > LevelVolumeWindow)
                        _recentLevelVolumes.Dequeue();
                    UpdateLevelVolumeStats();

                    // Maintain the divergence window (price extremes + CVD at bar close).
                    _divergenceWindow.Add(new DivergencePoint(compBar.High, compBar.Low, _sessionCumulativeDelta, compBar.Delta, compBar.StartTime));
                    while (_divergenceWindow.Count > DivergenceWindowBars)
                        _divergenceWindow.RemoveAt(0);

                    // Unfinished auctions: retire any active extreme this completed bar has
                    // traded back through (repair), then test this bar's own extremes.
                    _unfinishedAuctions.RemoveAll(u =>
                        u.BarStart < compBar.StartTime &&
                        u.Price >= compBar.Low - tickSize * 0.5 &&
                        u.Price <= compBar.High + tickSize * 0.5);
                    DetectUnfinishedAuctions(compBar, tickSize);

                    var barImbalances = compBar.Volume >= MinSignalVolume
                        ? DetectStackedImbalances(compBar, _baselineBarVolume)
                        : new List<(bool IsBuy, double StartP, double EndP, int Run)>();
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

            // Recompute CVD-vs-price divergence from the confirmed completed-bar window.
            ComputeDivergence(tickSize);

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
                _unfinishedAuctions.RemoveAll(u => u.BarStart < oldestInWindow);
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
                            Tone = "bearish",
                            Price = zone.LowPrice,
                            Priority = PriorityTrapped,
                            Description = $"Price traded below the buy imbalance at {P(zone.LowPrice)}; buyers are stuck offside.",
                            Time = T(zone.BarStartTime)
                        });
                    }
                    else if (Math.Abs(evalPrice - zone.LowPrice) <= tickSize * 1.5 ||
                             (evalPrice >= zone.LowPrice && evalPrice <= zone.HighPrice + tickSize * 0.5))
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Retesting Buy Zone ({P(zone.LowPrice)})",
                            Tone = "bullish",
                            Price = zone.LowPrice,
                            Priority = PriorityRetest,
                            Description = $"Pullback testing the buy imbalance zone {P(zone.LowPrice)}–{P(zone.HighPrice)} from above.",
                            Time = T(zone.BarStartTime)
                        });
                    }
                    else if (evalPrice > zone.HighPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Buyer Support ({P(zone.LowPrice)})",
                            Tone = "bullish",
                            Price = zone.LowPrice,
                            Priority = PriorityRetest,
                            Description = $"Fresh buy imbalance {P(zone.LowPrice)}–{P(zone.HighPrice)} holding as support.",
                            Time = T(zone.BarStartTime)
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
                            Tone = "bullish",
                            Price = zone.HighPrice,
                            Priority = PriorityTrapped,
                            Description = $"Price traded above the sell imbalance at {P(zone.HighPrice)}; sellers are stuck offside.",
                            Time = T(zone.BarStartTime)
                        });
                    }
                    else if (Math.Abs(evalPrice - zone.HighPrice) <= tickSize * 1.5 ||
                             (evalPrice <= zone.HighPrice && evalPrice >= zone.LowPrice - tickSize * 0.5))
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Retesting Sell Zone ({P(zone.HighPrice)})",
                            Tone = "bearish",
                            Price = zone.HighPrice,
                            Priority = PriorityRetest,
                            Description = $"Rally testing the sell imbalance zone {P(zone.LowPrice)}–{P(zone.HighPrice)} from below.",
                            Time = T(zone.BarStartTime)
                        });
                    }
                    else if (evalPrice < zone.LowPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Seller Resistance ({P(zone.HighPrice)})",
                            Tone = "bearish",
                            Price = zone.HighPrice,
                            Priority = PriorityRetest,
                            Description = $"Fresh sell imbalance {P(zone.LowPrice)}–{P(zone.HighPrice)} holding as resistance.",
                            Time = T(zone.BarStartTime)
                        });
                    }
                }
            }
        }

        // Developing Live Bar Signals (Debounced: only when live bar has >= MinLiveTradesForSignal)
        if (current is { TradeCount: >= MinLiveTradesForSignal, Volume: >= MinSignalVolume })
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
                            Tone = "bearish",
                            Price = imb.EndP,
                            Priority = PriorityRetest,
                            Description = $"Stacked buying into the bar high at {P(imb.EndP)} with price failing to advance; fade candidate.",
                            Time = T(current.StartTime)
                        });
                    }
                    else if (evalPrice >= imb.EndP)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Stacked Buy Imbalance ({P(imb.EndP)})",
                            Tone = "bullish",
                            Price = imb.EndP,
                            Priority = PriorityDeveloping,
                            Description = $"3+ consecutive diagonal buy imbalances topping out at {P(imb.EndP)}; buyers lifting offers.",
                            Time = T(current.StartTime)
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
                            Tone = "bullish",
                            Price = imb.StartP,
                            Priority = PriorityRetest,
                            Description = $"Stacked selling into the bar low at {P(imb.StartP)} with price failing to break down; squeeze candidate.",
                            Time = T(current.StartTime)
                        });
                    }
                    else if (evalPrice <= imb.StartP)
                    {
                        signals.Add(new OrderFlowSignal
                        {
                            Type = "stacked_imbalance",
                            Title = $"Stacked Sell Imbalance ({P(imb.StartP)})",
                            Tone = "bearish",
                            Price = imb.StartP,
                            Priority = PriorityDeveloping,
                            Description = $"3+ consecutive diagonal sell imbalances bottoming at {P(imb.StartP)}; sellers hitting bids.",
                            Time = T(current.StartTime)
                        });
                    }
                }
            }
        }

        // ============================================================
        // Absorption (real-time, per price level; Bookmap / MZpack style)
        // A level absorbs when one side's aggressor volume is an outlier versus the
        // recent per-level volume distribution AND price then rejects by AbsorptionDepthTicks.
        // Runs on the developing bar, so it does NOT wait for the bar to close.
        // ============================================================
        var absorptionThreshold = _levelVolMean + AbsorptionLevelSigma * _levelVolStd;

        if (absorptionThreshold > 0 && levels.Length > 0 &&
            bar.TradeCount >= MinLiveTradesForSignal && bar.Volume >= MinSignalVolume)
        {
            var depth = tickSize * AbsorptionDepthTicks;

            lock (_syncLock)
            {
                // Register candidate levels on the developing bar.
                foreach (var lvl in levels)
                {
                    // Seller absorption: heavy aggressive buying at/near the high, price stalls.
                    if ((double)lvl.AskVolume >= absorptionThreshold &&
                        lvl.Price >= bar.High - depth + 1e-9 &&
                        !_absorptionCandidates.Any(c => c.SellerAbsorption && c.BarStart == bar.StartTime && Math.Abs(c.Price - lvl.Price) < tickSize * 0.5))
                    {
                        _absorptionCandidates.Add(new AbsorptionCandidate { SellerAbsorption = true, Price = lvl.Price, BarStart = bar.StartTime });
                    }

                    // Buyer absorption: heavy aggressive selling at/near the low, price stalls.
                    if ((double)lvl.BidVolume >= absorptionThreshold &&
                        lvl.Price <= bar.Low + depth + 1e-9 &&
                        !_absorptionCandidates.Any(c => !c.SellerAbsorption && c.BarStart == bar.StartTime && Math.Abs(c.Price - lvl.Price) < tickSize * 0.5))
                    {
                        _absorptionCandidates.Add(new AbsorptionCandidate { SellerAbsorption = false, Price = lvl.Price, BarStart = bar.StartTime });
                    }
                }

                // Keep candidates only for the current bar and the most recent completed bar.
                var prevStart = completed.Length > 0 ? completed[^1].StartTime : bar.StartTime;
                _absorptionCandidates.RemoveAll(c => c.BarStart != bar.StartTime && c.BarStart != prevStart);

                // Confirm by rejection: price moved >= depth away from the absorbed level.
                // Confirmed candidates are kept (and re-emitted each tick) until their bar
                // ages out, so the signal persists rather than flickering for one second.
                foreach (var c in _absorptionCandidates)
                {
                    var rejected = c.SellerAbsorption
                        ? evalPrice <= c.Price - depth
                        : evalPrice >= c.Price + depth;
                    if (!rejected) continue;

                    signals.Add(new OrderFlowSignal
                    {
                        Type = "absorption",
                        Title = $"{(c.SellerAbsorption ? "Seller" : "Buyer")} Absorption ({P(c.Price)})",
                        Tone = c.SellerAbsorption ? "bearish" : "bullish",
                        Price = c.Price,
                        Priority = PriorityAbsorption,
                        Description = c.SellerAbsorption
                            ? $"Heavy aggressive buying at {P(c.Price)} absorbed; price rejected {AbsorptionDepthTicks} ticks lower."
                            : $"Heavy aggressive selling at {P(c.Price)} absorbed; price rejected {AbsorptionDepthTicks} ticks higher.",
                        Time = T(c.BarStart)
                    });
                }
            }
        }

        // ============================================================
        // Delta Divergence (windowed CVD vs price; confirmed at bar close).
        // New price extreme on a weaker cumulative-delta extreme.
        // ============================================================
        var deltaDivergence = _divergence;
        if (deltaDivergence != "None")
        {
            signals.Add(new OrderFlowSignal
            {
                Type = "divergence",
                Title = $"{deltaDivergence} Divergence ({P(_divergencePrice)})",
                Tone = deltaDivergence == "Bearish" ? "bearish" : "bullish",
                Price = _divergencePrice,
                Priority = PriorityDivergence,
                Description = deltaDivergence == "Bearish"
                    ? $"New high at {P(_divergencePrice)} on weaker cumulative delta; buyers losing conviction."
                    : $"New low at {P(_divergencePrice)} on stronger cumulative delta; sellers losing conviction.",
                Time = T(_divergenceBarStart)
            });
        }

        // ============================================================
        // Unfinished auctions (unrepaired extremes where both sides traded).
        // A reference/magnet price, not a directional entry: only the entry nearest the
        // current price is surfaced, at PriorityUnfinished so it can never win the bias.
        // ============================================================
        lock (_syncLock)
        {
            if (_unfinishedAuctions.Count > 0)
            {
                var ua = _unfinishedAuctions.OrderBy(u => Math.Abs(u.Price - evalPrice)).First();
                signals.Add(new OrderFlowSignal
                {
                    Type = "unfinished_auction",
                    Title = $"{(ua.IsHigh ? "Unfinished High" : "Unfinished Low")} ({P(ua.Price)})",
                    Tone = ua.IsHigh ? "bullish" : "bearish",
                    Price = ua.Price,
                    Priority = PriorityUnfinished,
                    Description = ua.IsHigh
                        ? $"Sellers also traded the bar high at {P(ua.Price)}; the high is unfinished and may be revisited."
                        : $"Buyers also traded the bar low at {P(ua.Price)}; the low is unfinished and may be revisited.",
                    Time = T(ua.BarStart)
                });
            }
        }

        // Deduplicate and prioritize: Trapped > Absorption > Divergence > Retest >
        // Developing > Unfinished (reference). OrderBy is stable, so equal-priority
        // signals keep their emission order.
        var distinctSignals = signals
            .GroupBy(s => $"{s.Type}:{s.Tone}:{s.Title}")
            .Select(g => g.First())
            .OrderBy(s => s.Priority)
            .Take(5)
            .ToList();

        // Single source of truth: the highest-priority signal drives bias, action and level.
        // Both the bias card and the interpretation panel read these fields, so the two
        // surfaces can no longer disagree on direction or level. Unfinished auctions are
        // excluded — they are reference prices, never an actionable bias on their own.
        var top = distinctSignals.FirstOrDefault(s => s.Priority < PriorityUnfinished);

        string bias;
        string biasTone;
        string action;
        double actionPrice;

        if (top == null)
        {
            bias = "Rotational / Balance";
            biasTone = "neutral";
            action = "Stand Aside";
            actionPrice = pocPrice;
        }
        else
        {
            biasTone = top.Tone;
            actionPrice = top.Price > 0 ? top.Price : pocPrice;
            var level = actionPrice.ToString("F" + dec, CultureInfo.InvariantCulture);

            switch (top.Type)
            {
                case "trapped_traders":
                    bias = top.Tone == "bearish" ? "Trapped Buyers" : "Trapped Sellers";
                    action = (top.Tone == "bearish" ? "Fade at " : "Squeeze at ") + level;
                    break;
                case "absorption":
                    bias = top.Tone == "bearish" ? "Seller Absorption" : "Buyer Absorption";
                    action = (top.Tone == "bearish" ? "Fade at " : "Squeeze at ") + level;
                    break;
                case "divergence":
                    bias = top.Tone == "bearish" ? "Bearish Divergence" : "Bullish Divergence";
                    action = (top.Tone == "bearish" ? "Fade at " : "Squeeze at ") + level;
                    break;
                case "unfinished_auction":
                    bias = top.Tone == "bearish" ? "Unfinished Low" : "Unfinished High";
                    action = (top.Tone == "bearish" ? "Target down " : "Target up ") + level;
                    break;
                case "exhaustion":
                    bias = top.Tone == "bearish" ? "Buying Exhaustion" : "Selling Exhaustion";
                    action = (top.Tone == "bearish" ? "Fade at " : "Squeeze at ") + level;
                    break;
                default: // stacked_imbalance
                    bias = top.Tone == "bearish" ? "Bearish Initiative" : "Bullish Initiative";
                    action = (top.Tone == "bearish" ? "Sell at " : "Buy at ") + level;
                    break;
            }
        }

        var result = new OrderFlowContext
        {
            Bias = bias,
            BiasTone = biasTone,
            Action = action,
            ActionPrice = actionPrice,
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
    // ROLLING PER-LEVEL VOLUME BASELINE (mean / σ)
    // Used by absorption to flag a price level whose one-sided aggressor
    // volume is an outlier versus the recent distribution.
    // ============================================================
    private static void UpdateLevelVolumeStats()
    {
        var n = _recentLevelVolumes.Count;
        if (n == 0)
        {
            _levelVolMean = 0;
            _levelVolStd = 0;
            return;
        }

        double sum = 0;
        foreach (var v in _recentLevelVolumes)
            sum += (double)v;
        var mean = sum / n;

        double sq = 0;
        foreach (var v in _recentLevelVolumes)
        {
            var d = (double)v - mean;
            sq += d * d;
        }

        _levelVolMean = mean;
        _levelVolStd = Math.Sqrt(sq / n);
    }

    // ============================================================
    // UNFINISHED AUCTION (per-bar extreme where both sides printed)
    // A completed bar's high is finished when only buyers printed there; if sellers also
    // traded the high (bid volume above the dust filter) the auction is unfinished. Mirror
    // for the low (ask volume). The price is a reference the market tends to revisit.
    // ============================================================
    private static void DetectUnfinishedAuctions(FootprintBarData bar, double tickSize)
    {
        var levels = bar.Levels;
        if (levels == null || levels.Length == 0)
            return;

        var minSide = (bar.Volume / levels.Length) * (decimal)UnfinishedAuctionMinSideRatio;

        foreach (var lvl in levels)
        {
            if (Math.Abs(lvl.Price - bar.High) <= tickSize * 0.5 &&
                lvl.BidVolume >= minSide &&
                !_unfinishedAuctions.Any(u => u.IsHigh && Math.Abs(u.Price - lvl.Price) < tickSize * 0.5))
            {
                _unfinishedAuctions.Add(new UnfinishedAuction { IsHigh = true, Price = lvl.Price, BarStart = bar.StartTime });
            }

            if (Math.Abs(lvl.Price - bar.Low) <= tickSize * 0.5 &&
                lvl.AskVolume >= minSide &&
                !_unfinishedAuctions.Any(u => !u.IsHigh && Math.Abs(u.Price - lvl.Price) < tickSize * 0.5))
            {
                _unfinishedAuctions.Add(new UnfinishedAuction { IsHigh = false, Price = lvl.Price, BarStart = bar.StartTime });
            }
        }

        while (_unfinishedAuctions.Count > MaxUnfinishedAuctions)
            _unfinishedAuctions.RemoveAt(0);
    }

    // ============================================================
    // DELTA DIVERGENCE (windowed CVD vs price, confirmed at bar close)
    // Bearish: new price high on a lower cumulative-delta high.
    // Bullish: new price low on a higher cumulative-delta low.
    // ============================================================
    private static void ComputeDivergence(double tickSize)
    {
        _divergence = "None";
        _divergencePrice = 0;
        _divergenceBarStart = 0;

        var n = _divergenceWindow.Count;
        if (n < 3)
            return;

        var latest = _divergenceWindow[^1];

        var priorHigh = double.MinValue;
        var priorHighCvd = 0m;
        var priorLow = double.MaxValue;
        var priorLowCvd = 0m;

        for (var i = 0; i < n - 1; i++)
        {
            var p = _divergenceWindow[i];
            if (p.High >= priorHigh) { priorHigh = p.High; priorHighCvd = p.Cvd; }
            if (p.Low <= priorLow) { priorLow = p.Low; priorLowCvd = p.Cvd; }
        }

        if (latest.High > priorHigh + tickSize * 0.5 &&
            latest.Cvd < priorHighCvd &&
            latest.Delta <= 0)
        {
            _divergence = "Bearish";
            _divergencePrice = latest.High;
            _divergenceBarStart = latest.Start;
        }
        else if (latest.Low < priorLow - tickSize * 0.5 &&
                 latest.Cvd > priorLowCvd &&
                 latest.Delta >= 0)
        {
            _divergence = "Bullish";
            _divergencePrice = latest.Low;
            _divergenceBarStart = latest.Start;
        }
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

        // Minimum level volume = X% of the bar's average level volume, never below the
        // absolute MinImbalanceVol floor (quiet-market gate). Ratios alone would let a
        // near-empty bar produce "meaningful" imbalances; the 3:1 ratio does the real work.
        var avgLevelVol = bar.Volume / levels.Length;
        var minLevelVol = Math.Max(avgLevelVol * (decimal)MinImbalanceVolRatio, MinImbalanceVol);

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

    public static int Decimals(double tickSize)
    {
        var s = tickSize.ToString(CultureInfo.InvariantCulture);
        var i = s.IndexOf('.');
        return i < 0 ? 0 : s.Length - i - 1;
    }
}