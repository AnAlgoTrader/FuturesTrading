# IndicatorBuild — Smart Money Concepts (SMC) Indicator Plan

Status: **Planning** — follow this document milestone by milestone. Update it as decisions change.

---

## 1. Goal

Build a more reliable SMC-style indicator **from scratch** (Trader's `ReversalDetector` is not used — it is a reference only).

- Input: `data/historicalData.csv` (OHLCV).
- Output: a generated HTML chart (lightweight-charts) that shows:
  - **Gray candles by default** — color is the exception, not the rule.
  - Concept colors, markers, levels and zones **only when the engine confirms a concept**.
  - A header/legend explaining what is shown.
- Design must support **two passes**: historical backtest now; streaming/event-driven later (so the same logic can power live trading in Trader).

### Principles
1. **No reinventing the wheel** — implement the well-documented canonical rules (Wyckoff, ICT BOS/CHoCH, 3-candle FVG, TTM Squeeze, RSI divergence) and cite sources (Section 10).
2. **No lookahead bias** — every signal uses only confirmed data. Swings/pivots require a confirmation bar; signals fire on **closed candles** only ("non-repainting").
3. **Gray is neutral** — anything colored must be justified by a detected concept. Overlapping concepts on one candle are resolved by a defined priority (Section 8).
4. **Separation of concerns** — detection (C#) produces a neutral `Analysis` model; rendering converts it into visuals. Backend never mixes chart-library knowledge with detection logic.

---

## 2. Domain: the four concept families

### 2.1 Market Phases — Wyckoff Cycle (Accumulation / Markup / Distribution / Markdown)

A regime engine that tracks *where price is in the institutional cycle*.

| Concept | Canonical definition (used by existing implementations) |
|---|---|
| Accumulation | Sideways range after a **prior downtrend**; institutions absorb supply. Events: **SC** (selling climax, wide spread + huge volume), **AR** (automatic rally), **ST** (secondary test on lower volume), **Spring** (false break below range low, closes back inside), **SOS** (sign of strength: high-volume close above range resistance), **LPS** (last point of support pullback), then **Phase E** = breakout → Markup. |
| Markup | Series of HH/HL; volume expands on rallies, contracts on pullbacks. |
| Distribution | Mirror of accumulation, after a **prior uptrend**. Events: **BC** (buying climax), **AR** (automatic reaction), **ST**, **UT/UTAD** (upthrust: false break above range high, closes back inside), **SOW** (sign of weakness), **LPSY** (last point of supply), then **Markdown**. |
| Markdown | Series of LH/LL. |

Rules borrowed from the automated Wyckoff EAs (MQL5 Part 1/2, Section 10):
- **Prior trend context decides the range type**: `downtrend before range ⇒ accumulation`, `uptrend before ⇒ distribution` (measured by comparing early-half high vs later-half low).
- Detection is a **state machine**: `IDLE → RANGE_FORMING → SPRING/UT_OR_SOW/UTAD → BOS_CONFIRMED → PHASE_END`, not "label everything".

### 2.2 Structure Events — Swing, BOS, CHoCH, Liquidity Sweep, FVG

The geometry of the chart. Everything else hangs off these primitives.

| Concept | Canonical rule |
|---|---|
| Swing High / Low | Pivot with `left=N` and `right=N` bars lower/higher on each side. Only confirmed after the right side exists. |
| HH / HL / LH / LL | Sequencing of confirmed swings into higher-high/higher-low/lower-high/lower-low declarations. |
| **BOS** (Break of Structure) | Close beyond a prior swing **in the direction of the current trend state** → continuation. |
| **CHoCH** (Change of Character) | Close beyond a prior swing **against the current trend state** → potential reversal. (CHoCH+ optional: requires an intervening HL/LH confirm before labeling.) |
| Break confirmation mode | Wick-only / body-only / close (choose; default **close** to stay non-repainting. Optionally 2-close for stronger.) |
| **Liquidity Sweep** | Wick pierces a confirmed swing high/low by `minPenetration` then **closes back inside** that level. Bullish sweep = low takes out prior low, close `> prior low`; bearish mirror. Filters: min wick % of candle range, optional volume spike, optional trend-EMA alignment. Lifecycle: **Detected → Confirmed → Faded** (confirmed = close beyond the sweep candle's extreme in expected direction; faded = close opposite). |
| **FVG** (Fair Value Gap) | 3-candle imbalance: **Bullish** when `Low(candle1) > High(candle3)`; zone = [High(candle3) .. Low(candle1)]. **Bearish** when `High(candle1) < Low(candle3)`; zone = [High(candle1) .. Low(candle3)]. Zone tracked until **mitigated** (price trades back into it; optionally partial-fill % + midline). Detection mode: bodies-only / wicks-only / full candle. Optional min-width filter (points / % / ATR). |

### 2.3 Price Behavior — Consolidation, Breakout, Pullback / Retracement

| Concept | Canonical rule |
|---|---|
| Consolidation | Range whose high–low stays inside a multiple of ATR over a minimum bar count (e.g. `minBars=10`, height `< ATR × 2`). Boundaries = max high / min low of the window. |
| Breakout / Breakdown | Confirmed on a **close** beyond the consolidation boundary (intrabar spikes don't count). Afterwards track outcome: **Held** vs **Failed** (close back inside within N bars). |
| Pullback / Retracement | A counter-move within the impulse leg. Measured as depth vs the impulse leg and ATR-normalized. "Healthy" retrace = 50–61.8% (Fibonacci) and/or return to FVG / order-block / previous swing level. Variant: pullback = retrace that then resumes trend; **retracement** = neutral depth measurement. |

### 2.4 Momentum — Exhaustion, Divergence, Compression, Expansion

| Concept | Canonical rule |
|---|---|
| Exhaustion (bar-level) | Big rejection wick + volume spike + extreme RSI: e.g. lower wick `> 2.5× body`, volume `> 1.8× avg`, RSI `< 35` (sell exhaustion); mirror for buy exhaustion (RSI `> 65`). |
| Divergence (pivot-level) | **Regular** bullish: price lower-low while RSI higher-low; **Regular** bearish: price higher-high while RSI lower-high. **Hidden** (continuation) variants are the inverse. Uses RSI pivots matched against price pivots; requires `minSwingPct` so tiny RSI moves don't fire. |
| Compression | Volatility squeeze. Canonical **TTM Squeeze**: Bollinger-Bands (20,2) fully inside Keltner Channels (20,1.5 ATR) → "squeeze on". Tiered: `stdev < ATR×0.5` (tight), `0.75`, `1.0`. Optional RSI-range compression (percentile of RSI high-low). |
| Expansion | Volatility release: ATR ratio vs its own history — `ATR(now) > 1.3 × ATR(50 avg)` = high volatility / expansion; squeeze-release (BB popped out of KC) marks directional `release`. |

---

## 3. Foundational building blocks (library layer)

Computed once, shared by every engine:

- `AtrCalculator` — Wilder's ATR(n).
- `RsiCalculator` — Wilder's RSI(n).
- `EmaCalculator` / rolling mean + stdev (Bollinger/Keltner math).
- `SwingDetector` — pivot highs/lows with `(left, right)` lookback → `SwingPoint` list.
- `AdLine` (Chaikin A/D) — optional, used by phase confidence.
- Volume metrics — rolling average volume (for volume-spike filters).

These are pure, stateless-per-bar calculators: `double Next(Candle)` style, so they can run both in a historical loop and per incoming tick later.

---

## 4. System architecture

```
historicalData.csv
      │  HistoricalDataSource
      ▼
  List<Candle>  ............ (already built)
      │  CandleAggregator / feed loop (one candle at a time, no future info)
      ▼
  IndicatorPipeline
   ├─ SharedCalculators   (ATR, RSI, EMA, Volume)
   ├─ SwingDetector       → SwingPoint[]
   ├─ MarketStructure     → BOS/CHoCH/HH-HL-LH-LL + running trend state
   ├─ LiquiditySweep      → SweepEvent[]
   ├─ FvgDetector         → FvgZone[]
   ├─ WyckoffEngine       → phase per range + phase events
   ├─ ConsolidationEngine → RangeZone[] + BreakoutEvent[] + PullbackEvent[]
   └─ MomentumEngine      → Exhaustion / Divergence / Compression / Expansion
      │
      ▼
  List<CandleAnalysis>          (neutral model: events, zones, phase per candle)
      │  ColorResolver (priority rules) 
      ▼
  ChartModel  (coloredCandles, markers, priceLines, zones, vwap)
      │  ChartHtmlGenerator
      ▼
  output/index.html
```

Rules of the pipeline:
1. **Stream, don't scan** — engines are state machines fed candle-by-candle. This makes the historical pass exactly equivalent to the future live pass.
2. **Events carry a timestamp + confirmation bar index.** The render pass can choose to show them at the event bar (for backtest truth) while a future live mode only shows them after the confirmation bar closed.
3. `CandleAnalysis` is **immutable per bar** and contains: candles OHLCV ref, `MarketPhase`, list of `ConceptEvent`s, list of active `Zone`s, computed indicators (rsi, atr) for debugging.

---

## 5. Class design

| Class | Responsibility |
|---|---|
| `Model/Candle` *(exists)* | OHLCV bar. |
| `Model/SwingPoint` (new) | Confirmed pivot: time, price, `IsHigh`, lookback span. |
| `Model/ConceptEvent` (new) | One detected concept on a bar: `{ Time, Type/Enum Concept, Direction, BarIndex }`. |
| `Model/Zone` (new) | Time–price rectangle: `{ StartTime, EndTime, Top, Bottom, Kind, State(mutable: mitigated/hit/active) }`. |
| `Model/MarketPhase` (new) | `{ Phase: Accumulation/Markup/Distribution/Markdown/Idle, RangeTop, RangeBottom, Bias }`. |
| `Model/CandleAnalysis` (new) | Per-bar aggregation: events on this bar, active zones, phase, rsi/atr snapshot. |
| `Model/VwapCalculator` *(exists)* | Session VWAP. |
| `Indicators/SharedCalculators.cs` (new) | Holds ATR, RSI, EMA, volume runners. |
| `Indicators/SwingDetector.cs` (new) | Pivot detection → `SwingPoint[]`. |
| `Indicators/MarketStructure.cs` (new) | Trend-state machine; emits **BOS / CHoCH**, labels HH/HL/LH/LL. |
| `Indicators/LiquiditySweepDetector.cs` (new) | Sweep lifecycle (Detected→Confirmed→Faded), wick/penetration/volume filters. |
| `Indicators/FvgDetector.cs` (new) | 3-candle FVG detection + mitigation state; zone output. |
| `Indicators/WyckoffEngine.cs` (new) | Prior-trend context, range detection, event labeling (SC/AR/ST/Spring/SOS/LPS, BC/UT/SOW/LPSY), phase output. |
| `Indicators/ConsolidationEngine.cs` (new) | ATR-tight ranges → Zone; breakout/breakdown events; held/failed tracking. |
| `Indicators/PullbackDetector.cs` (new) | Impulse-leg + retrace depth (ATR/Fib normalized); pullback events. |
| `Indicators/MomentumEngine.cs` (new) | RSI exhaustion, RSI/price divergence, squeeze (BB/KC), expansion (ATR ratio). |
| `IndicatorPipeline.cs` (new) | Constructs calculators + engines, feeds `Candle`s, returns `List<CandleAnalysis>`. |
| `Rendering/ColorResolver.cs` (new) | Priority rule + palette → final color per candle. |
| `Rendering/ChartModel.cs` (new) | Neutral render payload (candles+color, markers, price lines, zones). |
| `ChartHtmlGenerator.cs` *(extend)* | Renders `ChartModel` → HTML (candles, marker shapes, price lines, zone overlay). |
| `Program.cs` *(extend)* | Load CSV → build pipeline → build ChartModel → write HTML. |
| `IndicatorConfig.cs` (new) | All tunable constants in one place (Section 6). |

### Interface sketch
```csharp
public interface IIndicatorEngine
{
    string Name { get; }
    void ProcessBar(Candle c, SharedCalculators calc);   // advance state, emit events
    // events subscribe to CandleAnalysis sink (per-bar) or engine exposes collected results
}
```
Engines expose their results (`IReadOnlyList<ConceptEvent>`, `IReadOnlyList<Zone>`) after the pass; `CandleAnalysis` is assembled afterwards by replaying the feed (events + active zones) so rendering stays simple.

---

## 6. Configuration (single source of truth)

Add `IndicatorConfig.cs`. Initial defaults (tunable from scan results — see Milestone 6).

| Group | Parameter | Default |
|---|---|---|
| Swings | pivot lookback (left/right) | 5 |
| Break confirm | mode (close/wick/body) | close |
| Sweep | min penetration (% of level or ATR) | 0.25 × swing-to-swing distance |
| Sweep | min wick % of candle range | 0.5 (tuned) |
| FVG | min width | ATR × **3.0** (tuned; 1.0–1.5 flooded the chart) |
| FVG | detection mode | **full candle** (bodies-only is *less* selective on 5m data) |
| FVG | mitigation | candle reaches **midline** (fill fraction 0.5; edge-touch fills 99% within 1 bar — too weak) |
| FVG | detection mode | full candle |
| Consolidation | min bars | 8 (tuned; 130 ranges shown) |
| Consolidation | max range height | ATR × 1.5 (tuned) |
| Breakout | confirm bars outside range to call "Held" | 3 |
| Pullback | healthy retrace band (Fib) | 0.236–0.786 (tuned); resumption = close reclaims dip extreme |
| Wyckoff | ATR/volume event thresholds | SC volume ≥ 3× avg etc. |
| Wyckoff | range min swings | 5 (tuned) |
| Wyckoff | range min span (bars) | 40 (tuned) |
| Wyckoff | range height window | ATR × 1.0 – 4.0 (tuned; poke tolerance 2× max) |
| Wyckoff | prior-trend drift for typing | ATR × 0.5 (tuned; requires a full 6 prior swings) |
| Wyckoff | SOS/SOW volume factor | 1.2× avg |
| Wyckoff | SOS/SOW displacement | ≥ ATR × 1.0 |
| Momentum | ATR period | 14 |
| Momentum | RSI period / oversold / overbought | 14 / 35 / 65 |
| Momentum | squeeze (BB,KC) | 20/2, 20/1.5 |
| Momentum | expansion ATR ratio | ≥ 1.3 |
| Divergence | min price swing % | 0.3 |
| Volume | avg period | 50 |

All comparisons that decide *reliability* should be **ATR- or volume-normalized** — never hard price/percent thresholds — so the indicator behaves consistently across price regimes.

---

## 7. Visualization design

- **Candles**: gray `#808080` by default. Concept colors applied per Section 8 priority.
- **Markers** (lightweight-charts `createSeriesMarkers` shapes): up/down triangles for events, diamonds for exhaustion, circles for divergences, labeled per concept.
- **Price lines** (`createPriceLine`): swing levels, range boundaries, liquidity levels (state: active → taken).
- **Zones** (FVG boxes, consolidation ranges, Wyckoff ranges, order blocks): rendered as translucent rectangles. Lightweight-charts has no native box → implement a **coordinate-mapped overlay**: a positioned canvas/absolute div synced to the chart via `priceToCoordinate` / `timeScale().timeToCoordinate`, repainted on visible-range change and resize. *(Milestone 3+.)*
- **Header/legend**: date range, candle count, active phase, concept counts detected.

### Proposed palette (tune in Milestone 7)

| Concept | Color |
|---|---|
| Default / neutral | `#808080` gray |
| Bullish event (SOS, bullish CHoCH, bullish sweep, bullish breakout, bullish FVG, buy expansion) | `#26A69A` green |
| Bearish event (mirror) | `#EF5350` red |
| Exhaustion / divergence | `#FFB74D` orange |
| Compression / consolidation | `#7E57C2` purple (zones) |
| Accumulation zone | `#4DB6AC` teal shaded box |
| Distribution zone | `#F06292` pink shaded box |
| VWAP *(already shown)* | `#FF9900` |

---

## 8. Reliability & filtering philosophy

Coloring is sparse and disciplined:

1. **Every concept has quality gates** before it may color a candle. Examples:
   - Sweep needs min wick + close-back-inside; optionally volume confirmation.
   - FVG needs min width; ignored if the impulse that created it is ATR-insignificant.
   - Breakout needs a confirmed **close**, not an intrabar wick.
   - Divergence needs min swing sizes on both price and oscillator.
2. **Confluence scoring (later)**: like the "multi-factor sweep confluence score" pattern used by institutional sweep tools — each event scores against filters (trend alignment, level significance, volume, ATR displacement); low scores stay gray. This is the main lever for "more reliable".
3. **Priority when multiple concepts hit one candle** (initial order; back to backtest results before finalizing):
   Sweep / CHoCH > Breakout > Exhaustion/Divergence > Phase event (SOS/SOW) > pullback > zone state.

---

## 9. Roadmap

| Milestone | Deliverable | Acceptance |
|---|---|---|
| **M0 — Done** | CSV parsing + candle chart + VWAP | gray candles, orange VWAP, 20,635 bars |
| **M1 — Done** | `IndicatorConfig`, `SharedCalculators` (EMA/SMA/ATR/RSI), `SwingDetector`, `CandleAnalysis`/`ConceptEvent`, `ColorResolver` (gray), `IndicatorPipeline`, xunit tests | 9 tests pass (swings + calculators); pipeline reports 1032 swing highs / 1042 swing lows; chart unchanged (all gray) |
| **M2 — Done** | `MarketStructure` (HH/HL/LH/LL, BOS, CHoCH), `LiquiditySweepDetector`, `FvgDetector` (3-candle + ATR width filter + mitigation), markers + FVG zone overlay | 22 tests pass (9 M1 + 13 M2); chart shows green/red CHoCH+sweep candles, BOS/CHoCH/Sweep/FVG markers, FVG zone boxes; no repaint (close-only). **Tuned** CSV pass: 342 BOS / 332 CHoCH / 154 sweeps (64% confirm) / 453 FVG zones |
| **M3 — Done** | `ConsolidationEngine`, `PullbackDetector` (impulse leg + Fib-band retrace, close-reclaim resumption), breakout markers + pullback markers + consolidation purple zone boxes | 30 tests pass (9 M1 + 13 M2 + 8 M3); chart shows green/red "BO" breakout candles & arrow markers + purple "PB" pullback circles + purple consolidation zone boxes. **Tuned**: `conBars=8, conAtr=1.5, hold=3` → 130 consolidation ranges (held 55%); `pb[0.236–0.786]`, close-reclaim resumption → 45 pullbacks from 390 retrace measurements |
| **M4 — Done** | `WyckoffEngine` with prior-trend context and event recipes | 6 Wyckoff tests (recipe order, Spring, mirror distribution, UTAD, non-qualifying discard, flat/untyped). **Tuned** `wycSwings=5, wycBars=40, wycMaxAtr=4.0, wycPrior=0.5` → 24 ranges (8 accum / 16 dist), all finishing via orderly Phase E; events released only after the range completes end-to-end (labels place at their original swing times, no spurious markers) |
| **M5 — Momentum** | `MomentumEngine` (exhaustion, divergence, compression, expansion) | momentum events coloring with min-swing filters |
| **M6 — Backtest-driven tuning** | A stats dashboard (counts per concept, win context heuristics) | sweep the defaults in Section 6 against the CSV; tune thresholds; update config |
| **M7 — Confluence scoring** | `SignalScorer` wrapping engines; low-scoring events stay gray | measurable precision improvement on historical pass |
| **M8 — Live streaming** | EVENT-based param (`prevents lookahead`), per-bar feed identical to historical loop | same code path can consume a tick feed; drop-in reuse in Trader if wanted |

---

## 10. References & inspirations (canonical implementations to model after)

- Wyckoff automation — MQL5 article series: *Automating Classic Market Methods (Part 1: Accumulation & Distribution; Part 2: Cause & Effect)* — four-phase cycle, prior-trend bias, state machines for Spring/SOS/UT/SOW. http://mql5.com/en/articles/22628
- Market structure BOS/CHoCH — pivot-based (`left N / right N`), HH/HL/LH/LL, trend-state machine, BOS vs CHoCH by direction, close/wick/2-close confirmation. https://github.com/Johnfire45/market-structure · https://github.com/sadratech/Market-Structure-BOS-CHoCH
- FVG — strict 3-candle imbalance (`low[i] > high[i-2]` bullish), mitigation on return to zone, partial fill + midline, bodies/wicks/full modes, min-width filter. https://github.com/sonnyparlin/fvg_pinescript · https://github.com/ketaeishi/pinescript (LuxAlgo) · MQL5 *Modular FVG Detection Engine* article
- Liquidity Sweeps — swing pierce + close-back-inside, min wick %, lifecycle Detected→Confirmed→Faded, volume/trend filters, 5-factor confluence. https://github.com/mitchell-917/tradingview-pinescript-lab · https://www.tradingview.com/script/YPuN3zog (Edo Liquidity Sweeps) · Institutional Liquidity Sweep (PickMyTrade)
- Consolidation / Breakout — ATR-tight range window, close-only breakout, Held/Failed, retest tracking. https://fabtrader.in/blog/price-consolidation-boxes-using-python · https://github.com/saintmexas/trading-scripts
- Momentum — TTM Squeeze (BB inside KC), ATR expansion ratio, RSI exhaustion (wick/body × volume × RSI), regular/hidden RSI divergence with min-swing filter. https://github.com/bukosabino/ta or QuantConnect SqueezeMomentum (LEAN) · TradingView squeeze/divergence scripts referenced above

---

## 11. Open decisions (record here as we go)

- [ ] Sweep confirmation: require price to **hold outside** or accept immediate reclaim? (start: immediate close-back-inside)
- [ ] CHoCH+ requirement (intervening HL/LH) — yes by default?
- [x] FVG detection mode default: **full-candle** (wick piercing counts); mitigation on candle touching zone boundary (bar after creation only).
- [x] Break confirmation mode: **close** (non-repainting). CHoCH flips trend state immediately on close-break.
- [x] Marker/zone rendering is capped for UX (last 3 000 markers, last 2 000 zones) — presentation only; full counts are reported on the console. Consider a zoom-aware lazy renderer in an interactive milestone.
- [ ] Should a Wyckoff phase tint candles inside the range, or only mark events? (start: events + range box only)
- [x] **M2 tuning log (done):** built `Analytics/BacktestStats` metrics (structure flip-leg buckets, sweep confirm/fade, FVG fill rate + median bars-to-fill) and CLI overrides `--pivot --sweep --fvg --fvgbodies --fvgfill` in Program.
  - `pivot=7` — whipsaw flips (<20-bar legs) drop 32%→21%, median leg 30→46 bars, still 332 CHoCH.
  - `sweepWick=0.5` — 154 sweeps (0.75% of bars), confirm/fade 64%/36%.
  - `fvg=ATR×3.0, fill=0.5, full-candle` — 453 zones (2.2% of bars), median 17 bars to fill.
  - Bodies-only gaps were *less* selective (5 184 vs 4 733 zones) — rejected.
  - Open: FVG "mitigated" is near-100% on a 20k-bar history because everything gets revisited eventually; judge by **count + median fill time**, not mitigation%.
- [x] **M3 tuning log (done):** extended metrics + CLI (`--conbars --conatr --hold --pbleg --pbmin --pbmax`).
  - `conBars=8, conAtr=1.5` — 130 ranges (up=66 down=64), held 55% / failed 45% at 3-closes. `10/1.5` was only 40 ranges (too sparse); `8/1.2` 48 ranges but 52% held. `pbLeg` (impulse ≥ 1×ATR) is a **no-op** on this data — every swing leg exceeds it.
  - Original pullback design required resumption "beyond prior leg top" — only 8/390 counter-moves qualified. Rewrote resumption as **close-reclaims-the-dip-bar's-extreme** (structural reclaim): 45 pullbacks with a Fib band of 23.6–78.6% (390 neutral `Retracement` measurements emitted too). Band 38.2–61.8% gave only 14 (too selective for a chart).
- [x] **M4 tuning log (done):** extended metrics + CLI (`--wycswings --wycbars --wycmaxatr --wycprior`).
  - Wyckoff **events are confirmed-only**: recipe labels (SC/AR/ST/Spring/SOS/LPS/…) are queued during a range and flushed at the moment the range closes as a *qualified* zone, so unqualified bands (which start ~4× more often than they finish) never leak stray markers. Markers still render at their historical swing times; only bar-time events (SOS/SOW/Phase E) color candles, so on-chart color count equals released events.
  - Bands typed by prior-trend context require a **full 6 prior swings** (`take 6, first→last drift ≥ 0.5×ATR`) — the 4-swing version typed mid-downtrend at t5 instead of the true range start t7 and produced wrong SC/extreme and no Spring/ST.
  - Two consecutive decisive poke-outs (≥2 overflows) **discard** the band (range violated before an orderly Phase E) instead of closing a zone; only orderly Phase-E ends render boxes. Raising `--wycmaxatr` 4→5 lifted 24→39 ranges (rejected — kept 4.0 for precision).
  - CSV result: **24 ranges (accum=8 dist=16), 100% Phase-E completion**; recipe events released: AR=24 ST=16 SOS=4 SOW=14 LPS=3 LPSY=1 Spring=2 UTAD=6 SC=1 BC=8 (SC counts low because the terminal downtrend low can precede classification — the band then seeds with AR-first).
  - Next: SOS/SOW displacement default (ATR×1.0) is generous — revisit during M7 confluence rather than tightening on a 24-range sample.
- [ ] Priority ordering for multi-concept candles (Section 8) — pending backtest evidence.