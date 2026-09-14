using System;
using System.Collections.Generic;
using ReactiveUI;

namespace DayTrader.Models;

public class SidePanelInfo : ReactiveObject
{
    private double _vix;

    public double Vix
    {
        get => _vix;
        set => this.RaiseAndSetIfChanged(ref _vix, value);
    }

    private double _vvix;

    public double Vvix
    {
        get => _vvix;
        set => this.RaiseAndSetIfChanged(ref _vvix, value);
    }

    private double _ivPercentile;

    public double IvPercentile
    {
        get => _ivPercentile;
        set => this.RaiseAndSetIfChanged(ref _ivPercentile, value);
    }

    private double _ivRank;

    public double IvRank
    {
        get => _ivRank;
        set => this.RaiseAndSetIfChanged(ref _ivRank, value);
    }

    private double _rv5;

    public double Rv5
    {
        get => _rv5;
        set => this.RaiseAndSetIfChanged(ref _rv5, value);
    }

    private double _vrp;

    public double Vrp
    {
        get => _vrp;
        set => this.RaiseAndSetIfChanged(ref _vrp, value);
    }

    private double _vrpZScore;

    public double VrpZScore
    {
        get => _vrpZScore;
        set => this.RaiseAndSetIfChanged(ref _vrpZScore, value);
    }

    private double _vvixPercentile;

    public double VvixPercentile
    {
        get => _vvixPercentile;
        set => this.RaiseAndSetIfChanged(ref _vvixPercentile, value);
    }

    private string _analysis = "";

    public string Analysis
    {
        get => _analysis;
        set => this.RaiseAndSetIfChanged(ref _analysis, value);
    }

    public string BuildAnalysis()
    {
        if (Vix <= 0)
            return "Awaiting market regime data...";

        var lines = new List<string>();

        lines.Add(Vix switch
        {
            < 14 => $"VIX {Vix:F1} - calm, low-vol regime.",
            < 20 => $"VIX {Vix:F1} - normal risk appetite.",
            < 30 => $"VIX {Vix:F1} - elevated stress regime.",
            _ => $"VIX {Vix:F1} - high-fear regime."
        });

        lines.Add(Vvix switch
        {
            < 90 => $"VVIX {Vvix:F1} - smooth, mean-reverting vol-of-vol.",
            < 110 => $"VVIX {Vvix:F1} - normal vol-of-vol.",
            _ => $"VVIX {Vvix:F1} - jumpy vol-of-vol; tail risk elevated."
        });

        lines.Add(IvPercentile switch
        {
            < 25 => $"IV percentile {IvPercentile:F0} - cheap vs history; favors buying premium.",
            < 70 => $"IV percentile {IvPercentile:F0} - fair vs history.",
            _ => $"IV percentile {IvPercentile:F0} - rich vs history; favors selling premium."
        });

        lines.Add($"IV rank {IvRank:F0} within 1y range (VIX {Vix:F1}).");

        lines.Add(Vrp switch
        {
            > 0 => $"VRP {Vrp:F1} - implied above realized; premium rich.",
            < 0 => $"VRP {Vrp:F1} - implied below realized; premium cheap.",
            _ => $"VRP {Vrp:F1} - implied in line with realized."
        });

        if (Rv5 > 0)
            lines.Add($"5d realized vol {Rv5:F1}%.");

        lines.Add(VrpZScore switch
        {
            >= 2 => $"VRP z-score {VrpZScore:F1} - unusually rich premium.",
            <= -2 => $"VRP z-score {VrpZScore:F1} - unusually cheap premium.",
            _ => $"VRP z-score {VrpZScore:F1} - within normal bounds."
        });

        lines.Add(VvixPercentile switch
        {
            < 30 => $"VVIX percentile {VvixPercentile:F0} - low vol-of-vol regime.",
            < 70 => $"VVIX percentile {VvixPercentile:F0} - neutral vol-of-vol regime.",
            _ => $"VVIX percentile {VvixPercentile:F0} - high vol-of-vol regime."
        });

        return string.Join(Environment.NewLine, lines);
    }
}