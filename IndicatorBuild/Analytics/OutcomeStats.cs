using System;
using System.Collections.Generic;
using System.Linq;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;

namespace IndicatorBuild.Analytics;

public sealed class OutcomeRow
{
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public int Count { get; init; }
    public string Direction { get; init; } = "";
    public double? WinRate { get; init; }
    public double? AvgMoveAtr { get; init; }
    public double? MedianMoveAtr { get; init; }
    public double Hit1AtrRate { get; init; }
    public double Fail1AtrRate { get; init; }

    public override string ToString()
    {
        var avg = AvgMoveAtr is null ? "-" : AvgMoveAtr.Value.ToString("0.00");
        var med = MedianMoveAtr is null ? "-" : MedianMoveAtr.Value.ToString("0.00");
        var wr = WinRate is null ? "-" : ((int)WinRate.Value).ToString();
        return $"  {Label,-12}{(Detail == "" ? "" : "/" + Detail),-13}{Direction,-9}N={Count,-5}"
            + $"win={wr,-5}avg={avg,-7}med={med,-7}hit1={Hit1AtrRate:0}% fail1={Fail1AtrRate:0}%";
    }
}

public static class OutcomeStats
{
    /// <summary>
    /// Forward-looking quality heuristics per signal type (backtest-only). "Support" is
    /// antisymmetric: a judgement-free price change in the predicted direction.
    /// </summary>
    public static List<OutcomeRow> Collect(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<ConceptEvent> events,
        int forwardBars = IndicatorConfig.OutcomeForwardBars,
        double minWinAtr = IndicatorConfig.OutcomeMinWinAtr,
        double hitAtr = IndicatorConfig.OutcomeHitAtr,
        System.Func<ConceptEvent, bool>? include = null)
    {
        var timeToIndex = new Dictionary<long, int>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
            timeToIndex[candles[i].Time] = i;

        var atr = new AtrCalculator(IndicatorConfig.AtrPeriod);
        var atrAt = new double[candles.Count];
        for (var i = 0; i < candles.Count; i++)
            atrAt[i] = atr.Next(candles[i]) ?? candles[i].High - candles[i].Low;

        var buckets = new Dictionary<string, List<(double Move, bool Hit, bool Fail)>>();

        foreach (var evt in events)
        {
            if (include is not null && !include(evt)) continue;
            var dir = ExpectedDirection(evt.Type, evt.Detail) * (evt.Direction == "Bearish" ? -1 : 1);
            if (dir == 0) continue;
            if (!timeToIndex.TryGetValue(evt.Time, out var idx)) continue;
            if (idx + forwardBars >= candles.Count) continue;

            var start = candles[idx].Close;
            var scale = Math.Max(1e-9, atrAt[idx]);
            var end = candles[idx + forwardBars].Close;

            var hit = false;
            var fail = false;
            for (var k = 1; k <= forwardBars; k++)
            {
                if (dir > 0)
                {
                    if (candles[idx + k].High - start >= hitAtr * scale) hit = true;
                    if (start - candles[idx + k].Low >= hitAtr * scale) fail = true;
                }
                else
                {
                    if (start - candles[idx + k].Low >= hitAtr * scale) hit = true;
                    if (candles[idx + k].High - start >= hitAtr * scale) fail = true;
                }
            }

            var move = dir * (end - start) / scale;
            var key = BucketKey(evt, out var label, out var detail);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<(double, bool, bool)>();
                buckets[key] = list;
            }
            list.Add((move, hit, fail));
        }

        var rows = new List<OutcomeRow>();
        foreach (var kv in buckets)
        {
            var parts = kv.Key.Split('\u0001');
            var moves = kv.Value.Select(v => v.Move).OrderBy(m => m).ToList();
            var n = moves.Count;
            var median = n % 2 == 1 ? moves[n / 2] : (moves[n / 2 - 1] + moves[n / 2]) / 2.0;
            var winRate = n == 0 ? (double?)null : 100.0 * moves.Count(m => m >= minWinAtr) / n;
            var avg = moves.Count == 0 ? (double?)null : moves.Average();
            rows.Add(new OutcomeRow
            {
                Label = parts[0],
                Detail = parts[1],
                Direction = kv.Key.EndsWith("\u0001Bullish") ? "Bullish" : "Bearish",
                Count = n,
                WinRate = winRate,
                AvgMoveAtr = avg,
                MedianMoveAtr = median,
                Hit1AtrRate = 100.0 * kv.Value.Count(v => v.Hit) / n,
                Fail1AtrRate = 100.0 * kv.Value.Count(v => v.Fail) / n
            });
        }

