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

            interp.Details = range < MinViableRangePoints
                ? $"Opposing flow: resistance at {P(bar.High)}, support at {P(bar.Low)}. Range is compressed ({range.ToString("F" + dec, CultureInfo.InvariantCulture)} pts)."
                : $"Opposing flow: resistance at {P(bar.High)}, support at {P(bar.Low)}. Wait for breakout.";
            return interp;
        }

        // 2. Compressed Range Filter: Even if one bias exists, narrow range offers poor R:R
        if (range < tickSize * MinTicksForTrade && context.Bias != "Rotational / Balance")
        {
            interp.IsCompressedRange = true;
            interp.Title = $"{context.Bias} (Range Compressed)";
            interp.Tone = "warning";
            interp.Action = "STAND ASIDE";
            interp.Details = $"Bar range is only {range.ToString("F" + dec, CultureInfo.InvariantCulture)} pts. Poor risk/reward inside the bar.";
            return interp;
        }

        // 3. Trapped Traders
        var trappedSignal = signals.FirstOrDefault(s => s.Type == "trapped_traders");
        if (trappedSignal != null)
        {
            if (trappedSignal.Tone == "bearish")
            {
                interp.Title = "Trapped Buyers";
                interp.Tone = "bearish";
                interp.Action = $"SHORT (Fade @ {P(context.PocPrice)})";
                interp.Details = $"Longs trapped above {P(context.PocPrice)}. Look for short continuation.";
            }
            else
            {
                interp.Title = "Trapped Sellers";
                interp.Tone = "bullish";
                interp.Action = $"BUY (Squeeze @ {P(context.PocPrice)})";
                interp.Details = $"Shorts trapped below {P(context.PocPrice)}. Look for squeeze upward.";
            }
            return interp;
        }

        // 4. Institutional Absorption
        if (hasCeilingAbsorption && !hasBullishSignal)
        {
            interp.Title = "Institutional Seller Absorption";
            interp.Tone = "bearish";
            interp.Action = $"SHORT (Fade @ {P(bar.High)})";
            interp.Details = $"Heavy buying absorbed at {P(bar.High)}. Seller defending the high.";
            return interp;
        }

        var hasFloorAbsorption = signals.Any(s => s.Type == "absorption" && s.Tone == "bullish");
        if (hasFloorAbsorption && !hasBearishSignal)
        {
            interp.Title = "Institutional Buyer Absorption";
            interp.Tone = "bullish";
            interp.Action = $"BUY (Squeeze @ {P(bar.Low)})";
            interp.Details = $"Heavy selling absorbed at {P(bar.Low)}. Buyer defending the low.";
            return interp;
        }

        // 5. Delta Divergence
        if (context.DeltaDivergence == "Bearish" && !hasBullishSignal)
        {
            interp.Title = "Bearish Delta Divergence";
            interp.Tone = "bearish";
            interp.Action = $"SHORT (Divergence @ {P(bar.High)})";
            interp.Details = $"New high with negative delta. Buyers exhausted at the top.";
            return interp;
        }

        if (context.DeltaDivergence == "Bullish" && !hasBearishSignal)
        {
            interp.Title = "Bullish Delta Divergence";
            interp.Tone = "bullish";
            interp.Action = $"BUY (Divergence @ {P(bar.Low)})";
            interp.Details = $"New low with positive delta. Sellers exhausted at the bottom.";
            return interp;
        }

        // 6. Stacked Imbalance / Initiative Drive
        var initiativeBuy = signals.FirstOrDefault(s => s.Type == "stacked_imbalance" && s.Tone == "bullish");
        if (initiativeBuy != null && !hasBearishSignal)
        {
            interp.Title = "Bullish Initiative Drive";
            interp.Tone = "bullish";
            interp.Action = $"BUY (Retest @ {P(context.PocPrice)})";
            interp.Details = $"Aggressive buyers in control. Buy pullbacks to {P(context.PocPrice)}.";
            return interp;
        }

        var initiativeSell = signals.FirstOrDefault(s => s.Type == "stacked_imbalance" && s.Tone == "bearish");
        if (initiativeSell != null && !hasBullishSignal)
        {
            interp.Title = "Bearish Initiative Drive";
            interp.Tone = "bearish";
            interp.Action = $"SHORT (Retest @ {P(context.PocPrice)})";
            interp.Details = $"Aggressive sellers in control. Sell rallies to {P(context.PocPrice)}.";
            return interp;
        }

        // 7. Default Balanced Rotation (No actionable interpretation)
        interp.Title = "";
        interp.Tone = "neutral";
        interp.Action = "";
        interp.Details = "";
        return interp;
    }
}
