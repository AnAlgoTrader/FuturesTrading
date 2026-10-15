using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FootPrintTrader.Helper;

public class MarketInterpretation
{
    public string Title { get; set; } = "Analyzing...";
    public string Tone { get; set; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
    public string Action { get; set; } = "STAND ASIDE";
    public string Details { get; set; } = "Evaluating auction context...";
    public List<string> Factors { get; set; } = new();
    public bool IsConflict { get; set; }
    public bool IsCompressedRange { get; set; }
    public double BarRange { get; set; }
}

public static class FootprintInterpreter
{
    public const double MinViableRangePoints = 2.5; // Under 2.5 pts on ES/MES is a tight bracket/noise
    public const int MinTicksForTrade = 10;

    public static MarketInterpretation Interpret(
        OrderFlowContext context,
        FootprintSnapshot snapshot,
        double tickSize,
        double currentPrice = 0)
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

        var range = Math.Max(0, bar.High - bar.Low);
        interp.BarRange = range;
        var signals = context.Signals ?? new List<OrderFlowSignal>();

        // 1. Conflict Detection: Opposing strong signals (e.g. Bullish Divergence vs Seller Absorption)
        var hasBullishSignal = signals.Any(s => s.Tone == "bullish");
        var hasBearishSignal = signals.Any(s => s.Tone == "bearish");
        var isDirectConflict = hasBullishSignal && hasBearishSignal;

        var hasCeilingAbsorption = signals.Any(s => s.Type == "absorption" && s.Tone == "bearish");
        var hasFloorSupport = signals.Any(s => (s.Type == "divergence" || s.Type == "absorption") && s.Tone == "bullish");

        if (isDirectConflict || (hasCeilingAbsorption && hasFloorSupport))
        {
            interp.IsConflict = true;
            interp.Title = "Conflicting Auction (Bracket)";
            interp.Tone = "warning";
            interp.Action = "STAND ASIDE";

            var ceiling = signals.FirstOrDefault(s => s.Tone == "bearish");
            var floor = signals.FirstOrDefault(s => s.Tone == "bullish");

            var ceilingDesc = ceiling != null ? ceiling.Title : $"Ceiling near {P(bar.High)}";
            var floorDesc = floor != null ? floor.Title : $"Floor near {P(bar.Low)}";

            if (range < MinViableRangePoints)
            {
                interp.IsCompressedRange = true;
                interp.Details = $"Opposing order flow ({ceilingDesc} vs. {floorDesc}) in a compressed {range.ToString("F" + dec, CultureInfo.InvariantCulture)} pt range. High chop risk — do not fade inside this bracket.";
            }
            else
            {
                interp.Details = $"Opposing order flow ({ceilingDesc} vs. {floorDesc}). Price is bracketed between {P(bar.Low)} and {P(bar.High)}. Await breakout resolution.";
            }

            interp.Factors.Add($"Resistance: {ceilingDesc}");
            interp.Factors.Add($"Support: {floorDesc}");
            interp.Factors.Add($"Bar Range: {range.ToString("F" + dec, CultureInfo.InvariantCulture)} pts (Too compressed for inside-bar trade)");
            return interp;
        }

        // 2. Compressed Range Filter: Even if one bias exists, narrow range offers poor R:R
        if (range < tickSize * MinTicksForTrade && context.Bias != "Rotational / Balance")
        {
            interp.IsCompressedRange = true;
            interp.Title = $"{context.Bias} (Range Compressed)";
            interp.Tone = "warning";
            interp.Action = "STAND ASIDE";
            interp.Details = $"Bar range is only {range.ToString("F" + dec, CultureInfo.InvariantCulture)} pts. Insufficient profit margin inside the bar after spread and commissions. Wait for range expansion.";
            interp.Factors.Add($"Current Range: {range.ToString("F" + dec, CultureInfo.InvariantCulture)} pts");
            interp.Factors.Add($"Min Viable Range: {MinViableRangePoints:F2} pts");
            return interp;
        }