        return rows.OrderBy(r => r.Label).ToList();
    }

    private static int ExpectedDirection(Concept type, string detail)
    {
        switch (type)
        {
            case Concept.ChoCh:
            case Concept.Exhaustion:
            case Concept.Pullback:
            case Concept.SignOfStrength:
            case Concept.SignOfWeakness:
            case Concept.AccumulationBreakout:
            case Concept.DistributionBreakout:
            case Concept.Expansion:
                return 1;
            case Concept.LiquiditySweep:
                return detail == "Confirmed" ? 1 : 0;
            case Concept.Breakout:
                return detail == "" ? 1 : 0;
            case Concept.Divergence:
                return 1;
            default:
                return 0;
        }
    }

    private static string BucketKey(ConceptEvent evt, out string label, out string detail)
    {
        switch (evt.Type)
        {
            case Concept.ChoCh: label = "CHoCH"; detail = ""; break;
            case Concept.LiquiditySweep when evt.Detail == "Confirmed": label = "Sweep"; detail = "Confirmed"; break;
            case Concept.Breakout when evt.Detail == "": label = "Breakout"; detail = ""; break;
            case Concept.Pullback: label = "Pullback"; detail = ""; break;
            case Concept.Exhaustion: label = "Exhaustion"; detail = evt.Detail; break;
            case Concept.Divergence: label = "Divergence"; detail = evt.Detail; break;
            case Concept.SignOfStrength: label = "SOS"; detail = ""; break;
            case Concept.SignOfWeakness: label = "SOW"; detail = ""; break;
            case Concept.AccumulationBreakout: label = "Wyckoff"; detail = "AccumOut"; break;
            case Concept.DistributionBreakout: label = "Wyckoff"; detail = "DistOut"; break;
            case Concept.Expansion: label = "Expansion"; detail = evt.Detail; break;
            default: label = evt.Type.ToString(); detail = evt.Detail; break;
        }

        return label + "\u0001" + detail + "\u0001" + (evt.Direction == "Bearish" ? "Bearish" : "Bullish");
    }
}

public sealed class OutcomeComparison
{
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Direction { get; init; } = "";
    public int AllCount { get; init; }
    public int GatedCount { get; init; }
    public double? AllWin { get; init; }
    public double? GatedWin { get; init; }

    public string ToTable()
    {
        var delta = AllWin is null || GatedWin is null ? "  -" : (GatedWin - AllWin).Value.ToString("+0;-0;0");
        var allW = AllWin is null ? "-" : ((int)AllWin.Value).ToString();
        var gatedW = GatedWin is null ? "-" : ((int)GatedWin.Value).ToString();
        return $"  {Label,-12}{(Detail == "" ? "" : "/" + Detail),-13}{Direction,-9}"
            + $"all N={AllCount,-5}win={allW,-5}gated N={GatedCount,-5}win={gatedW,-5}delta={delta,-4}";
    }
}

public static class OutcomeCompare
{
    public static List<OutcomeComparison> Build(
        IReadOnlyList<OutcomeRow> all,
        IReadOnlyList<OutcomeRow> gated)
    {
        var gatedByKey = gated.ToDictionary(
            r => r.Label + "\u0001" + r.Detail + "\u0001" + r.Direction);

        return all.Select(r =>
        {
            var key = r.Label + "\u0001" + r.Detail + "\u0001" + r.Direction;
            gatedByKey.TryGetValue(key, out var g);
            return new OutcomeComparison
            {
                Label = r.Label,
                Detail = r.Detail,
                Direction = r.Direction,
                AllCount = r.Count,
                AllWin = r.WinRate,
                GatedCount = g?.Count ?? 0,
                GatedWin = g?.WinRate
            };
        }).ToList();
    }
}