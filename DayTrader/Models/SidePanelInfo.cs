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
}