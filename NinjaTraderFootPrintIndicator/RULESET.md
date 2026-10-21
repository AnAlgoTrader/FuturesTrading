# FootprintAlertPanel — Ruleset & Signal Reference

Ruleset extracted from the indicator source:

| File | Responsibility |
|---|---|
| `FootprintAlertPanel.cs` | NinjaScript indicator: data capture, settings, panel rendering |
| `FootprintAnalyzer.cs` | Signal detection, ranking, auction bias, structural bias |
| `FootprintInterpreter.cs` | Conflict/compression gates, interpretation wording |
| `FootprintSupport.cs` | Data types, tunable settings, time formatting |

**Scope:** one instrument, one intraday session. All state (cumulative delta, rolling
baselines, active zones, level-volume stats) is session-scoped and is never rolled over
or persisted. Requires a **Volumetric (Order Flow) bar type**; without it the panel shows
"Waiting for volumetric (Order Flow) data...".

---

## 1. Data inputs

Per tick (throttled, see §8) the indicator builds a `FootprintSnapshot`:

- **Completed bars:** the last **25** closed bars.
- **Current bar:** the developing bar.
- Per bar: High / Low / Close, `Delta`, `Volume`, `TradeCount`, and per-price-level
  `BidVolume` / `AskVolume` (one row per tick size from Low to High, max 5000 levels).
- **POC** = price level with the highest total volume in the bar (falls back to Close).

### Rolling baselines (adaptive thresholds)

Everything relative is measured against a rolling baseline rather than absolute values,
so the detector tracks volatility within the session.

| Baseline | Window | Used for |
|---|---|---|
| Avg bar volume | 20 completed bars | Min-bar-volume gate, divergence gap, CVD-slope threshold |
| Avg bar range | 20 completed bars | Imbalance run length, absorption depth, compression check |
| Avg trade count | 20 completed bars | Live-bar activity gate |
| Level volume mean/std | last 2000 levels (`max(bid, ask)`) | Absorption threshold |
| Bar window (H/L/C/POC/CVD/delta) | 20 completed bars | Divergence detection + structural bias |
| Cumulative delta | session, summed on first sight of each completed bar | CVD divergence, structural bias |

---

## 2. Tunable settings

All percentages are whole numbers (`25` = 25%). Every threshold is a ratio or a
percentage of a rolling baseline — there are no absolute price/trade-count thresholds.

| Setting | Default | Range | Meaning |
|---|---|---|---|
| `ImbalanceRatio` | 3.0 | 1.5–10 | `ask[i+1] >= bid[i] * ratio` on adjacent levels (a ratio, not %) |
| `ImbalanceRunRangePct` | 25 | 1–100 | A run must span ≥ this % of the recent avg bar range to count as "stacked" |
| `MinBarVolumePct` | 25 | 1–100 | A bar must carry ≥ this % of avg bar volume to generate zones |
| `MinLevelVolumePct` | 25 | 0–50 | A level must carry ≥ this % of its bar's average level volume |
| `MinLiveTradesPct` | 35 | 0–100 | Live bar must show ≥ this % of avg trade count before live signals fire |
| `AbsorptionSigma` | 3.0 | 1–8 | Absorption = level volume > mean + sigma × std-dev |
| `AbsorptionEdgePct` | 25 | 1–100 | Absorption level must sit within this % of avg bar range from the extreme |
| `AbsorptionSidePct` | 60 | 50–100 | One aggressor side must be ≥ this % of the level's total volume |
| `MaxZoneAgeBars` | 20 | 1–25 | Bars a stacked-imbalance zone stays active |
| `UpdateIntervalMs` | 500 | 0–60000 | Min ms between recomputations (0 = every tick) |

### Internal constants (not user-tunable)

