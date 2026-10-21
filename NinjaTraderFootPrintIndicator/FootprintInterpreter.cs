#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
#endregion

namespace FootPrintTrader.Helper
{
	public class MarketInterpretation
	{
		public string Title { get; set; } = "";
		public string Tone { get; set; } = "neutral";
		public string Action { get; set; } = "";
		public string Details { get; set; } = "";
	}

	public static class FootprintInterpreter
	{
		// Fallback only, used before enough completed bars exist to establish an average range.
		public const int MinTicksForTrade = 10;

		// The last bar is "compressed" when its range is under this fraction of the recent
		// average bar range. Relative, so it means the same thing on any instrument.
		private const double MinCompressedRangeRatio = 0.4;

		public static MarketInterpretation Interpret(
			OrderFlowContext context,
			FootprintSnapshot snapshot,
			double tickSize)
		{
			var interp = new MarketInterpretation();
			if (context == null || snapshot == null)
				return interp;

			var completed = snapshot.Completed ?? new FootprintBarData[0];
			var bar = snapshot.Current ?? completed.LastOrDefault();
			if (bar == null)
				return interp;

			var dec = FootprintAnalyzer.Decimals(tickSize);
			string P(double v) { return v.ToString("F" + dec, CultureInfo.InvariantCulture); }

			var signals = context.Signals ?? new List<OrderFlowSignal>();

			var strong = signals.Where(s => s.Priority <= FootprintAnalyzer.PriorityDivergence).ToList();
			var hasBullStrong = strong.Any(s => s.Tone == "bullish");
			var hasBearStrong = strong.Any(s => s.Tone == "bearish");

			var refBar = completed.Length > 0 ? completed[completed.Length - 1] : bar;
			var range = Math.Max(0, refBar.High - refBar.Low);
			var rangeText = range.ToString("F" + dec, CultureInfo.InvariantCulture);

			var compressed = context.AvgBarRange > 0
				? range < context.AvgBarRange * MinCompressedRangeRatio
				: range < tickSize * MinTicksForTrade;

			if (hasBullStrong && hasBearStrong)
			{
				interp.Title = "Conflicting Auction (Bracket)";
				interp.Tone = "warning";
				interp.Action = "STAND ASIDE";
				interp.Details = compressed
					? "Opposing structural flow inside a compressed " + rangeText + "-pt range."
					: "Opposing structural flow. Wait for one side to take control.";
				return interp;
			}

			if (compressed && context.Bias != "Rotational / Balance")
			{
				interp.Title = context.Bias + " (Range Compressed)";
				interp.Tone = "warning";
				interp.Action = "STAND ASIDE";
				interp.Details = "Prior bar range is only " + rangeText + " pts vs a " +
					context.AvgBarRange.ToString("F" + dec, CultureInfo.InvariantCulture) +
					"-pt average. Poor risk/reward.";
				return interp;
			}

			if (context.IsNeutral || context.Bias == "Rotational / Balance" || context.BiasTone == "neutral" || string.IsNullOrEmpty(context.Action))
				return interp;

			var level = context.ActionPrice > 0 ? context.ActionPrice : context.PocPrice;

			interp.Title = context.Bias;
			interp.Tone = context.BiasTone;

			// The analyzer owns the action wording. Rewriting it here used to produce
			// "SHORT @ <level below market>" for a retest entry, which reads as a market order
			// at an unfillable price.
			interp.Action = context.Action;
			interp.Details = Describe(context, level, P);

			if (context.DivergenceIsLagging &&
				context.Signals != null &&
				context.Signals.Any(s => s.Type == "divergence"))
			{
				interp.Details += " (computed on last closed bar)";
			}

			return interp;
		}

		private static string Describe(OrderFlowContext context, double level, Func<double, string> P)
		{
			var top = context.Signals != null ? context.Signals.FirstOrDefault() : null;
			if (top == null)
				return "";

			switch (top.Type)
			{
				case "trapped_traders":
					return top.Tone == "bearish"
						? "Longs trapped below " + P(level) + ". Expect a failed retest of " + P(level) + "."
						: "Shorts trapped above " + P(level) + ". Expect a failed retest of " + P(level) + ".";
				case "absorption":
					return top.Tone == "bearish"
						? "Heavy buying absorbed at " + P(level) + "; seller defending the high."
						: "Heavy selling absorbed at " + P(level) + "; buyer defending the low.";
				case "divergence":
					return top.Tone == "bearish"
						? "New high on weaker cumulative delta; buyers exhausted at " + P(level) + "."
						: "New low on stronger cumulative delta; sellers exhausted at " + P(level) + ".";
				case "exhaustion":
					return top.Tone == "bearish"
						? "Buying exhaustion at " + P(level) + "."
						: "Selling exhaustion at " + P(level) + ".";
				default:
					return top.Tone == "bearish"
						? "Aggressive sellers in control. Sell rallies to " + P(level) + "."
						: "Aggressive buyers in control. Buy pullbacks to " + P(level) + ".";
			}
		}
	}
}