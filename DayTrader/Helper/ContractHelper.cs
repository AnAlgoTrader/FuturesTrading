using System;
using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;

namespace DayTrader.Helper;

public class ContractHelper
{
    private const string Symbol = "MES";

    public static async Task<ContractDetails> GetFrontMonthContract(IbClient client)
    {
        var contractDataList = await client.GetContractData(Symbol, "FUT");
        return contractDataList.OrderBy(x=> x.Contract.LastTradeDate).FirstOrDefault(x=> 
            DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) > DateTime.Now)!;
    }
}