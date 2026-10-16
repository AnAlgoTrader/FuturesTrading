# FootPrintPanel — NinjaTrader 8 Add-On

A NinjaTrader 8 add-on window that shows the **order-flow side panel** from the
FootPrintTrader Photino app: footprint analyzer, interpreter, auction bias,
order-flow signals and live bar metrics. NinjaTrader renders the footprint chart
itself; this panel runs your own analysis stream next to it.

```
Control Center → New → Order Flow Panel
┌─────────────────────────────────────┐
│ ORDER FLOW CONTEXT            ● LIVE│
│ [ instrument selector           ]   │
│ ES 06-26 (CME)                       │
│ ┌ INTERPRETATION ────────────────┐ │
│ │ Trapped Buyers / LONG @ 5312.25│ │
│ ├ AUCTION BIAS ──────────────────┤ │
│ │ Trapped Buyers  Fade at 5312.25│ │
│ ├ ORDER FLOW SIGNALS (3) ────────┤ │
│ │ ▎Trapped Buyers   10:42        │ │
│ ├ LIVE BAR METRICS ──────────────┤ │
│ │ Bar Delta  +412  │ Delta % +18 │ │
│ ├ AUCTION STRUCTURE ─────────────┤ │
│ │ Cum. Delta +2.1k │ Divergence …│ │
│ └────────────────────────────────┘ │
└─────────────────────────────────────┘
```

## What's inside

| File | Role |
|------|------|
| `Core/TradingConfig.cs` | Trades-per-bar constant (1000) |
| `Core/FootprintModels.cs` | FootprintLevel / FootprintBarData / FootprintSnapshot |
| `Core/FootprintClassifier.cs` | Tick→N-trade-bar aggregation with buy/sell aggressor classification (quote-synced, tick-rule fallback) |
| `Core/OrderFlowModels.cs` | OrderFlowSignal / OrderFlowZone / OrderFlowContext / MarketInterpretation |
| `Core/FootprintAnalyzer.cs` | Signal engine: stacked imbalance, absorption, trapped traders, CVD divergence, unfinished auctions |
| `Core/FootprintInterpreter.cs` | LONG/SHORT/STAND ASIDE recommendation + time helpers |
| `Panel/FlowPanelAddOn.cs` | `AddOnBase` — adds the entry to Control Center → New |
| `Panel/FlowPanelWindow.cs` | `NTWindow` + tab factory (workspace-persisted, multi-tab) |
| `Panel/FlowPanelTab.cs` | `NTTabPage` — instrument selector, L1 subscription, 1s refresh, all cards |
| `Panel/PanelTheme.cs` | Dark theme colours + WPF card/metric builders |

The core engine is a faithful port of the FootPrintTrader analyzer/interpreter
(C# 8 / .NET Framework 4.8 compatible). The analyzer is **instance-based** here
(one per tab), so two panels on different instruments never mix state.

## Install

1. Close NinjaTrader 8.
2. Copy the `Core/` and `Panel/` `.cs` files into:
   ```
   Documents\NinjaTrader 8\bin\Custom\AddOns\FootPrintPanel\
   ```
   (any subfolder of `bin\Custom` works; flat or Core+Panel subfolders both fine)
3. Start NinjaTrader 8, open **New → NinjaScript Editor**, press **F5** (compile).
   Fix any errors reported at this step (see Troubleshooting).
4. The panel appears under **Control Center → New → Order Flow Panel**.

> Alternatively use the official external-IDE workflow: in the NinjaScript Editor
> click "Open project in Visual Studio", add these files to the generated
> project, and build — XAML/DLL deployment is not required for this add-on since
> the UI is built in C#.

## Usage

1. Open **New → Order Flow Panel**.
2. Pick an instrument in the panel's selector (e.g. `ES 06-26` or `MES 06-26`).
   The tab header follows the selection, and the panel participates in NT's
   instrument linking (link-color a chart to the panel to keep them in sync).
3. The panel subscribes to L1 market data (bid/ask/last) and builds 1000-trade
   footprint bars, exactly like the Photino app did with IB tick-by-tick data.
4. Everything updates once per second. Signals need a warm-up period: the first
   completed bars must arrive before zones/divergence have anything to chew on
   (a bar closes every 1000 trades).

Multiple tabs are supported (the + button) — each tab is fully independent.

## Data notes

- **Trade classification**: trades at/above the ask are aggressive buys, at/below
  the bid aggressive sells; if the quote hasn't caught up (or no L1 quotes are
  available) the classifier falls back to mid-price and then the tick rule —
  same behaviour as the original app on a thin feed.
- **Tick size** is taken from the instrument (`MasterInstrument.TickSize`), so
  ES/MES use 0.25 automatically.
- A CME L1 subscription (or trial) is required for live bid/ask; without quotes
  the panel still works on last-price + tick rule.

## Troubleshooting

- **Compile errors about `NTTabPage` members** — NT point releases occasionally
  change abstract members. Compare against the official docs sample:
  https://ninjatrader.com/support/helpGuides/nt8/creating_your_own_addon_window.htm
- **Menu item missing** — check the NinjaScript Output window
  (New → NinjaScript Output) for compile errors; the add-on only appears after
  a successful compile.
- **Panel shows "Waiting for Data..."** — normal until 1000 trades have printed
  (first completed bar) and L1 data is flowing on the selected instrument.
- **To tweak trades-per-bar**, edit `Core/TradingConfig.cs` (`TicksPerBucket`)
  and recompile (F5).
