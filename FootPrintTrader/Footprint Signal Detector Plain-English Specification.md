## 1. Purpose

This document describes, without code, a set of rules that scan completed footprint bars and flag moments where buyers or sellers may be getting absorbed, where a breakout may be confirming, or where the market's pace is changing. It is meant to be implemented inside your own charting program.

Important: the rules were tested on only three sessions, all of which were up days. Only one signal (sell absorption) showed any edge, and a small one. Treat everything else as a thing to watch and log, not to trade on. This is not financial advice.

## 2. Numbers to calculate for each bar

- **Delta:** total volume traded at the ask minus total volume traded at the bid, summed over every price row in the bar.
- **CVD (cumulative delta):** a running total of delta from the first bar of the session. Reset it at the start of each session.
- **Swing low and swing high:** the lowest low and the highest high of the previous 10 bars, not including the current bar.
- **Buy imbalance count:** for each price row, compare the ask volume at that price with the bid volume one tick lower. If the ask is at least 10 contracts and at least 3 times that bid, it counts as one buy imbalance. The bar's count is how many rows qualify. The rows do not need to be adjacent.
- **Speed:** the number of seconds the bar took to complete. Compare it with the median seconds of the previous 20 bars. Only do this once at least 8 earlier bars exist.

## 3. The five signals

Check each signal once, at the moment a bar closes, using only that bar and earlier bars.

### Signal 1: Sell absorption (bullish)

Fires when the bar's delta is -250 or lower, and the bar's low did not break the swing low by more than 1 tick. In words: heavy selling hit the market, but price refused to go lower.

### Signal 2: Buy absorption (bearish)

The mirror image. Fires when delta is +250 or higher, and the bar's high did not exceed the swing high by more than 1 tick. Heavy buying, but price could not go higher.

### Signal 3: CVD divergence (bearish)

Fires when the bar's CVD is above the CVD of every one of the previous 15 bars, but the bar's high is below the highest high of those 15 bars. Cumulative buying keeps growing while price fails to confirm.

### Signal 4: Breakout confirmation (bullish)

Fires when all three conditions are met: the bar closes above the highest high of the previous 10 bars, delta is +100 or more, and the bar has at least 3 buy imbalances. Add a FAST tag if the bar's seconds are under 70% of the 20-bar median.

### Signal 5: Slowdown (phase indicator)

Fires when the average seconds of the last 3 bars is more than 1.5 times the 20-bar median. It describes the market's pace, not a direction.


## 4. Adjustable settings

These values are starting points, not fixed truths. Expose them as settings so they can be changed.

| Setting | Starting value |
| --- | --- |
| Minimum absolute delta for absorption | 250 |
| Swing lookback (bars) | 10 |
| Tolerance past the swing level | 1 tick |
| CVD divergence lookback (bars) | 15 |
| Breakout lookback (bars) | 10 |
| Minimum delta for breakout | 100 |
| Minimum buy imbalances for breakout | 3 |
| Imbalance ratio and minimum volume | 3 to 1, and 10 contracts |
| FAST threshold | under 70% of median |
| Slowdown threshold | over 150% of median (last 3 bars) |
| Speed median window | 20 bars |
| Repeat control | 3 bars |



