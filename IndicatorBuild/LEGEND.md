# Indicator color & marker legend

How to read the chart produced at `output/index.html` (and the outcome report at `output/dashboard.html`).

## Shared colors

| Color | Meaning |
|---|---|
| Teal `#26A69A` | Bullish / continuation (typically). |
| Red `#EF5350` | Bearish / reversal (typically). |
| Orange `#FFB74D` | Exhaustion, divergence, Wyckoff climaxes (`SC`/`BC`) and false-breakouts (`Spring`/`UTAD`). |
| Purple `#7E57C2` | Compression / squeeze state, pullbacks (`PB`), consolidation zones. |
| Gray `#808080` | No confirmed signal (or below the M7 confluence gate). |
| Orange line `#FF9900` | Anchored VWAP — session volume-weighted mean price; magnet / trend reference. |

## Confluence gating (M7)

Actionable signals keep their color **only if Qualified** — i.e. the confluence score ≥ 2, where each of these counts 1 point:

- **Trend** — close is on the signal's side of the 50-EMA.
- **Volume** — bar volume ≥ 1.5× the 50-bar average.
- **Displacement** — bar range ≥ 0.7× ATR (real participation).
- **Level significance** — close within 0.35× ATR of an active zone boundary or a recent confirmed swing.

Below-gate events render **gray** and their traded markers are suppressed. Compression is never gated (pure state). Structure markers (`BOS`/`CHoCH`) always render; a CHoCH candle's *color* is gated but its marker is not. Outcome statistics on `dashboard.html` compare **all** events vs the **gated** subset per signal (`Δ` = gating's precision change).

## Candle colors — priority order

One candle gets one color, first matching rule wins.

1. **CHoCH candle** (teal/red) — structure flipped. First break of the swing structure. Context marker, not a trade (~45–46% forward win).
2. **Liquidity sweep** (teal/red) — wick took out the swing extreme and closed back inside; direction = reversal bias (Big-player liquidity grab).
3. **Breakout** (teal/red) — closed outside a consolidated range with follow-through.
4. **Exhaustion or divergence** (orange) — wick rejections; both are *reversal* signals.
5. **Wyckoff bar events** (teal/red) — SOS/SOW and Phase-E accumulation/distribution breakouts.
6. **Compression** (purple) — TTM squeeze on; coiling setup state.
7. **Expansion** (teal/red) — squeeze release / ATR volatility spike; direction of expansion.
8. **Default** (gray) — nothing confirmed.

## Markers (labels on the chart)

| Label | Shape / side | Color | Meaning & action |
|---|---|---|---|
| `BOS` / `CHoCH` | arrow up/down | Teal/Red | Break of structure (continuation) / change of structure (reversal). Mark the regime shift. |
| `Sweep` | circle up/down | Teal/Red | Stop hunt of a swing/liquidity level. Possible reversal bias. |
| `FVG` | square up/down | Teal/Red | Fair-value gap / imbalance formed. Price tends to revisit (fill) it — 96% filled on the backtest. |
| `BO` | arrow up/down | Teal/Red | Consolidation breakout. |
| `PB` | circle up/down | Purple | **Pullback into the healthy Fib band (0.236–0.786)** of an impulse leg. Highest-conviction signal (~84–88% forward win) — look for continuation entries here. |
| `SC` / `BC` | diamond below | Orange | Wyckoff Selling/Buying Climax — panic end of a trend leg. |
| `AR` | diamond | Teal/Red | Automatic Rally — relief rebound after selling pressure. |
| `ST` | diamond | Teal/Red | Secondary Test — re-visit of the range low/high after the AR. |
| `Spring` / `UTAD` | circle below | Orange | Wyckoff shakeout false-breakout under accumulation / over distribution — last trap before Phase E. |
| `SOS` / `SOW` | diamond up/down | Teal/Red | Sign of Strength / Weakness — displacement out of the Wyckoff range. |
| `LPS` / `LPSY` | diamond up/down | Teal/Red | Last Point of Support / Supply — final test before the phase-E move. |
| `PhaseE` | arrow up/down | Teal/Red | Phase-E break of accumulation (up) / distribution (down). |
| `Exh` | circle up/down | Orange | Exhaustion bar — rejection wick ≥ body, volume spike, extreme RSI. Reversal bias. |
| `Div` / `DivH` | diamond up/down | Orange | Regular (Div) / hidden (DivH) RSI-price divergence. Div = reversal, DivH = continuation; hidden divergences run ~83–87% on the backtest. |
| `Sq` | diamond below | Purple | TTM squeeze on — volatility coiling. |
| `Exp` | arrow up/down | Teal/Red | Squeeze release / volatility expansion — the "fire" companion to the `Sq` setup. |

## Zone overlays (tinted rectangles)

| Fill | Zone | Notes |
|---|---|---|
| Teal / Red ~16% alpha | Fair Value Gap | Bullish/bearish imbalance; expect price to return and fill. |
| Purple ~14% alpha | Consolidation range | Range before breakout. |
| Teal ~12% alpha | Wyckoff accumulation | Bottoming structure. |
| Red ~12% alpha | Wyckoff distribution | Topping structure. |

## Measured signal quality (1-minute bars, forward-10-bar window)

Reported on `dashboard.html`; treat it as descriptive, never as signal-feedback. **These are in-sample numbers.** The dashboard also reserves the last 20% of bars (`--holdout`, M9) as a never-tuned tail and reports holdout N/win% per signal:

- **High conviction (in-sample only):** Pullback Bullish ~88%, hidden Bullish divergence 83–87%, hidden Bearish divergence ~85%, Wyckoff distribution-out ~62% — but holdout N for each is ~0–3, so these edges are not certified out-of-sample.
- **Coin-flip / context only:** Breakout 43–56%, CHoCH 45–46% (in-sample) → **34–36% on holdout**, sweep-confirmed 43–47%, exhaustion 42–45%, expansion 38–43%. Treat these as regime/context flags, not standalone entries.
- Rule of thumb from the holdout run: no signal yet has enough out-of-sample samples to be called an edge; collect more data or higher timeframes before trusting any win-rate.