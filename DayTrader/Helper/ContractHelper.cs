using System;
using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;

namespace DayTrader.Helper;

public class ContractHelper
{
    public static async Task<ContractDetails> GetSpyContract(IbClient client)
    {
        var contractDataList = await client.GetContractData("SPY", "STK");
        return contractDataList.OrderBy(x => x.Contract.LastTradeDate).FirstOrDefault(x => x.Contract.Exchange == "SMART" && x.Contract.Currency == "USD")!;
    }

    public static async Task<ContractDetails> GetMesContract(IbClient client)
    {
        var contractDataList = await client.GetContractData("MES", "FUT");
        return contractDataList.OrderBy(x=> x.Contract.LastTradeDate).FirstOrDefault(x=> 
            DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) > DateTime.Now)!;
    }
}