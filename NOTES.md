Trader - Handoff Notes
======================

PURPOSE
Live trading client for MES futures via Interactive Brokers.
Implements the momentum strategy documented in STRATEGY.txt (same folder).
Backtest research lives in /home/oce/Source/Private/StrategyDevelopment/StrategyDevelopment.

WORKFLOW RULES (IMPORTANT)
- Build after EVERY change: dotnet build Trader.csproj
- Read the code before discussing it - the user edits files between sessions/messages.
- Classic C# style ONLY:
  * Explicit using statements (no ImplicitUsings)
  * Explicit namespace + class Program + static Main (no top-level statements)
  * Nullable DISABLED project-wide. No ? annotations. Nulls should fail loudly at runtime.
- User develops some parts themselves; only do what is explicitly asked.
- All output flows through client.NotifySubscriber() -> OnNotification callback ->
  Console + trader.log (Logger class). Keep it that way, one unified pipe.

CURRENT STATE (as of Aug 2026)
- Connects to IB Gateway 127.0.0.1:4001, subscribes contract id 1005, MES FUT
  (front month by LastTradeDate, looked up via GetContractData).
- Pipeline live:
  OnMarketDataPriceUpdate -> mid price (bid+ask)/2 -> CandleAggregator.OnTick
  -> 1-min candle closed -> SignalDetector.OnCandleClosed -> Signal object.
- SignalDetector: rolling window of 1000 log returns, needs >= 60 bars before signaling,
  fires when |log return| >= 2.0 * stdev. Direction = same as the spike (momentum).
- REAL ORDERS ARE PLACED. OrderManager.OnSignal submits a 1-lot bracket per signal:
  parent LMT entry crossing the spread - best ask for LONG, best bid for SHORT
  (Program tracks the latest PriceUpdate and passes it in),
  child STP stop (2.0*stdev distance), tick-rounded (0.25), DAY TIF.
  NO take-profit order: trailing stop only. Backtest showed the target was capping
  profits - trail-only nearly quadrupled train P&L with lower drawdown (see STRATEGY.txt).
- Trailing stop (OrderManager.OnCandleClosed, every closed candle while position Filled):
  tracks best price since fill; once profit >= ActivationDistance
  (0.5 * legacy 1.5*stdev target distance), resends the STP with a new AuxPrice trailing
  TrailDistance (0.5 * initial stop distance) behind best price; ratchet-only, never loosens.
  Contract cached on first OnSignal and reused for modifications.
- One position at a time: new signals ignored while tracked order is Submitted/Filled.
- Order state persisted to order_state.json (OrderStore, JSON incl. enum as string);
  OnOrderStatus moves Submitted->Filled->Closed/Cancelled and clears the store on exit/cancel.
FILES
- Program.cs            wiring: logger, client, aggregator, detector, store, manager,
                        contract lookup, market data / order status handlers, Ctrl+C exit.
- Models/Candle.cs      OHLCV + TickCount, incremental Update(price). double types.
- Models/CandleAggregator.cs  ticks -> minute candles, fires OnCandleClosed on rollover.
- Models/SignalDetector.cs    returns Signal or null per closed candle.
- Models/Signal.cs      signal DTO: direction, return/stdev bps, ratio, price.
- Models/OrderManager.cs      signal -> bracket (entry + stop); candle-close trailing stop
                              modifications; order status state machine.
- Models/TrackedOrder.cs      persisted position/order state + TradeDirection/Status enums.
- Models/OrderStore.cs  Load/Save/Clear of TrackedOrder in order_state.json.
- Models/Logger.cs      timestamped lines to console + trader.log (AutoFlush).
- STRATEGY.txt          full strategy spec incl. backtest results.

NEXT STEPS (per STRATEGY.txt, in order)
1. Max hold 50 bars -> market exit.
2. Session boundary handling: close open position at end of RTH, no entry on first bar of session.

BACKTEST REFERENCE (net of $12.50 spread/trade, ES; divide by 10 for MES)
Train Aug'25-Apr'26: $40,544 P&L, 45.3% WR, Sharpe 10.42, max DD $1,265.
Test  May'26-Aug'26: $197,356 P&L, 79.6% WR, Sharpe 43.3,  max DD $215.
