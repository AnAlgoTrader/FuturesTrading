using System.Collections.Generic;
using FootPrintTrader.Configuration;

namespace FootPrintTrader.Domain.Footprint;

public enum FootprintSignalType
{
    SellAbsorption,      // SAB - Bullish
    BuyAbsorption,       // BAB - Bearish
    CvdDivergence,       // CDIV - Bearish
    BreakoutConfirmation,// BRK - Bullish
    Slowdown,            // SLW - Phase
    CvdBullishDivergence // CDIV▲ - Bullish (mirror of CDIV)
}

public class DetectedFootprintSignal
{
    public FootprintSignalType Type { get; init; }
    public string Acronym { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tone { get; init; } = "neutral"; // "bullish", "bearish", "phase"
    public bool IsFast { get; init; }
    public long BarStartTime { get; init; }
    public long BarId { get; init; }
}

/// <summary>
/// Signal detection entry point. The detector is intentionally dormant: it produces no
/// signals and exists so the signal logic can be rebuilt here without changing callers.
/// </summary>
public static class FootprintSignalDetector
{
    /// <summary>
    /// Returns an empty map. The bar key is <see cref="FootprintBarData.Id"/> when detection
    /// is re-enabled.
    /// </summary>
    public static Dictionary<long, List<DetectedFootprintSignal>> DetectSignals(
        FootprintBarData[] completed,
        double tickSize,
        SignalSettings settings)
    {
        return new Dictionary<long, List<DetectedFootprintSignal>>();
    }
}
