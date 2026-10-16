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
