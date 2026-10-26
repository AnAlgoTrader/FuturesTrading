using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FootPrintTrader.Helper;

public class MarketInterpretation
{
    public string Title { get; set; } = "";
    public string Tone { get; set; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}

public static class FootprintInterpreter
{
    public const int MinTicksForTrade = 10; // prior-bar range < 10 ticks = compressed (2.5 pts on ES)

    public static MarketInterpretation Interpret(
        OrderFlowContext context,
        FootprintSnapshot snapshot,
        double tickSize)
    {
        var interp = new MarketInterpretation();
        if (context == null || snapshot == null)
            return interp;

        var completed = snapshot.Completed ?? Array.Empty<FootprintBarData>();
        var bar = snapshot.Current ?? completed.LastOrDefault();
        if (bar == null)
            return interp;

        var dec = FootprintAnalyzer.Decimals(tickSize);
        string P(double v) => v.ToString("F" + dec, CultureInfo.InvariantCulture);

        var signals = context.Signals ?? new List<OrderFlowSignal>();

        // Conflict uses only high-priority (structural) signals, so a routine two-sided
        // auction — developing/retest imbalances on both sides — no longer forces STAND ASIDE.
        var strong = signals.Where(s => s.Priority <= FootprintAnalyzer.PriorityDivergence).ToList();
        var hasBullStrong = strong.Any(s => s.Tone == "bullish");
        var hasBearStrong = strong.Any(s => s.Tone == "bearish");

        // Range filter is based on the last COMPLETED bar, not the developing bar.
        var refBar = completed.Length > 0 ? completed[^1] : bar;
        var range = Math.Max(0, refBar.High - refBar.Low);

        if (hasBullStrong && hasBearStrong)
        {
            interp.Title = "Conflicting Auction (Bracket)";
            interp.Tone = "warning";
            interp.Action = "STAND ASIDE";
            interp.Details = range < tickSize * MinTicksForTrade
                ? $"Opposing structural flow inside a compressed {range.ToString("F" + dec, CultureInfo.InvariantCulture)}-pt range."
                : "Opposing structural flow. Wait for one side to take control.";
            return interp;
        }

        if (range < tickSize * MinTicksForTrade && context.Bias != "Rotational / Balance")
        {
            interp.Title = $"{context.Bias} (Range Compressed)";
            interp.Tone = "warning";
            interp.Action = "STAND ASIDE";
            interp.Details = $"Prior bar range is only {range.ToString("F" + dec, CultureInfo.InvariantCulture)} pts. Poor risk/reward.";
            return interp;
        }

        if (context.Bias == "Rotational / Balance" || context.BiasTone == "neutral" || string.IsNullOrEmpty(context.Action))
            return interp;

        // Single source of truth: reuse the analyzer's bias/level so both panels agree.
        var level = context.ActionPrice > 0 ? context.ActionPrice : context.PocPrice;

        // Unfinished auction is a reference price (unfinished business / magnet), not a
        // directional entry — surface the target without forcing a LONG/SHORT trigger.
        if (context.Bias == "Unfinished High" || context.Bias == "Unfinished Low")
        {
            interp.Title = context.Bias;
            interp.Tone = context.BiasTone;
            interp.Action = context.Action;
            interp.Details = Describe(context, level, P);
            return interp;
        }

        interp.Title = context.Bias;
        interp.Tone = context.BiasTone;
        interp.Action = context.BiasTone == "bearish" ? $"SHORT @ {P(level)}" : $"LONG @ {P(level)}";
        interp.Details = Describe(context, level, P);

        return interp;
    }

    private static string Describe(OrderFlowContext context, double level, Func<double, string> P)
    {
        var top = context.Signals?.FirstOrDefault();
        if (top == null)
            return "";

        return top.Type switch
        {
            "trapped_traders" => top.Tone == "bearish"
                ? $"Longs trapped below {P(level)}. Look for continuation lower."
                : $"Shorts trapped above {P(level)}. Look for a squeeze higher.",
            "absorption" => top.Tone == "bearish"
                ? $"Heavy buying absorbed at {P(level)}; seller defending the high."
                : $"Heavy selling absorbed at {P(level)}; buyer defending the low.",
            "divergence" => top.Tone == "bearish"
                ? $"New high on weaker cumulative delta; buyers exhausted at {P(level)}."
                : $"New low on stronger cumulative delta; sellers exhausted at {P(level)}.",
            "exhaustion" => top.Tone == "bearish"
                ? $"Buying exhaustion at {P(level)}."
                : $"Selling exhaustion at {P(level)}.",
            "unfinished_auction" => top.Tone == "bearish"
                ? $"Unfinished low at {P(level)}: buyers and sellers both traded the extreme; the auction is unfinished and may be revisited."
                : $"Unfinished high at {P(level)}: buyers and sellers both traded the extreme; the auction is unfinished and may be revisited.",
            _ => top.Tone == "bearish"
                ? $"Aggressive sellers in control. Sell rallies to {P(level)}."
                : $"Aggressive buyers in control. Buy pullbacks to {P(level)}."
        };
    }
}
