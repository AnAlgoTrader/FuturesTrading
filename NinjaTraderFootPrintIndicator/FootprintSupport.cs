#region Using declarations
using System;
#endregion

namespace FootPrintTrader.Helper
{
	// A single price level (footprint cell) of a bar.
	public class FootprintLevel
	{
		public double Price { get; set; }
		public decimal BidVolume { get; set; }
		public decimal AskVolume { get; set; }
		public decimal Total { get; set; }
	}

	// One bar's footprint: OHLC, aggregate flow metrics and the per-price levels.
	public class FootprintBarData
	{
		public long StartTime { get; set; } // unix seconds of the bar open
		public double High { get; set; }
		public double Low { get; set; }
		public double Close { get; set; }
		public decimal Delta { get; set; }
		public decimal Volume { get; set; }
		public int TradeCount { get; set; }
		public FootprintLevel[] Levels { get; set; }
	}

	// The snapshot handed to the analyzer each tick: the closed-bar window + the developing bar.
	public class FootprintSnapshot
	{
		public FootprintBarData[] Completed { get; set; }
		public FootprintBarData Current { get; set; }
	}

	// Tunable order-flow thresholds.
	//
	// Every value is either a ratio between two volumes, a percentage of a rolling baseline
	// (average bar volume / range / trade count over the recent window), or a bar count for a
	// time horizon. There are no absolute price or trade-count thresholds, so the detector
	// tracks volatility as it changes within the session instead of being tuned to one
	// instrument's typical range.
	//
	// Percent values are whole numbers: 25 means 25%, not 0.25. Every consumer of a percent
	// value must divide by 100 before using it as a multiplier - see the analyzer's
	// MinLiveTradesPct / MinBarVolumePct / MinLevelVolumePct / AbsorptionSidePct sites.
	//
	// Defaults are tuned for ES on 1000-tick bars during RTH. The previous 3.0 / 25% pair was
	// tuned for a large-tick instrument: on ES it demanded a 4-consecutive-level 3:1 diagonal
	// stack, which essentially never prints, so stacked-imbalance zones (and the trapped /
	// retest signals derived from them) stayed empty. 2.0 / 15% is the realistic ES setting.
	public class FootprintSettings
	{
		// ask[i+1] >= bid[i] * ratio on adjacent price levels. Already a ratio, not a percent.
		// 2.0 is the ES-friendly setting; 3.0+ is for large-tick instruments (CL, 6E).
		public double ImbalanceRatio { get; set; } = 2.0;

		// An imbalance run must span at least this % of the recent average bar range before it
		// counts as "stacked". Replaces a fixed level count. The analyzer caps the resulting
		// run at 4 consecutive levels: the percentage is meant to lower the bar on tight
		// bars, not to demand an impossible run on wide ones. 15% lands the ES 1000-tick
		// required-run at 2-3 levels instead of pinning it at the 4-level cap.
		public double ImbalanceRunRangePct { get; set; } = 15;

		// A bar must carry at least this % of the recent average bar volume to be considered.
		public double MinBarVolumePct { get; set; } = 25;

		// An individual price level must carry at least this % of its own bar's average level
		// volume, filtering out levels that are too thin to read. 5% was barely a filter: it
		// let thin prints form imbalances wherever the opposite side was zero.
		public double MinLevelVolumePct { get; set; } = 25;

		// The live bar must show at least this % of the recent average trade count before
		// live-bar signals (stacked imbalance, exhaustion, absorption) are allowed. On tick
		// bars every completed bar holds ~bar-period trades, so this is effectively "bar is
		// X% complete". Raised from 35 to 60: at 35% the live bar's level ladder is still
		// truncated, which can fabricate "consecutive level" imbalances out of a partial
		// print. 60 delays live signals to roughly the last third of a ~30 s bar.
		//
		// UNIT: this is a percent (60 = 60%). Every site that uses it must divide by 100.
		public double MinLiveTradesPct { get; set; } = 60;

		// Minimum number of price levels the live bar must have printed before live-bar
		// detection is allowed. Guards against reading a "stacked" run off a truncated ladder
		// early in the bar. A 1000-tick ES bar typically visits 40-100 levels, so 8 is a
		// conservative floor that still fires on quiet bars.
		public int MinLiveLevels { get; set; } = 8;

		// A level qualifies as absorption when its volume exceeds mean + sigma * std-dev of the
		// rolling level-volume distribution.
		public double AbsorptionSigma { get; set; } = 3.0;

		// The absorption level must sit within this % of the recent average bar range from the
		// bar's extreme. Replaces a fixed tick distance.
		public double AbsorptionEdgePct { get; set; } = 25;

		// One aggressor side must account for at least this % of the level's total volume.
		//
		// UNIT: this is a percent (60 = 60%). The analyzer divides it by 100 before use.
		public double AbsorptionSidePct { get; set; } = 60;

		// Minimum confirmation depth, in ticks, for a rejection to count as absorption. On a
		// cold start _avgBarRange is 0, which would otherwise collapse the depth to one tick
		// and let a single tick confirm absorption. Scaled by the live tick size at runtime.
		// The analyzer takes the SMALLER of this floor and the range-scaled edge depth, so a
		// quiet session does not require an outsized move to confirm.
		public double AbsorptionMinDepthTicks { get; set; } = 3.0;

		// How many bars a stacked-imbalance zone stays active before it is discarded.
		// Deliberately in bars, not percent: this is a time horizon, not a volatility measure.
		// 20 bars is ~10 min on 1000-tick bars, and matches the 25-bar snapshot size.
		public int MaxZoneAgeBars { get; set; } = 20;
	}

	// Formatting helpers shared by the analyzer / interpreter / panel.
	public static class FootprintRenderBuilder
	{
		public static string FormatTime(long unixSeconds)
		{
			if (unixSeconds <= 0)
				return "";

			try
			{
				return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("HH:mm");
			}
			catch
			{
				return "";
			}
		}
	}
}