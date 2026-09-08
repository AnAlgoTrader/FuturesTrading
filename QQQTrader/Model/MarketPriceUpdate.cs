using ReactiveUI;

namespace QQQTrader.Model;

public class MarketPriceUpdate : ReactiveObject
{
    private double _bid;
    
    public double Bid
    {
        get => _bid;
        set => this.RaiseAndSetIfChanged(ref _bid, value);
    }
    
    private double _ask;
    
    public double Ask
    {
        get => _ask;
        set
        {
            this.RaiseAndSetIfChanged(ref _ask, value);
            this.RaisePropertyChanged(nameof(Spread));
        }
    }
    
    public double Spread => Ask - Bid;
}