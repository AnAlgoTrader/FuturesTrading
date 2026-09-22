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
    
    private DateTime _futuresExpiration;

    public DateTime FuturesExpiration
    {
        get => _futuresExpiration;
        set => this.RaiseAndSetIfChanged(ref _futuresExpiration, value);
    }

    public string BuildAnalysis()
    {
        if (Vix <= 0)
            return "Awaiting market regime data...";

        var lines = new List<string>
        {
            Vix switch
            {
                < 14 => $"VIX {Vix:F1} - calm tape, low expected range; trending setups favored.",
                < 20 => $"VIX {Vix:F1} - normal conditions, volatility within typical bounds.",
                < 30 => $"VIX {Vix:F1} - elevated; expect wider ranges, faster moves, wider stops.",
                _ => $"VIX {Vix:F1} - stressed market; wide swings, whipsaw risk."
            },
            Vvix switch
            {
                < 90 => $"VVIX {Vvix:F1} - smooth vol-of-vol; regime shifts unlikely.",
                < 110 => $"VVIX {Vvix:F1} - normal vol-of-vol.",
                _ => $"VVIX {Vvix:F1} - jumpy; volatility itself unstable, regime changes likely."
            },
            IvPercentile switch
            {
                < 25 => $"IV at {IvPercentile:F0}th pct - calm, near the bottom of its range.",
                < 70 => $"IV at {IvPercentile:F0}th pct - within normal bounds.",
                _ => $"IV at {IvPercentile:F0}th pct - elevated; conditions clearly outside normal."
            },
            $"IV rank {IvRank:F0} vs the 1y band (VIX {Vix:F1}).",
            Rv5 switch
            {
                <= 0 => "5d realized vol: n/a.",
                < 10 => $"5d realized vol {Rv5:F1}% - quiet, trending-friendly tape.",
                < 20 => $"5d realized vol {Rv5:F1}% - recent movement within normal bounds.",
                _ => $"5d realized vol {Rv5:F1}% - recent chop above normal; respect wider stops."
            },
            Vrp switch
            {
                > 3 => $"VRP {Vrp:F1} - market prices more risk than realized; watch for repricing toward reality.",
                < -3 => $"VRP {Vrp:F1} - realized has outpaced implied; the market was caught off guard.",
                _ => $"VRP {Vrp:F1} - implied and realized aligned; no repricing pressure."
            },
            VrpZScore switch
            {
                >= 2 => $"VRP z {VrpZScore:F1} - extreme; a dislocation that tends to snap back.",
                <= -2 => $"VRP z {VrpZScore:F1} - extreme; unwind risk is high.",
                _ => $"VRP z {VrpZScore:F1} - normal."
            },
            VvixPercentile switch
            {
                < 30 => $"VVIX pct {VvixPercentile:F0} - calm vol environment.",
                < 70 => $"VVIX pct {VvixPercentile:F0} - neutral.",
                _ => $"VVIX pct {VvixPercentile:F0} - volatile vol; conditions abnormal."
            }
        };

        var abnormal = Vix >= 30 || IvPercentile >= 85 || VvixPercentile >= 85;
        var calm = Vix < 20 && IvPercentile < 70 && VvixPercentile < 70 && Rv5 is > 0 and < 20;
        lines.Add(abnormal
            ? "Verdict: ABNORMAL conditions - stay defensive."
            : calm
                ? "Verdict: calm tape, vol within normal bounds - directional setups OK."
                : "Verdict: normal conditions.");

        return string.Join(Environment.NewLine, lines);
    }
}