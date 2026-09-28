using System;
using ReactiveUI;

namespace DayTrader.Models;

public class AccountInfo : ReactiveObject
{
    private DateTime _updateOn;

    public DateTime UpdateOn
    {
        get => _updateOn;
        set => this.RaiseAndSetIfChanged(ref _updateOn, value);
    }
    
    private double _availableFunds;

    public double AvailableFunds
    {
        get => _availableFunds;
        set => this.RaiseAndSetIfChanged(ref _availableFunds, value);
    }

    private double _cashEuro;

    public double CashEuro
    {
        get => _cashEuro;
        set => this.RaiseAndSetIfChanged(ref _cashEuro, value);
    }

    private double _cashUsd;

    public double CashUsd
    {
        get => _cashUsd;
        set => this.RaiseAndSetIfChanged(ref _cashUsd, value);
    }
    
    private double _buyingPower;

    public double BuyingPower
    {
        get => _buyingPower;
        set => this.RaiseAndSetIfChanged(ref _buyingPower, value);
    }

    private DateTime _futuresExpiration;

    public DateTime FuturesExpiration
    {
        get => _futuresExpiration;
        set => this.RaiseAndSetIfChanged(ref _futuresExpiration, value);
    }
}