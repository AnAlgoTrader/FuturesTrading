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
- Connects to IB Gateway 127.0.0.1:4001, contract id 1005, MES FUT (front month by LastTradeDate).
- Pipeline live:
  OnMarketDataPriceUpdate -> mid price (bid+ask)/2 -> CandleAggregator.OnTick
  -> 1-min candle closed -> SignalDetector.OnCandleClosed -> signal string -> NotifySubscriber.
- SignalDetector: rolling window of 1000 log returns, needs >= 60 bars before signaling,
  fires when |log return| >= 2.0 * stdev. Direction = same as the spike (momentum).
- Entry signal is LOG-ONLY. No orders are placed yet.

FILES
- Program.cs            wiring: logger, client, aggregator, detector, contract lookup, Ctrl+C exit.
- Models/Candle.cs      OHLCV + TickCount, incremental Update(price). double types.
- Models/CandleAggregator.cs  ticks -> minute candles, fires OnCandleClosed on rollover.
- Models/SignalDetector.cs    returns signal string or null per closed candle.
- Models/Logger.cs      timestamped lines to console + trader.log (AutoFlush).
- STRATEGY.txt          full strategy spec incl. backtest results.

NEXT STEPS (per STRATEGY.txt, in order)
1. Position state machine on signal: entry price, best-price-since-entry, bars held.
2. Time-scaled stop/target from stdev: target = 1.5*stdev*sqrt(hold)*price/1e4,
   stop = 2.0*stdev*sqrt(hold)*price/1e4. Log exits (no real orders yet).
3. Trailing stop: activate at 50% of target profit, trail 50% of stop distance behind best price.
4. Max hold 50 bars -> market exit. Session boundary handling.
5. Only then: order placement via IbClient.

BACKTEST REFERENCE (net of $12.50 spread/trade, ES; divide by 10 for MES)
Train Aug'25-Apr'26: $40,544 P&L, 45.3% WR, Sharpe 10.42, max DD $1,265.
Test  May'26-Aug'26: $197,356 P&L, 79.6% WR, Sharpe 43.3,  max DD $215.
