using ReactiveUI;

namespace QQQTrader.Model;

public class AccountInfo : ReactiveObject
{
    private double _availableFunds;

    public double AvailableFunds
    {
        get => _availableFunds;
        set => this.RaiseAndSetIfChanged(ref _availableFunds, value);
    }
}