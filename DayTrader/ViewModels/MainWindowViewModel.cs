using System;
using System.Threading.Tasks;
using DayTrader.Helper;
using Ib.Csharp.Api.Client.Implementation;

namespace DayTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IbClient _client = new();
    
    public MainWindowViewModel()
    {
        _ = InitAndSubscribe();
    }

    private async Task InitAndSubscribe()
    {
        _client.OnNotification += Console.WriteLine;
        _client.Start("127.0.0.1", 4001, 0);
        
        var esFutures = await ContractHelper.GetFrontMonth(_client, "ES");
        _client.NotifySubscriber($"ES contract - Exchange:{esFutures.Contract.Exchange} LastDate:{esFutures.Contract.LastTradeDate}");
    }
}