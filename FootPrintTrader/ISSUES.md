# FootPrintTrader — Live-Trading Readiness Review

**Scope:** functionality of the market-data → analysis → render pipeline. Trade execution mechanics are out of scope.
**Date:** 2026-10-16
**Method:** full code trace + external verification of IB/CME behavior (sources noted per issue). Line numbers are approximate and will drift as files change.
**Verdict:** ❌ **Not ready for live trading.** Analysis reliability (FT-006/007/008) is now fixed; the remaining blocker is the data-quality question (FT-009/FT-010). (Connection, startup, contract-roll, session-reset, and warm-up concerns were reviewed and deliberately deferred — see below.)

> **Note (removed issues):** FT-001 (reconnect), FT-002 (startup error handling), FT-003 (contract roll), FT-004 (warm-up), and FT-005 (session reset) were intentionally dropped and will **not** be fixed. IDs are stable and are not renumbered, so the remaining set has gaps (FT-006…).

---

## Status legend

| Status | Meaning |
|---|---|
| 🔴 Open | Not started |
| 🟡 In progress | Being worked |
| 🟢 Fixed | Resolved & verified |
| ⚪ Won't fix | Accepted / deferred with rationale |

## Issue index

| ID | Severity | Area | Title | Status |
|---|---|---|---|---|
| [FT-006](#ft-006) | High | Analysis | Absorption & divergence computed on in-progress bar | 🟢 Fixed |
| [FT-007](#ft-007) | Medium | Analysis | Signal prioritization not implemented; truncation drops key signals | 🟢 Fixed |
| [FT-008](#ft-008) | Medium | Analysis/UI | Two conflicting recommendation surfaces | 🟢 Fixed |
| [FT-009](#ft-009) | Medium | Data | Trade ticks use `"Last"` instead of `"AllLast"` | 🔴 Open |
| [FT-010](#ft-010) | Medium | Data | IB data fitness for footprint/delta (known quality limits) | 🔴 Open |
| [FT-011](#ft-011) | Low | UI/Config | UI label says 1000-trade; actually 2000; unused config | 🔴 Open |
| [FT-012](#ft-012) | Low | Perf | Full 200-bar payload rebuilt & pushed every second | 🔴 Open |
| [FT-013](#ft-013) | Low | Concurrency | Static state read outside lock; overlapping timer callbacks | 🔴 Open |
| [FT-014](#ft-014) | Low | Logging | Hardcoded log path, truncate on start | 🔴 Open |
| [FT-015](#ft-015) | Low | Analysis | `"unfinished_auction"` type documented but never emitted | 🔴 Open |
| [FT-016](#ft-016) | Low | Frontend | Dead `interp-factors` UI + deprecated `setAnalysis` no-op | 🔴 Open |

---

## Cleanup impact (previous edit)

The removal of `ResetSession()`, `Analyze()`, `FootprintRenderLine`/`Analysis`, `FmtVol()`, `Signed()`, `MarketInterpretation.{Factors,IsConflict,IsCompressedRange,BarRange}`, and the unused `currentPrice` parameter of `Interpret()` **lost no functionality**:

- Every removed member had zero call sites; build passes with 0 warnings.
- `Factors` was the only removed field the frontend referenced (`main.js:295`), but the interpreter never populated it → the factor list was already inert.
- `Analyze()`/`Analysis` map to the already-deprecated `setAnalysis` no-op (`footprint-render.js:203`).
- Remaining cruft from the removal is tracked in [FT-016](#ft-016).

---

## High

### FT-006
**Absorption & divergence computed on the in-progress bar**
- **Area:** analysis
- **Evidence:** `bar = current ?? completed.LastOrDefault()`. Delta divergence compares a partial live-bar delta against a completed bar's delta (`FootprintAnalyzer.cs:404,414`); absorption uses partial live-bar volume/delta against a completed-bar baseline (`:355-370`). Absorption also requires live volume ≥ 100% of the prior 20-bar average (`:68`, `:368-370`).
- **Impact:** The two signals have different latency profiles, and both use the wrong *measure*. Absorption is compared as whole-bar volume vs a bar baseline (so it only fires late in the bar); divergence compares one partial bar's delta to one completed bar's delta (unstable, flappy), instead of CVD-vs-price over a window.
- **Researched approach (industry norm):**
  - **Absorption is a price-level event over a short rolling window, not a bar-close event.**
    - Bookmap: "large volume traded over short time periods"; detection = *within a configurable time period, traded volume at a price level > threshold*, buys/sells separate; auto-threshold = **mean + k·SD over a rolling interval**.
    - MZpack `mzFootprint`: "absorption is a diagonal imbalance combined with **level rejection**"; params = Absorption % (ratio), **Depth (ticks)** price must bounce, Volume filter.
    - BestOrderFlow manual: 3 conditions = ratio (≥ ~400%) + volume filter + **Depth** (price travels N ticks away).
    - The Confluence Show: absorption strength is "knowable **test by test, as each test seals**" → continuous, not at close.
    - Net: measure **per-price-level one-sided volume vs a rolling/adaptive baseline**, require price to fail to progress, confirm by a rejection of `Depth` ticks. Runs on the developing `Levels` → *earlier* than bar close.
  - **Divergence is inherently lagging and is confirmed at the bar/swing close.**
    - MZpack: "triggered on bar close"; LONG = new low + bullish candle + positive delta; gated by volume + delta thresholds.
    - trading-ml `Cvd_divergence`: CVD vs price over a **lookback window** ("price makes a high while CVD does not").
    - market-data-service: regular/hidden divergences between price and cumulative delta, **rolling window, session-aware**.
    - The Confluence Show: "a CVD divergence built from two pivots is knowable at the **confirming bar**, never at the pivot"; no reading is drawn during warm-up (empty sample).
    - Net: compare **CVD vs price extremes over a lookback** (swing pivots or N-bar window), emit at the confirming bar, and label the lag explicitly.
- **Fix direction:**
  1. **Absorption (real-time, per level):** track a rolling per-level aggressor-volume baseline (EWMA, or mean + k·SD over last N bars/seconds/trades); flag a level when one-sided volume exceeds the adaptive threshold, price fails to progress beyond the level (near bar extreme), and price then rejects by ≥ `Depth` ticks. Evaluate continuously on the developing bar — do not wait for completion.
  2. **Divergence (windowed, confirmed):** compute CVD vs price swing highs/lows over a lookback window; emit at the confirming bar; gate with min volume/delta; mark as "confirmed at bar close".
  3. Require a minimum sample (trades/time) before either signal is valid (no reading during warm-up).
- **References:** Bookmap Absorption Indicator KB; MZpack `mzFootprint` docs; BestOrderFlow Footprint Manual; The Confluence Show "How AI Analyzes Order Flow"; `trading-ml` order-flow docs (`Cvd_divergence`).
- **🟢 Implemented (`FootprintAnalyzer.cs`):**
  - Absorption is now per-price-level and runs on the developing bar: a rolling per-level volume distribution (`_recentLevelVolumes`, mean + `AbsorptionLevelSigma`·σ) flags outlier one-sided aggressor volume at the bar extreme; the candidate is confirmed and kept while price rejects by `AbsorptionDepthTicks`. No bar close required.
  - Divergence is now a windowed CVD-vs-price measure (`_divergenceWindow`, `DivergenceWindowBars`): a new price extreme on a weaker cumulative-delta extreme with opposing bar delta, recomputed only when a bar completes and labelled as a bar-close signal.
  - Both are gated by `MinLiveTradesForSignal` / history warm-up.

---

## Medium

### FT-007
**Signal prioritization not implemented; truncation drops key signals**
- **Area:** analysis
- **Evidence:** Comment claims `Trapped > Absorption > Divergence > Retest > Developing` (`FootprintAnalyzer.cs:426`), but the code only dedupes then `Take(5)` in insertion order (`:427-441`). Zone retest/support signals are added first (`:239-305`), absorption/divergence last (`:379-423`).
- **Impact:** Absorption/divergence are the first signals dropped. The interpreter reads `context.Signals` for absorption (`Interpreter.cs:53,111`), so it can miss signals the bias logic used → the two panels disagree.
- **Fix direction:** actually sort by priority (or emit with a priority field and cap by rank), and make the interpreter read the same prioritized set.
- **🟢 Implemented (`FootprintAnalyzer.cs`):** `OrderFlowSignal` now carries `Priority`; every emit site sets it. Dedup → `OrderBy(Priority).Take(5)` (stable, so equal priority keeps emission order). The bias and the interpreter both read the same prioritized `context.Signals`, so high-priority signals are no longer dropped by truncation.

### FT-008
**Two conflicting recommendation surfaces**
- **Area:** analysis/UI
- **Evidence:** Bias card uses the zone price (`Fade at {trappedBuy.LowPrice}`, `FootprintAnalyzer.cs:476/482`); interpretation card uses the POC (`SHORT (Fade @ {context.PocPrice})`, `FootprintInterpreter.cs:88/95`). Interpreter conflict check is `hasBullish && hasBearish` (`:49-56`); compressed-range check uses the live partial bar (`:70`).
- **Impact:** Same trap shown at two different levels; a routine two-sided auction triggers frequent "Conflicting Auction / STAND ASIDE"; early in each bar the interpreter says STAND ASIDE while the bias card says "Buy at …".
- **Fix direction:** single source of truth for the actionable level; make conflict detection require *opposing high-priority* signals; base range checks on completed bars (or clearly label live-bar variants).
- **🟢 Implemented:** the analyzer now derives bias/action/level from the single highest-priority signal and exposes `OrderFlowContext.ActionPrice`; the bias card (`Action`) and interpreter both use it, so the two surfaces can no longer disagree on direction or level. The interpreter's conflict test now requires opposing *structural* signals only (`Priority <= PriorityDivergence`), and its range filter uses the last **completed** bar (`FootprintInterpreter.cs`).

### FT-009
**Trade ticks use `"Last"` instead of `"AllLast"`**
- **Area:** data
- **Evidence:** `SubscribeToTradesTickByTick` requests `"Last"` (`IbMarketData.cs:365`).
- **External fact:** IB docs — `AllLast` includes additional trade types (combos, derivatives, average-price trades) not present in `Last`.
- **Impact:** Understated volume/delta vs a full Time & Sales feed; footprint may not reconcile with exchange volume.
- **Fix direction:** switch to `"AllLast"` (and reconcile against cumulative volume).

### FT-010
**IB data fitness for footprint/delta**
- **Area:** data
- **Evidence (external):**
  - DeepCharts: IB data "is aggregated… cannot be used for footprint, delta, or volume analysis".
  - Overcharts: IB is "NOT the right data feed… for Volume Ladder (footprint), Volume Delta".
  - Sierra Chart / ATAS: documented missing/duplicate IB ticks; IB streaming is ~0.2–0.3s snapshots except true tick-by-tick.
- **Impact:** Even with `reqTickByTickData` (the best IB offers), footprint/delta accuracy may not match a professional feed. This is a fitness-for-purpose decision, not a bug.
- **Fix direction:** decide explicitly; if footprint accuracy matters, source data from a dedicated feed (e.g., dxFeed/Rithmic) and keep IB for execution only.

---

## Low

### FT-011
**UI label vs actual bar size; unused config**
- **Evidence:** `index.html:29` says "1000-Trade Footprint", but the classifier uses the default `tradesPerBar = 2000` (`FootprintClassifier.cs:68`; `Program.cs:48` passes only `tickSize`). `TradingConfig.TicksPerBucket = 1000` is unused.
- **Fix:** correct the label, drive both from one config value.

### FT-012
**Full payload rebuilt & pushed every second**
- **Evidence:** `TimerCallback` calls `FootprintRenderBuilder.Build` each second (`Program.cs:188`), which re-converts all bars plus `Current` (`FootprintRenderBuilder.cs:79-107`); the frontend only patches the last bar unless the signature changes (`main.js:119-127`).
- **Fix:** send deltas (last bar / new bar) instead of the full history each tick.

### FT-013
**Static state read outside lock; overlapping timer callbacks**
- **Evidence:** `_baselineBarVolume` (non-atomic `decimal`) is read outside `_syncLock` (`FootprintAnalyzer.cs:311`) while written under it (`:181-183`). `System.Threading.Timer` can overlap callbacks if a tick exceeds 1s. `_lastContext` written unlocked (`:543`).
- **Fix:** snapshot the baseline under lock (or make it `double`/`volatile`-safe), guard against overlapping ticks, and synchronize `_lastContext`.

### FT-014
**Hardcoded log path; truncate on start**
- **Evidence:** `Logger.DATA_DIR = "/home/oce/LocalDB/logs/footprint"` (`Logger.cs:9`); log opened with `append: false` (`Logger.cs:16`).
- **Impact:** Not portable; history from prior runs is destroyed on every start. No audit trail of signals/bias transitions either (worth adding for post-session review).
- **Fix:** configurable path; append with rotation.

### FT-015
**`"unfinished_auction"` documented but never emitted**
- **Evidence:** listed in the `OrderFlowSignal.Type` comment (`FootprintAnalyzer.cs:11`); no code path emits it.
- **Fix:** implement or remove from the documented type set.

### FT-016
**Dead frontend factor UI + deprecated API**
- **Evidence:** `#interp-factors` element (`index.html:84`), rendering logic (`main.js:294-303`), CSS `.interp-factors`; `window.footprint.setAnalysis` is a documented no-op (`footprint-render.js:203`).
- **Fix:** remove, or repopulate `Factors` in the interpreter and re-add the field.

---

## Verified correct (non-issues)

- **P&L / multiplier:** `TradeMultiplier` fallback 5 matches MES (`$5`/point). ES/MES tick 0.25 = `$12.50` / `$1.25`. Point math in `SendPositionUpdate` is correct. (CME Group, AMP Futures.)
- **Dual tick-by-tick subscription is fine:** subscribing `"BidAsk"` and `"Last"` for the same contract is allowed — IB's own sample issues four `reqTickByTickData` calls on one contract. (IB TWS API sample.)
- **Cleanup edit:** build passes 0 warnings/0 errors; no callers were orphaned.

---

## Suggested remediation order

1. ~~FT-006 + FT-007 + FT-008 — make the analysis and its two panels consistent and reliable.~~ 🟢 Done.
2. FT-009 + FT-010 — settle the data-quality question (feed choice).
3. Low-severity items as cleanup.

## Open decisions

- **Feed:** is IB tick-by-tick acceptable for this footprint, or is a dedicated feed required (FT-010)?
- **Bar model:** keep trade-count bars (2000) or move to time-based/session-aligned bars?