        // 3. Trapped Traders
        var trappedSignal = signals.FirstOrDefault(s => s.Type == "trapped_traders");
        if (trappedSignal != null)
        {
            if (trappedSignal.Tone == "bearish")
            {
                interp.Title = "Trapped Buyers (Liquidation Risk)";
                interp.Tone = "bearish";
                interp.Action = $"FADE RALLIES (< {P(context.PocPrice)})";
                interp.Details = "Buyers trapped above the current market. Liquidation pressure favors shorts on pullbacks.";
            }
            else
            {
                interp.Title = "Trapped Sellers (Short Squeeze Risk)";
                interp.Tone = "bullish";
                interp.Action = $"BUY PULLBACKS (> {P(context.PocPrice)})";
                interp.Details = "Sellers trapped below the current market. Short covering pressure favors longs on dips.";
            }
            interp.Factors.Add(trappedSignal.Title);
            return interp;
        }

        // 4. Institutional Absorption
        if (hasCeilingAbsorption && !hasBullishSignal)
        {
            interp.Title = "Institutional Seller Absorption";
            interp.Tone = "bearish";
            interp.Action = $"LEAN SHORT ({context.Action})";
            interp.Details = $"Aggressive market buyers absorbed at {P(bar.High)}. Passive sellers capping price. Fade against high with tight stop.";
            interp.Factors.Add($"Absorption at high: {P(bar.High)}");
            return interp;
        }

        var hasFloorAbsorption = signals.Any(s => s.Type == "absorption" && s.Tone == "bullish");
        if (hasFloorAbsorption && !hasBearishSignal)
        {
            interp.Title = "Institutional Buyer Absorption";
            interp.Tone = "bullish";
            interp.Action = $"LEAN LONG ({context.Action})";
            interp.Details = $"Aggressive market sellers absorbed at {P(bar.Low)}. Passive buyers defending base. Squeeze off low with tight stop.";
            interp.Factors.Add($"Absorption at low: {P(bar.Low)}");
            return interp;
        }

        // 5. Delta Divergence
        if (context.DeltaDivergence == "Bearish" && !hasBullishSignal)
        {
            interp.Title = "Bearish Delta Divergence";
            interp.Tone = "bearish";
            interp.Action = $"WATCH FOR REVERSAL ({P(bar.High)})";
            interp.Details = $"Price tested high on negative delta. Buying momentum exhausted; watch for seller initiative.";
            interp.Factors.Add("Negative delta at bar / session high");
            return interp;
        }

        if (context.DeltaDivergence == "Bullish" && !hasBearishSignal)
        {
            interp.Title = "Bullish Delta Divergence";
            interp.Tone = "bullish";
            interp.Action = $"WATCH FOR REVERSAL ({P(bar.Low)})";
            interp.Details = $"Price tested low on positive delta. Selling momentum exhausted; watch for buyer initiative.";
            interp.Factors.Add("Positive delta at bar / session low");
            return interp;
        }

        // 6. Stacked Imbalance / Initiative Drive
        var initiativeBuy = signals.FirstOrDefault(s => s.Type == "stacked_imbalance" && s.Tone == "bullish");
        if (initiativeBuy != null && !hasBearishSignal)
        {
            interp.Title = "Bullish Initiative Drive";
            interp.Tone = "bullish";
            interp.Action = $"FAVOR LONGS ({context.Action})";
            interp.Details = "Aggressive initiative buyers in control. Look for retest entries near POC / imbalance zone.";
            interp.Factors.Add(initiativeBuy.Title);
            return interp;
        }

        var initiativeSell = signals.FirstOrDefault(s => s.Type == "stacked_imbalance" && s.Tone == "bearish");
        if (initiativeSell != null && !hasBullishSignal)
        {
            interp.Title = "Bearish Initiative Drive";
            interp.Tone = "bearish";
            interp.Action = $"FAVOR SHORTS ({context.Action})";
            interp.Details = "Aggressive initiative sellers in control. Look for bounce short entries near POC / imbalance zone.";
            interp.Factors.Add(initiativeSell.Title);
            return interp;
        }

        // 7. Default Balanced Rotation
        interp.Title = "Balanced Auction (Rotational)";
        interp.Tone = "neutral";
        interp.Action = "STAND ASIDE";
        interp.Details = "Two-sided responsive auction with no clean directional edge. Await clear initiative or absorption.";
        return interp;
    }
}