| Constant | Value | Meaning |
|---|---|---|
| `MinImbalanceRunLevels` | 2 | Floor for the imbalance run length (a tight bar can't produce a 1-level stack) |
| `AbsorptionMaxAgeBars` | 2 | Live bar + 2 completed bars for price to reject an absorption candidate |
| `TrappedZoneMaxAgeBars` | 1 | A trapped zone lingers only 1 bar, just long enough for a one-bar retest |
| `DivergenceWindowBars` | 20 | Lookback for price/CVD divergence |
| `DivergenceCvdGapRatio` | 0.15 | Required CVD shortfall/excess = 15% of one avg bar's volume |
| `MinCompressedRangeRatio` | 0.4 | Bar range < 40% of avg range ⇒ "compressed" |
| `MinTicksForTrade` | 10 | Fallback compression threshold before an avg range exists |


### Default profile: ES, 1000-tick bars, RTH only

Defaults above are tuned for ES on a **1000-tick** chart during **RTH**. Assumptions
(estimates, not measured): ~650–800 bars per RTH session (≈30 s per bar on average,
≈20 s at the open), average bar range ≈ 4–6 ticks, widening to 8–12+ on news/open.
Verify against `ATR(20)` in ticks on the actual chart and re-tune.

| Setting | Previous | Now | Reason |
|---|---|---|---|
| `MinLevelVolumePct` | 5 | 25 | 5% of the average level volume is barely a filter; it lets thin prints form imbalances (especially where the opposite side is zero) |
| `MinLiveTradesPct` | 60 | 35 | Every completed tick bar holds ~1000 trades, so this gate means "bar is X% complete"; 60% delayed live signals to the end of a ~30 s bar |
| `MaxZoneAgeBars` | 8 | 20 | 8 bars ≈ 4 min of clock time on 1000-tick bars; 20 ≈ 10 min (max allowed is 25, the snapshot size) |
| `UpdateIntervalMs` | 2000 | 500 | A 2 s throttle is a large slice of a 20–30 s bar |

**Deliberately unchanged:**

- `ImbalanceRatio` (3.0) — mid-range of the commonly cited 2:1–4:1. Raise to ~4.0 if small-sample noise
  produces too many stacks.
- `ImbalanceRunRangePct` (25) — with ~5-tick bars the 2-level floor already applies, and a 3-level stack
  would cover over half the bar. The percentage only bites above ~8 ticks of average range
  (25% → 2 levels at 8 ticks, 3 at 12, 4 at 16). Consider 30 if 2-level stacks prove noisy.
- `MinBarVolumePct`, `AbsorptionSigma`, `AbsorptionEdgePct`, `AbsorptionSidePct` — no basis yet to change; review after replay.

**Caveats at this bar speed:**

- Rolling baselines (20 bars) cover only ~5–15 minutes of clock time, and take ~5–7 minutes to warm up after
  the 9:30 ET open. The `MinTicksForTrade` fallback (10 ticks) is about double a typical bar range, so if it
  applies during warm-up nearly every bar reads as compressed (STAND ASIDE).
- The compression gate (range < 40% of average) is only ~2 ticks on a typical bar; monitor how often it fires.
- Fixed constants (`TrappedZoneMaxAgeBars` = 1, `AbsorptionMaxAgeBars` = 2, the Buyer Support / Seller
  Resistance age limit of 2 bars) are expressed in bars, so zone-retest and trapped signals live only seconds
  to ~1–2 minutes. Changing that requires editing the source.
- Changing these defaults in this document does **not** change the indicator: the same values must be
  updated in `FootprintSupport.cs` / the indicator's property defaults.

---

## 3. Detection rules

### 3.1 Stacked imbalance (zone construction)

Run on **completed bars** (only if `bar volume >= avg volume × MinBarVolumePct`) and on
the **live bar** (only if `trades >= avg trades × MinLiveTradesPct` **and**
`volume >= avg volume × MinBarVolumePct`).

1. Per price level, compute average level volume = `bar.Volume / levelCount`;
   `minLevelVol = avgLevelVol × MinLevelVolumePct`.
2. **Buy imbalance** at level `i+1` when `ask[i+1] >= minLevelVol` and
   (`bid[i] == 0` or `ask[i+1] >= bid[i] × ImbalanceRatio`).
3. **Sell imbalance** at level `i` when `bid[i] >= minLevelVol` and
   (`ask[i+1] == 0` or `bid[i] >= ask[i+1] × ImbalanceRatio`).
4. Consecutive imbalances of the same side form a **run**. Required run length:
   `max(2, ceil(avgBarRange / tickSize × ImbalanceRunRangePct%))`.
5. Each qualifying run becomes an **order-flow zone** (price span from run start to run
   end) born on that bar.

### 3.2 Exhaustion at birth (zone attribute)

A zone is flagged `IsExhaustionAtBirth` when the run sits at the extreme of its bar **but
price closed back through it**:

- **Buy run:** run top ≥ `High − 0.5 tick` **or** run top in the upper 25% of the bar
  range — **and** `Close < run top`.
- **Sell run:** run bottom ≤ `Low + 0.5 tick` **or** run bottom in the lower 25% of the
  bar range — **and** `Close > run bottom`.

This flag changes how the zone is later titled (Trapped Longs/Shorts vs. Trapped
Buyers/Sellers) and suppresses the "Support / Resistance" reading.

### 3.3 Zone lifecycle signals (evaluated every tick against current price)

**Buy zone** (zone low / zone high):

| Condition | Signal | Tone | Priority |
|---|---|---|---|
| `price < Low − 0.5 tick` | **Trapped Longs** (exhaustion-at-birth) / **Trapped Buyers** | bearish | 1 |
| `\|price − Low\| ≤ 1.5 tick` or `price ∈ [Low, High + 0.5 tick]` | **Retesting Buy Zone** | bullish | 5 |
| `price > High` and age ≤ 2 bars and not exhaustion-at-birth and not trapped | **Buyer Support** | bullish | 5 |

**Sell zone** (symmetric):

| Condition | Signal | Tone | Priority |
|---|---|---|---|
| `price > High + 0.5 tick` | **Trapped Shorts** / **Trapped Sellers** | bullish | 1 |
| `\|price − High\| ≤ 1.5 tick` or `price ∈ [Low − 0.5 tick, High]` | **Retesting Sell Zone** | bearish | 5 |
| `price < Low` and age ≤ 2 bars and not exhaustion-at-birth and not trapped | **Seller Resistance** | bearish | 5 |

- **Trapped** is an *event*, reported **once** per zone (`TrappedReported`); the zone then
  survives only 1 more bar so a one-bar retest can still be flagged.
- Zones are discarded after `MaxZoneAgeBars` (default 20).

### 3.4 Live-bar imbalance signals

Only while the live-bar activity gate (§3.1) holds:

**Buy run:**
- Run at the top of the range (top ≥ `High − 0.5 tick` or in the upper 25%) **and**
  (`price < run top` or `Close < run top`) → **Buying Exhaustion at High** *(bearish, P3)*.
- Otherwise, `price ≥ run top` → **Stacked Buy Imbalance** *(bullish, P6)*.

**Sell run** (symmetric):
- Run at the low **and** (`price > run bottom` or `Close > run bottom`) →
  **Selling Exhaustion at Low** *(bullish, P3)*.
- Otherwise, `price ≤ run bottom` → **Stacked Sell Imbalance** *(bearish, P6)*.

### 3.5 Absorption

**Detection** (gated by the live-bar activity gate and `levels.Length > 0`):

- `threshold = levelVolMean + AbsorptionSigma × levelVolStd`
- `edge depth = max(tickSize, avgBarRange × AbsorptionEdgePct%)` — relative to the
  bar's extreme, floored at one tick.
- **Seller absorption candidate:** `ask ≥ threshold` **and** `ask ≥ total × AbsorptionSidePct%`
  **and** level sits within `edge depth` of the bar **high**.
- **Buyer absorption candidate:** `bid ≥ threshold` **and** `bid ≥ total × AbsorptionSidePct%`
  **and** level sits within `edge depth` of the bar **low**.
- One candidate per side per price per bar (deduplicated).

**Confirmation** — the candidate must be *rejected* by price within the live bar + 2
completed bars:

| Candidate | Rejection test | Signal |
|---|---|---|
| Seller absorption | `price ≤ level − edge depth` | **Seller Absorption** *(bearish, P2)* |
| Buyer absorption | `price ≥ level + edge depth` | **Buyer Absorption** *(bullish, P2)* |

Pruning/confirmation run unconditionally (not behind the detection gate), so confirmed
signals don't blink off at each new bar.

### 3.6 Price / CVD divergence

Over the rolling 20-bar window (needs ≥ 3 bars and `avgBarVolume > 0`), comparing the
**latest** bar against the prior max-high / min-low:

| Condition | Signal |
|---|---|
| `latest.High > prior max High + 0.5 tick` **and** `latest.CVD < CVD at prior high − 0.15 × avgBarVolume` **and** `latest.Delta ≤ 0` | **Bearish Divergence** at latest High *(bearish, P4)* |
| `latest.Low < prior min Low − 0.5 tick` **and** `latest.CVD > CVD at prior low + 0.15 × avgBarVolume` **and** `latest.Delta ≥ 0` | **Bullish Divergence** at latest Low *(bullish, P4)* |

The CVD gap scales with average bar volume so the threshold stays meaningful early in
the session when cumulative delta is still small. Only one divergence is reported per bar.

---

## 4. Signal priority & ranking

Lower priority number = more important.

| Priority | Signal family |
|---|---|
| 1 | `trapped_traders` — Trapped Longs/Shorts, Trapped Buyers/Sellers |
| 2 | `absorption` — Seller/Buyer Absorption |
| 3 | `exhaustion` — Buying/Selling Exhaustion at High/Low |
| 4 | `divergence` — Bullish/Bearish Divergence |
| 5 | `stacked_imbalance` (retest / support / resistance) |
| 6 | `stacked_imbalance` (developing, live-bar) |

Ranking rules:

1. Deduplicate on `Type + Tone + Title`.
2. Sort by **priority ascending**, then **newest bar descending** (`BarTime` tie-break —
   this stops an old zone retest from outranking what the developing bar is doing now).
3. Keep the **top 5** for display.
4. Priorities 1–4 are **structural**; 5–6 are not.

---

## 5. Auction bias selection

The **structural bias** (§6) is the base layer. Only structural signals (P1–P4) may
override it; retests and developing imbalances stay in the message list but never set
the headline.

- **No structural signal:** headline = structural bias, action priced at the POC:
  - bullish → `Buy pullbacks to <POC>`
  - bearish → `Sell rallies to <POC>`
  - neutral → **no message** — the AUCTION BIAS section stays visible but its content is empty (see §8)
- **Structural signal present** (top-ranked, P1–P4), action priced at the signal level:

| Signal type | Headline bias | Action |
|---|---|---|
| `trapped_traders` | Trapped Buyers / Trapped Sellers | `Short/Long on retest of <level>` |
| `absorption` | Seller Absorption / Buyer Absorption | `Short/Long on retest of <level>` |
| `divergence` | Bearish/Bullish Divergence | `Short/Long on retest of <level>` |
| `exhaustion` | Buying/Selling Exhaustion | `Short/Long on retest of <level>` |
| default (`stacked_imbalance`) | Bearish/Bullish Initiative | `Sell rallies to` / `Buy pullbacks to <level>` |

---

## 6. Structural bias (score −4 … +4)

Four independent readings over the completed-bar window, each worth ±1:

| # | Reading | Bullish (+1) | Bearish (−1) |
|---|---|---|---|
| 1 | **CVD slope** across window | change > `+0.15 × avgBarVolume` | change < `−0.15 × avgBarVolume` |
| 2 | **Delta persistence**, last 10 completed bars | ≥ 7 positive-delta bars | ≤ 3 positive-delta bars |
| 3 | **POC migration**, last 3 completed bars | POC moved up > 0.5 tick | POC moved down > 0.5 tick |
| 4 | **Close location** in last completed bar | `(Close − Low)/Range ≥ 0.70` | `(Close − Low)/Range ≤ 0.30` |

Scoring:

| Score | Reported as |
|---|---|
| ≥ +3 | **Strong Bullish** |
| +2 | **Bullish** |
| −1 … +1 | **Rotational / Balance** |
| −2 | **Bearish** |
| ≤ −3 | **Strong Bearish** |

With < 2 bars in the window or no baseline yet: **Rotational / Balance (0)**.

---

## 7. Interpretation gates (evaluated in order)

1. **Conflict gate** — at least one bullish *and* one bearish structural signal (P1–P4):
   → `Conflicting Auction (Bracket)` · tone **warning** · **STAND ASIDE**
   *(detail mentions compression if the bar is also compressed).*
2. **Compression gate** — last completed bar range `< 40%` of average bar range
   (fallback before an average exists: `< 10 ticks`) **and** bias ≠ Rotational / Balance:
   → `<Bias> (Range Compressed)` · tone **warning** · **STAND ASIDE**
   *(detail: actual range vs average range, "Poor risk/reward").*
3. **Neutral gate** — bias is Rotational / Balance, tone neutral, or action empty:
   → no interpretation message is shown, **and no AUCTION BIAS message either** (§8). There
   is no `Stand Aside` text for a neutral state; the sections stay visible but empty.
4. Otherwise → title = bias, tone = bias tone, action = analyzer's action wording,
   detail = type-specific description:

| Signal | Bearish detail | Bullish detail |
|---|---|---|
| trapped | "Longs trapped below L. Expect a failed retest of L." | "Shorts trapped above L. Expect a failed retest of L." |
| absorption | "Heavy buying absorbed at L; seller defending the high." | "Heavy selling absorbed at L; buyer defending the low." |
| divergence | "New high on weaker cumulative delta; buyers exhausted at L." | "New low on stronger cumulative delta; sellers exhausted at L." |
| exhaustion | "Buying exhaustion at L." | "Selling exhaustion at L." |
| default | "Aggressive sellers in control. Sell rallies to L." | "Aggressive buyers in control. Buy pullbacks to L." |

---

## 8. Execution & display rules

- `Calculate.OnEachTick`; requires `CurrentBar ≥ 1` and a `VolumetricBarsType`.
- Recomputation throttled to `UpdateIntervalMs` (default 500 ms), except on the first
  tick of each bar (always recalculated).
- Completed-bar snapshot cached per `CurrentBar`.
- Panel sections are **always drawn** (headers stay visible); only the messages inside them
  are conditional:
  1. **Status line** — last update time + bar number (or error text).
  2. **INTERPRETATION** — title, action, detail. Content only if a non-empty interpretation
     exists; otherwise the section is empty.
  3. **AUCTION BIAS** — bias + action, and `Structure: <bias> (±n/4)`. Content is **omitted in a
     neutral state** (structural bias Rotational / Balance and no structural signal P1–P4);
     otherwise it is shown as before.
  4. **ORDER FLOW MESSAGES** — up to **5** ranked signals with their bar time. Empty when there
     are no ranked signals.
- **Empty messages:** in a neutral state no "Stand Aside", "Rotational / Balance" or
  "Structure: … (0/4)" text is displayed, and no "nothing happening" filler is added in its
  place. The section headers remain, with nothing under them. If ranked signals exist (e.g. a
  developing imbalance, P5/P6) they are still listed under ORDER FLOW MESSAGES even when the
  bias is neutral.
- Tone colours: bullish green, bearish red, warning amber, neutral grey.
- There is no alert/audio/`Alert()` invocation — signals are displayed on the chart
  panel only.

---

## 9. Signal catalogue (quick reference)

| Signal title | Type | Tone | Priority | Source rule |
|---|---|---|---|---|
| Trapped Longs (L) | `trapped_traders` | bearish | 1 | Exhaustion buy zone broken below (§3.3) |
| Trapped Sellers (L) | `trapped_traders` | bearish | 1 | Non-exhaustion buy zone broken below (§3.3) |
| Trapped Shorts (H) | `trapped_traders` | bullish | 1 | Exhaustion sell zone broken above (§3.3) |
| Trapped Buyers (H) | `trapped_traders` | bullish | 1 | Non-exhaustion sell zone broken above (§3.3) |
| Seller Absorption (P) | `absorption` | bearish | 2 | Ask-side spike at high, rejected (§3.5) |
| Buyer Absorption (P) | `absorption` | bullish | 2 | Bid-side spike at low, rejected (§3.5) |
| Buying Exhaustion at High (P) | `exhaustion` | bearish | 3 | Live-bar buy stack at high, price falls back (§3.4) |
| Selling Exhaustion at Low (P) | `exhaustion` | bullish | 3 | Live-bar sell stack at low, price recovers (§3.4) |
| Bearish Divergence (P) | `divergence` | bearish | 4 | New high on weaker CVD (§3.6) |
| Bullish Divergence (P) | `divergence` | bullish | 4 | New low on stronger CVD (§3.6) |
| Retesting Buy Zone (L) | `stacked_imbalance` | bullish | 5 | Price back inside/at buy zone (§3.3) |
| Retesting Sell Zone (H) | `stacked_imbalance` | bearish | 5 | Price back inside/at sell zone (§3.3) |
| Buyer Support (L) | `stacked_imbalance` | bullish | 5 | Price above fresh non-exhaustion buy zone (§3.3) |
| Seller Resistance (H) | `stacked_imbalance` | bearish | 5 | Price below fresh non-exhaustion sell zone (§3.3) |
| Stacked Buy Imbalance (P) | `stacked_imbalance` | bullish | 6 | Live-bar buy stack, price above it (§3.4) |
| Stacked Sell Imbalance (P) | `stacked_imbalance` | bearish | 6 | Live-bar sell stack, price below it (§3.4) |

*(L = zone/run low price, H = zone/run high price, P = signal price.)*

---

## 10. Known limitations / deliberate omissions

- No state reset on reconfigure (bar-period change / template reload keeps analyzer
  state) — intentional, see comment block at the top of `FootprintAlertPanel.cs`.
- No multi-session handling: cumulative delta, baselines and level stats never roll over.
- The panel is display-only: no order execution, no sound/email alerts.
- Zone evaluation uses the current price vs. static zone boundaries; a zone never
  re-arms once `TrappedReported` is set.
