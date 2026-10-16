using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FootPrintNT
{
    /// <summary>
    /// Order-flow signal engine. Instance-based (one per panel tab) so that
    /// multiple instruments/windows never share state.
    /// Ported from the FootPrintTrader analyzer; logic is unchanged.
    /// </summary>
    public sealed class FootprintAnalyzer
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

        // --- Volume floors (percentage-based, relative to recent activity) ---
        // Ratios alone normalize thin data into looking meaningful, so a bar/level must
        // also clear a share of its own recent activity before any signal is emitted.
        private const double MinImbalanceVolRatio = 0.05;      // level >= 5% of the bar's avg level volume
        private const double MinBarVolumeBaselineRatio = 0.25; // bar >= 25% of the 20-bar rolling avg volume

        // --- Absorption (real-time, per price level; Bookmap / MZpack style) ---
        private const double AbsorptionLevelSigma = 3.0;  // aggressor vol >= mean + 3σ of recent max-side level volumes
        private const int AbsorptionDepthTicks = 2;       // price must reject >= 2 ticks from the level
        private const double AbsorptionSideDominance = 0.6; // aggressor side >= 60% of the level's total volume

        // --- Divergence (windowed CVD vs price; confirmed at bar close) ---
        private const int DivergenceWindowBars = 20;
        private const double DivergenceMinCvdGapRatio = 0.10; // CVD gap must be >= 10% of the prior extreme's |CVD|

        // --- Unfinished auction (per-bar extreme; both sides printed == unfinished) ---
        private const int MaxUnfinishedAuctions = 4;

        // --- Rolling baselines ---
        private const int BaselineWindowBars = 20;
        private const int LevelVolumeWindow = 2000;       // rolling per-level max-side volume samples for mean/σ

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
        private readonly object _syncLock = new object();
        private readonly List<OrderFlowZone> _activeZones = new List<OrderFlowZone>();
        private readonly HashSet<long> _processedBars = new HashSet<long>();
        private readonly Queue<decimal> _recentBarVolumes = new Queue<decimal>();
        private readonly Queue<decimal> _recentLevelVolumes = new Queue<decimal>();
        private OrderFlowContext _lastContext;
        private decimal _sessionCumulativeDelta;
        private decimal _baselineBarVolume;
        private double _levelVolMean;
        private double _levelVolStd;

        // Absorption candidates detected on a developing/recent bar; confirmed when price rejects.
        private readonly List<AbsorptionCandidate> _absorptionCandidates = new List<AbsorptionCandidate>();

        // Persistent unfinished-auction references (unrepaired bar extremes where both sides traded).
        private readonly List<UnfinishedAuction> _unfinishedAuctions = new List<UnfinishedAuction>();

        // Rolling window of completed bars (price extremes + cumulative delta at close) for divergence.
        private readonly List<DivergencePoint> _divergenceWindow = new List<DivergencePoint>();
        private string _divergence = "None";
        private double _divergencePrice;
        private long _divergenceBarStart;

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

        private readonly struct DivergencePoint
        {
            public DivergencePoint(double high, double low, decimal cvd, decimal delta, long start)
            {
                High = high; Low = low; Cvd = cvd; Delta = delta; Start = start;
            }
            public readonly double High;
            public readonly double Low;
            public readonly decimal Cvd;
            public readonly decimal Delta;
            public readonly long Start;
        }

        /// <summary>
        /// Clears all session state (call when the panel switches instruments).
        /// </summary>
        public void Reset()
        {
            lock (_syncLock)
            {
                _activeZones.Clear();
                _processedBars.Clear();
                _recentBarVolumes.Clear();
                _recentLevelVolumes.Clear();
                _absorptionCandidates.Clear();
                _unfinishedAuctions.Clear();
                _divergenceWindow.Clear();
                _lastContext = null;
                _sessionCumulativeDelta = 0m;
                _baselineBarVolume = 0m;
                _levelVolMean = 0;
                _levelVolStd = 0;
                _divergence = "None";
                _divergencePrice = 0;
                _divergenceBarStart = 0;
            }
        }

        public OrderFlowContext AnalyzeContext(FootprintSnapshot snapshot, double tickSize, double currentPrice = 0)
        {
            if (snapshot == null)
                return _lastContext ?? new OrderFlowContext();

            var completed = snapshot.Completed ?? new FootprintBarData[0];
            var current = snapshot.Current;

            // Use live bar for real-time live metrics (delta, POC, volume), fallback to last completed
            var bar = current ?? completed.LastOrDefault();
            FootprintBarData prevBar = current != null
                ? completed.LastOrDefault()
                : (completed.Length > 1 ? completed[completed.Length - 2] : null);

            if (bar == null)
                return _lastContext ?? new OrderFlowContext();

            var dec = Decimals(tickSize);
            string P(double v) { return v.ToString("F" + dec, CultureInfo.InvariantCulture); }
            string T(long unixSeconds) { return FootprintRender.FormatTime(unixSeconds); }
            var signals = new List<OrderFlowSignal>();
            var evalPrice = currentPrice > 0 ? currentPrice : bar.Close;

            // POC Calculation & Zone Placement for current bar
            var levels = bar.Levels ?? new FootprintLevel[0];
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
                var prevLevels = prevBar.Levels ?? new FootprintLevel[0];
                if (prevLevels.Length > 0)
                    prevPoc = prevLevels.OrderByDescending(l => l.Total).First().Price;
                if (pocPrice > prevPoc + tickSize * 0.5) pocMigration = "▲ Up";
                else if (pocPrice < prevPoc - tickSize * 0.5) pocMigration = "▼ Down";
                else pocMigration = "→ Flat";
            }

            // Multi-bar / Session Structure
            var liveDelta = current != null ? current.Delta : 0m;
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

                        // Maintain rolling per-level max-side volume distribution (mean/σ)
                        // for absorption — one side is compared against one side.
                        foreach (var lvl in compBar.Levels)
                            _recentLevelVolumes.Enqueue(Math.Max(lvl.BidVolume, lvl.AskVolume));
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

                        var barImbalances = compBar.Volume >= _baselineBarVolume * (decimal)MinBarVolumeBaselineRatio
                            ? DetectStackedImbalances(compBar, _baselineBarVolume)
                            : new List<ImbalanceSpan>();
                        var bRange = compBar.High - compBar.Low;

                        foreach (var imb in barImbalances)
                        {
                            bool isExhaustion;
                            if (imb.IsBuy)
                            {
                                // At the bar extreme AND the close failed to hold the stack top
                                // (industry: aggression at the extreme + close off the extreme).
                                isExhaustion = bRange > 1e-6 &&
                                               (imb.EndP >= compBar.High - tickSize * 0.5 ||
                                                (imb.EndP - compBar.Low) / bRange >= 0.75) &&
                                               compBar.Close < imb.EndP;
                            }
                            else
                            {
                                isExhaustion = bRange > 1e-6 &&
                                               (imb.StartP <= compBar.Low + tickSize * 0.5 ||
                                                (imb.StartP - compBar.Low) / bRange <= 0.25) &&
                                               compBar.Close > imb.StartP;
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
                                Title = zone.IsExhaustionAtBirth ? "Trapped Longs (" + P(zone.LowPrice) + ")" : "Trapped Buyers (" + P(zone.LowPrice) + ")",
                                Tone = "bearish",
                                Price = zone.LowPrice,
                                Priority = PriorityTrapped,
                                Description = "Price traded below the buy imbalance at " + P(zone.LowPrice) + "; buyers are stuck offside.",
                                Time = T(zone.BarStartTime)
                            });
                        }
                        else if (Math.Abs(evalPrice - zone.LowPrice) <= tickSize * 1.5 ||
                                 (evalPrice >= zone.LowPrice && evalPrice <= zone.HighPrice + tickSize * 0.5))
                        {
                            signals.Add(new OrderFlowSignal
                            {
                                Type = "stacked_imbalance",
                                Title = "Retesting Buy Zone (" + P(zone.LowPrice) + ")",
                                Tone = "bullish",
                                Price = zone.LowPrice,
                                Priority = PriorityRetest,
                                Description = "Pullback testing the buy imbalance zone " + P(zone.LowPrice) + "–" + P(zone.HighPrice) + " from above.",
                                Time = T(zone.BarStartTime)
                            });
                        }
                        else if (evalPrice > zone.HighPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
                        {
                            signals.Add(new OrderFlowSignal
                            {
                                Type = "stacked_imbalance",
                                Title = "Buyer Support (" + P(zone.LowPrice) + ")",
                                Tone = "bullish",
                                Price = zone.LowPrice,
                                Priority = PriorityRetest,
                                Description = "Fresh buy imbalance " + P(zone.LowPrice) + "–" + P(zone.HighPrice) + " holding as support.",
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
                                Title = zone.IsExhaustionAtBirth ? "Trapped Shorts (" + P(zone.HighPrice) + ")" : "Trapped Sellers (" + P(zone.HighPrice) + ")",
                                Tone = "bullish",
                                Price = zone.HighPrice,
                                Priority = PriorityTrapped,
                                Description = "Price traded above the sell imbalance at " + P(zone.HighPrice) + "; sellers are stuck offside.",
                                Time = T(zone.BarStartTime)
                            });
                        }
                        else if (Math.Abs(evalPrice - zone.HighPrice) <= tickSize * 1.5 ||
                                 (evalPrice <= zone.HighPrice && evalPrice >= zone.LowPrice - tickSize * 0.5))
                        {
                            signals.Add(new OrderFlowSignal
                            {
                                Type = "stacked_imbalance",
                                Title = "Retesting Sell Zone (" + P(zone.HighPrice) + ")",
                                Tone = "bearish",
                                Price = zone.HighPrice,
                                Priority = PriorityRetest,
                                Description = "Rally testing the sell imbalance zone " + P(zone.LowPrice) + "–" + P(zone.HighPrice) + " from below.",
                                Time = T(zone.BarStartTime)
                            });
                        }
                        else if (evalPrice < zone.LowPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
                        {
                            signals.Add(new OrderFlowSignal
                            {
                                Type = "stacked_imbalance",
                                Title = "Seller Resistance (" + P(zone.HighPrice) + ")",
                                Tone = "bearish",
                                Price = zone.HighPrice,
                                Priority = PriorityRetest,
                                Description = "Fresh sell imbalance " + P(zone.LowPrice) + "–" + P(zone.HighPrice) + " holding as resistance.",
                                Time = T(zone.BarStartTime)
                            });
                        }
                    }
                }
            }

            // Developing Live Bar Signals (Debounced: only when live bar has >= MinLiveTradesForSignal
            // and enough volume versus its own 20-bar baseline)
            if (current != null && current.TradeCount >= MinLiveTradesForSignal &&
                current.Volume >= _baselineBarVolume * (decimal)MinBarVolumeBaselineRatio)
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
                                Title = "Buying Exhaustion at High (" + P(imb.EndP) + ")",
                                Tone = "bearish",
                                Price = imb.EndP,
                                Priority = PriorityRetest,
                                Description = "Stacked buying into the bar high at " + P(imb.EndP) + " with price failing to advance; fade candidate.",
                                Time = T(current.StartTime)
                            });
                        }
                        else if (evalPrice >= imb.EndP)
                        {
                            signals.Add(new OrderFlowSignal
                            {
                                Type = "stacked_imbalance",
                                Title = "Stacked Buy Imbalance (" + P(imb.EndP) + ")",
                                Tone = "bullish",
                                Price = imb.EndP,
                                Priority = PriorityDeveloping,
                                Description = "3+ consecutive diagonal buy imbalances topping out at " + P(imb.EndP) + "; buyers lifting offers.",
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
                                Title = "Selling Exhaustion at Low (" + P(imb.StartP) + ")",
                                Tone = "bullish",
                                Price = imb.StartP,
                                Priority = PriorityRetest,
                                Description = "Stacked selling into the bar low at " + P(imb.StartP) + " with price failing to break down; squeeze candidate.",
                                Time = T(current.StartTime)
                            });
                        }
                        else if (evalPrice <= imb.StartP)
                        {
                            signals.Add(new OrderFlowSignal
                            {
                                Type = "stacked_imbalance",
                                Title = "Stacked Sell Imbalance (" + P(imb.StartP) + ")",
                                Tone = "bearish",
                                Price = imb.StartP,
                                Priority = PriorityDeveloping,
                                Description = "3+ consecutive diagonal sell imbalances bottoming at " + P(imb.StartP) + "; sellers hitting bids.",
                                Time = T(current.StartTime)
                            });
                        }
                    }
                }
            }

            // ============================================================
            // Absorption (real-time, per price level; Bookmap / MZpack style)
            // ============================================================
            var absorptionThreshold = _levelVolMean + AbsorptionLevelSigma * _levelVolStd;

            if (absorptionThreshold > 0 && levels.Length > 0 &&
                bar.TradeCount >= MinLiveTradesForSignal &&
                bar.Volume >= _baselineBarVolume * (decimal)MinBarVolumeBaselineRatio)
            {
                var depth = tickSize * AbsorptionDepthTicks;

                lock (_syncLock)
                {
                    // Register candidate levels on the developing bar.
                    foreach (var lvl in levels)
                    {
                        // Seller absorption: heavy aggressive buying at/near the high, price stalls.
                        if ((double)lvl.AskVolume >= absorptionThreshold &&
                            lvl.Total > 0 && lvl.AskVolume >= lvl.Total * (decimal)AbsorptionSideDominance &&
                            lvl.Price >= bar.High - depth + 1e-9 &&
                            !_absorptionCandidates.Any(c => c.SellerAbsorption && c.BarStart == bar.StartTime && Math.Abs(c.Price - lvl.Price) < tickSize * 0.5))
                        {
                            _absorptionCandidates.Add(new AbsorptionCandidate { SellerAbsorption = true, Price = lvl.Price, BarStart = bar.StartTime });
                        }

                        // Buyer absorption: heavy aggressive selling at/near the low, price stalls.
                        if ((double)lvl.BidVolume >= absorptionThreshold &&
                            lvl.Total > 0 && lvl.BidVolume >= lvl.Total * (decimal)AbsorptionSideDominance &&
                            lvl.Price <= bar.Low + depth + 1e-9 &&
                            !_absorptionCandidates.Any(c => !c.SellerAbsorption && c.BarStart == bar.StartTime && Math.Abs(c.Price - lvl.Price) < tickSize * 0.5))
                        {
                            _absorptionCandidates.Add(new AbsorptionCandidate { SellerAbsorption = false, Price = lvl.Price, BarStart = bar.StartTime });
                        }
                    }

                    // Keep candidates only for the current bar and the most recent completed bar.
                    var prevStart = completed.Length > 0 ? completed[completed.Length - 1].StartTime : bar.StartTime;
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
                            Title = (c.SellerAbsorption ? "Seller" : "Buyer") + " Absorption (" + P(c.Price) + ")",
                            Tone = c.SellerAbsorption ? "bearish" : "bullish",
                            Price = c.Price,
                            Priority = PriorityAbsorption,
                            Description = c.SellerAbsorption
                                ? "Heavy aggressive buying at " + P(c.Price) + " absorbed; price rejected " + AbsorptionDepthTicks + " ticks lower."
                                : "Heavy selling at " + P(c.Price) + " absorbed; price rejected " + AbsorptionDepthTicks + " ticks higher.",
                            Time = T(c.BarStart)
                        });
                    }
                }
            }

            // ============================================================
            // Delta Divergence (windowed CVD vs price; confirmed at bar close).
            // ============================================================
            var deltaDivergence = _divergence;
            if (deltaDivergence != "None")
            {
                signals.Add(new OrderFlowSignal
                {
                    Type = "divergence",
                    Title = deltaDivergence + " Divergence (" + P(_divergencePrice) + ")",
                    Tone = deltaDivergence == "Bearish" ? "bearish" : "bullish",
                    Price = _divergencePrice,
                    Priority = PriorityDivergence,
                    Description = deltaDivergence == "Bearish"
                        ? "New high at " + P(_divergencePrice) + " on weaker cumulative delta; buyers losing conviction."
                        : "New low at " + P(_divergencePrice) + " on stronger cumulative delta; sellers losing conviction.",
                    Time = T(_divergenceBarStart)
                });
            }

            // ============================================================
            // Unfinished auctions (unrepaired extremes where both sides traded).
            // ============================================================
            lock (_syncLock)
            {
                if (_unfinishedAuctions.Count > 0)
                {
                    var ua = _unfinishedAuctions.OrderBy(u => Math.Abs(u.Price - evalPrice)).First();
                    signals.Add(new OrderFlowSignal
                    {
                        Type = "unfinished_auction",
                        Title = (ua.IsHigh ? "Unfinished High" : "Unfinished Low") + " (" + P(ua.Price) + ")",
                        Tone = ua.IsHigh ? "bullish" : "bearish",
                        Price = ua.Price,
                        Priority = PriorityUnfinished,
                        Description = ua.IsHigh
                            ? "Sellers also traded the bar high at " + P(ua.Price) + "; the high is unfinished and may be revisited."
                            : "Buyers also traded the bar low at " + P(ua.Price) + "; the low is unfinished and may be revisited.",
                        Time = T(ua.BarStart)
                    });
                }
            }

            // Deduplicate and prioritize: Trapped > Absorption > Divergence > Retest >
            // Developing > Unfinished (reference). OrderBy is stable, so equal-priority
            // signals keep their emission order.
            var distinctSignals = signals
                .GroupBy(s => s.Type + ":" + s.Tone + ":" + s.Title)
                .Select(g => g.First())
                .OrderBy(s => s.Priority)
                .Take(5)
                .ToList();

            // Single source of truth: the highest-priority signal drives bias, action and level.
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
        // ============================================================
        private void UpdateLevelVolumeStats()
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
        // ============================================================
        private void DetectUnfinishedAuctions(FootprintBarData bar, double tickSize)
        {
            var levels = bar.Levels;
            if (levels == null || levels.Length == 0)
                return;

            foreach (var lvl in levels)
            {
                if (Math.Abs(lvl.Price - bar.High) <= tickSize * 0.5 &&
                    lvl.BidVolume > 0 &&
                    !_unfinishedAuctions.Any(u => u.IsHigh && Math.Abs(u.Price - lvl.Price) < tickSize * 0.5))
                {
                    _unfinishedAuctions.Add(new UnfinishedAuction { IsHigh = true, Price = lvl.Price, BarStart = bar.StartTime });
                }

                if (Math.Abs(lvl.Price - bar.Low) <= tickSize * 0.5 &&
                    lvl.AskVolume > 0 &&
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
        // ============================================================
        private void ComputeDivergence(double tickSize)
        {
            _divergence = "None";
            _divergencePrice = 0;
            _divergenceBarStart = 0;

            var n = _divergenceWindow.Count;
            if (n < 3)
                return;

            var latest = _divergenceWindow[n - 1];

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

            var bearGap = Math.Abs(priorHighCvd) * (decimal)DivergenceMinCvdGapRatio;
            var bullGap = Math.Abs(priorLowCvd) * (decimal)DivergenceMinCvdGapRatio;

            if (latest.High > priorHigh + tickSize * 0.5 &&
                latest.Cvd < priorHighCvd - bearGap &&
                latest.Delta <= 0)
            {
                _divergence = "Bearish";
                _divergencePrice = latest.High;
                _divergenceBarStart = latest.Start;
            }
            else if (latest.Low < priorLow - tickSize * 0.5 &&
                     latest.Cvd > priorLowCvd + bullGap &&
                     latest.Delta >= 0)
            {
                _divergence = "Bullish";
                _divergencePrice = latest.Low;
                _divergenceBarStart = latest.Start;
            }
        }

        // ============================================================
        // IMBALANCE DETECTION
        // Diagonal imbalance: Ask@N+1 vs Bid@N (buy) or Bid@N vs Ask@N+1 (sell).
        // Ratio gate is fixed at 3:1 (industry standard).
        // ============================================================
        internal sealed class ImbalanceSpan
        {
            public ImbalanceSpan(bool isBuy, double startP, double endP, int run)
            {
                IsBuy = isBuy; StartP = startP; EndP = endP; Run = run;
            }
            public readonly bool IsBuy;
            public readonly double StartP;
            public readonly double EndP;
            public readonly int Run;
        }

        private List<ImbalanceSpan> DetectStackedImbalances(FootprintBarData bar, decimal baselineBarVolume)
        {
            var list = new List<ImbalanceSpan>();
            var levels = bar != null ? bar.Levels : null;
            if (levels == null || levels.Length < 2) return list;

            // Defensive: footprint feeds occasionally deliver unsorted levels.
            // Sort ascending by price so the diagonal comparison is valid.
            if (!IsSortedAscending(levels))
                levels = levels.OrderBy(l => l.Price).ToArray();

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
                        list.Add(new ImbalanceSpan(true, levels[i - run].Price, levels[i - 1].Price, run));
                    run = 0;
                }
            }

            if (run >= StackedLevelsRequired)
                list.Add(new ImbalanceSpan(true, levels[levels.Length - run].Price, levels[levels.Length - 1].Price, run));

            // Detect Stacked Sell
            run = 0;
            for (var i = 0; i < levels.Length; i++)
            {
                if (sellImb[i]) run++;
                else
                {
                    if (run >= StackedLevelsRequired)
                        list.Add(new ImbalanceSpan(false, levels[i - run].Price, levels[i - 1].Price, run));
                    run = 0;
                }
            }

            if (run >= StackedLevelsRequired)
                list.Add(new ImbalanceSpan(false, levels[levels.Length - run].Price, levels[levels.Length - 1].Price, run));

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
}
