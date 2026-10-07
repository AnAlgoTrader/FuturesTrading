using System;
using System.Collections.Generic;
using System.Linq;

namespace IndicatorBuild.Analytics;

/// <summary>
/// M9 holdout validation. The engine is tuned only on the earlier bars; this class compares the
/// same signal families' forward-10-bar win% on the in-sample region vs a reserved, never-tuned
/// tail (holdout). If an edge survives, it is far more believable than a single-CSV number.
/// Win-rates are optimistic in-sample estimates — small holdout N must be read as anecdote, so
/// rows carry their sample sizes.
/// </summary>
public sealed class OutcomeHoldoutRow
{
    public string Label { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Direction { get; set; } = "";
    public int InCount { get; set; }
    public double? InWin { get; set; }
    public int HoldoutCount { get; set; }
    public double? HoldoutWin { get; set; }
    /// <summary>Qualified (colored) subset of the holdout — what a user would actually trade.</summary>
    public int GatedCount { get; set; }
    public double? GatedWin { get; set; }

    public string ToTable() =>
        $"  {Label,-12}{(Detail == "" ? "" : "/" + Detail),-13}{Direction,-9}"
        + $"in N={InCount,-6}win={(Fmt(InWin)),-4}hold N={HoldoutCount,-6}win={Fmt(HoldoutWin),-4}"
        + $"gated N={GatedCount,-5}win={Fmt(GatedWin),-4}";

    private static string Fmt(double? v) => v is null ? "-" : ((int)v.Value).ToString();
}

public static class OutcomeHoldout
{
    private static string Key(OutcomeRow r) => r.Label + "\u0001" + r.Detail + "\u0001" + r.Direction;

    /// <summary>Merges in-sample / holdout / gated-holdout row sets by (label, detail, direction).</summary>
    public static List<OutcomeHoldoutRow> Build(
        IReadOnlyList<OutcomeRow> inSample,
        IReadOnlyList<OutcomeRow> holdout,
        IReadOnlyList<OutcomeRow> holdoutGated)
    {
        var map = new Dictionary<string, OutcomeHoldoutRow>();

        // each slice fills exactly its own columns; later fills never clobber earlier ones
        void Add(OutcomeRow r, bool isInSample, bool isHoldout, bool isGated)
        {
            var k = Key(r);
            if (!map.TryGetValue(k, out var row))
                map[k] = row = new OutcomeHoldoutRow { Label = r.Label, Detail = r.Detail, Direction = r.Direction };

            if (isInSample) { row.InCount = r.Count; row.InWin = r.WinRate; }
            if (isHoldout) { row.HoldoutCount = r.Count; row.HoldoutWin = r.WinRate; }
            if (isGated) { row.GatedCount = r.Count; row.GatedWin = r.WinRate; }
        }

        foreach (var r in inSample) Add(r, isInSample: true, isHoldout: false, isGated: false);
        foreach (var r in holdout) Add(r, isInSample: false, isHoldout: true, isGated: false);
        foreach (var r in holdoutGated) Add(r, isInSample: false, isHoldout: false, isGated: true);

        return map.Values.OrderBy(r => r.Label).ThenBy(r => r.Detail).ThenBy(r => r.Direction).ToList();
    }
}