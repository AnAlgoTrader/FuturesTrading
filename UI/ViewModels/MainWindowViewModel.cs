using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;

namespace UI.ViewModels;

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
        _client.OnMarketDataPriceUpdate += HandleMarketUpdate;

        _client.Start("127.0.0.1", 4001, 0);

        var contractDataList = await _client.GetContractData("MES", "FUT");
        var contractDetails = contractDataList.OrderBy(x => x.Contract.LastTradeDate)
            .FirstOrDefault(x => DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) > DateTime.Now);

        _client.SubscribeToMarketData(1005, contractDetails);
    }

    private void HandleMarketUpdate(Dictionary<int, PriceUpdate> marketDataUpdate)
    {
        var update = marketDataUpdate.First();
        if (update.Value.BidPrice <= 0 || update.Value.AskPrice <= 0)
            return;
        Console.WriteLine($"Bid:{update.Value.BidPrice} Ask:{update.Value.AskPrice}");
    }
}