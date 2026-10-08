using System;
using System.Collections.Generic;
using System.Globalization;

namespace FootPrintTrader.Helper;

public static class FootprintAnalyzer
{
    public const decimal MinSignalVolume = 200m;
    public const decimal MinAbsorptionDelta = 50m;

    private const string ToneUp = "up";
    private const string ToneDown = "down";

    public static List<FootprintRenderLine> Analyze(FootprintSnapshot snapshot, double tickSize)
    {
        var lines = new List<FootprintRenderLine>();
        var bar = snapshot?.Current;
        if (bar == null || bar.Volume < MinSignalVolume) return lines;

        var delta = bar.Delta;
        var change = bar.Close - bar.Open;
        if (Math.Abs(delta) < MinAbsorptionDelta || Math.Abs(change) < 1e-9) return lines;
        var dec = Decimals(tickSize);

        if (delta > 0 && change < 0)
            lines.Add(new FootprintRenderLine
            {
                Text = $"absorption +Δ {FmtVol(delta)} but price {Signed(change, dec)} → sellers absorbing",
                Tone = ToneDown
            });
        else if (delta < 0 && change > 0)
            lines.Add(new FootprintRenderLine
            {
                Text = $"absorption −Δ {FmtVol(-delta)} but price {Signed(change, dec)} → buyers absorbing",
                Tone = ToneUp
            });

        return lines;
    }

    private static string FmtVol(decimal v) =>
        Math.Abs(v) >= 1000
            ? (v / 1000m).ToString("0.0", CultureInfo.InvariantCulture) + "k"
            : v.ToString(CultureInfo.InvariantCulture);

    private static string Signed(double v, int dec) =>
        (v >= 0 ? "+" : "-") + Math.Abs(v).ToString("F" + dec, CultureInfo.InvariantCulture);

    private static int Decimals(double tickSize)
    {
        var s = tickSize.ToString(CultureInfo.InvariantCulture);
        var i = s.IndexOf('.');
        return i < 0 ? 0 : s.Length - i - 1;
    }
}
