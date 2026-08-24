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
- Common/ shared project: Candle, CandleAggregator, SignalDetector, Signal, OrderManager,
  TrackedOrder, OrderStore, Logger, FuturesContract (front-month lookup), TradeJournal.
  Both Trader and UI reference it. Classic style, nullable disabled.
- Trader console app and UI Avalonia app run the IDENTICAL engine via Common.
  DO NOT RUN BOTH AT ONCE - same client id 0 on Gateway 4001, would double-trade.
- Connects to IB Gateway 127.0.0.1:4001, subscribes contract id 1005, MES FUT
  (front month by LastTradeDate, looked up via FuturesContract.GetFrontMonth).
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
- Max hold: BarsHeld counted per closed candle after fill; at 50 bars -> timed exit.
- Session boundary: gap > 60 min between consecutive candles = session break
  (same rule as backtest). Open position force-exited; first bar of new session blocks entries.
- Timed/forced exits use a marketable LIMIT crossing the spread (SELL@bid close long,
  BUY@ask close short), never market orders. Flow: status -> Exiting, cancel protective
  stop, place exit LMT. Rare stop/exit double-fill race surfaces a loud VERIFY message;
  exit leg dead without fill also alerts loudly.
- One position at a time: new signals ignored while tracked order is Submitted/Filled.
- Order state persisted to order_state.json (OrderStore, JSON incl. enum as string);
  OnOrderStatus moves Submitted->Filled->Closed/Cancelled and clears the store on exit/cancel.
- UI app: Avalonia + WebView lightweight-charts, live forming candles per tick,
  entry arrow + trailing stop line from order_state.json transitions,
  closed trades appended to trade_journal.csv (TradeJournal: symbol, direction,
  quantity, entry/exit time+price, bars held, PnL in $ via point value MES=5/ES=50).
FILES
- Common/Candle.cs           OHLCV + TickCount, incremental Update(price). double types.
- Common/CandleAggregator.cs ticks -> minute candles; OnCandleClosed on rollover,
                             OnCandleUpdated every tick.
- Common/SignalDetector.cs   returns Signal or null per closed candle.
- Common/Signal.cs           signal DTO: direction, return/stdev bps, ratio, price.
- Common/OrderManager.cs     signal -> bracket (entry + stop); candle-close trailing stop
                             modifications, max-hold and session-break exits; order status
                             state machine incl. Exiting race handling; optional TradeJournal.
- Common/TrackedOrder.cs     persisted position/order state + TradeDirection/Status enums.
- Common/OrderStore.cs       Load/Save/Clear of TrackedOrder in order_state.json.
- Common/TradeJournal.cs     CSV trade log appended when a position closes.
- Common/FuturesContract.cs  front-month FUT lookup by LastTradeDate.
- Common/Logger.cs           timestamped lines to console + log file (AutoFlush).
- STRATEGY.txt          full strategy spec incl. backtest results.

NEXT STEPS
- Strategy spec (STRATEGY.txt) fully implemented. Remaining known limitation:
  restart does not reconcile order_state.json against IB open orders
  (IbClient.OnOpenOrderUpdate exists, unwired) - a crash between placing and
  saving could orphan a live bracket; reconcile manually or wire it up.

BACKTEST REFERENCE (net of $12.50 spread/trade, ES; divide by 10 for MES)
Train Aug'25-Apr'26: $40,544 P&L, 45.3% WR, Sharpe 10.42, max DD $1,265.
Test  May'26-Aug'26: $197,356 P&L, 79.6% WR, Sharpe 43.3,  max DD $215.
Trail-only variant:  Train $156,811 (Sharpe 20.58, DD $438); Test $210,003 (Sharpe 45.0, DD $173).
