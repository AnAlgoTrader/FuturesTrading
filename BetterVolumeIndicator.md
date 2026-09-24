# Better Volume Indicator (BVI)

The Better Volume Indicator is a port of the classic emini-watch / LazyBear
*Better Volume* indicator. It colorizes every 1-minute MES candle based on the
relationship between **volume** and **price range**, and is meant to surface
cumulative-volume events — exhaustion, indecision and quiet — that a raw price
chart hides.

It is implemented in `DayTrader/Helper/BetterVolumeIndicator.cs` and runs on
both the historical backfill and the live forming candle (the current minute
recolors as it builds).

## How it works

1. **Buy/sell volume split** — each candle's total volume is split into
   estimated buy and sell volume using its OHLC:
   - up candle: `buy = vol * (range / (2*range + open - close))`
   - down candle: `buy = vol * ((range + close - open) / (2*range + close - open))`
   - doji: `buy = sell = vol / 2`

2. **One-bar vs two-bar mode** (`useTwoBars`, default `true`) — signals are
   evaluated on the single current bar, or on the current+previous bar pair.

3. **Lookback window** (`lookBack`, default `20`) — each signal compares against
   the highest/lowest of the same metric over the last 20 bars.

4. **Session gate** — two-bar signals are only fired when both bars fall on the
   same Eastern session date, so a signal can never span the nightly gap or a
   weekend. (Equivalent to the original's `Date == Date[1]` guard.)

5. **Warmup** — the first 19 bars are gray (not enough history for a 20-bar
   window). This happens once at startup/backfill.

## Colors and meaning

| Color | Hex | Signal | Meaning |
|-------|-----|--------|---------|
| Magenta | <span style="background-color:#ff00ff; color:white; padding:0 6px; border-radius:3px;">#ff00ff</span> | Climax + churn | Exhaustion climax *and* churn same bar — maximum-meaningful event |
| Yellow | <span style="background-color:#ffff00; color:black; padding:0 6px; border-radius:3px;">#ffff00</span> | Low volume | One of the lowest volume bars of the window — quiet/paused |
| Red | <span style="background-color:#ff0000; color:white; padding:0 6px; border-radius:3px;">#ff0000</span> | Climax up | Up candle with climaxing buy volume — buying exhaustion |
| White | <span style="background-color:#ffffff; color:black; padding:0 6px; border-radius:3px;">#ffffff</span> | Climax down | Down candle with climaxing sell volume — selling exhaustion |
| Green | <span style="background-color:#009b32; color:white; padding:0 6px; border-radius:3px;">#009b32</span> | Churn | High volume, narrow range — heavy participation, no net resolve |
| Gray | <span style="background-color:#808080; color:white; padding:0 6px; border-radius:3px;">#808080</span> | No signal | Default / warmup / filtered bar |

## Signal priority

When a bar matches several signals, the first match wins (a bar can be e.g. both
a low-volume and a climactic candle):

```
Magenta  (climax + churn)
   > Yellow  (low volume)
   > Red     (climax up)
   > White   (climax down)
   > Green   (churn)
   > Gray    (no signal)
```

## Possible actions

Signals are **context, not entries.** Always trade in the direction of the
broader trend / higher timeframe and manage risk to a plan. These are the common
day-trading reads:

| Color | Read | Possible action |
|-------|------|-----------------|
| Red (climax up) | Buyers exhausted — large up bar into heavy volume | Do **not** chase long; watch for reversal/short if price stalls or breaks the bar's low; scale/trail longs out |
| White (climax down) | Sellers exhausted — large down bar into heavy volume | Do **not** chase short; watch for reversal/long if price stalls or reclaims the bar's high; scale/trail shorts out |
| Yellow (low volume) | Pause / no participation | Stand aside; expect a range and await a volume expansion for the real move; good place to tighten stops or scale out |
| Green (churn) | Heavy volume, tiny range — haggling at a level | No edge here; expect a breakout direction soon — wait for a red/white climax on the breakout to confirm, otherwise fade the churn level |
| Magenta (climax + churn) | Both exhaustion and indecision at the same bar | Strongest single-bar warning — treat like a red/white climax *and* avoid joining the churn; be ready for a reversal and/or sharp move, not a trend continuation |
| Gray | Nothing notable | No action |

### Practical notes for MES

- Magenta/Red/White are worth the most right after a **climax at a key level**
  (session VWAP, prior day high/low, round numbers).
- Yellow clusters usually precede a **volatility expansion** — a yellow bar, then
  a sudden red/white move, is buying/selling the expansion's first leg.
- On a near-24h instrument like MES the session gate keeps the **first bar of
  the session** honest: it never inherits a two-bar signal from the previous
  day's last bar.
- The current-minute recolor is live: a color fixates once the minute closes, so
  only act on colors on **closed** bars.

## Configuration

| Parameter | Default | Effect |
|-----------|---------|--------|
| `lookBack` | `20` | Bars in the highest/lowest comparison window |
| `useTwoBars` | `true` | Evaluate on bar pairs (`false` = single-bar signals only) |

The indicator is instantiated in `CandleAggregator` as
`new BetterVolumeIndicator()` using the defaults above.