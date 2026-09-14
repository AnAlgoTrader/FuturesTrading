using ReactiveUI;

namespace DayTrader.Models;

public class SidePanelInfo: ReactiveObject
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
}