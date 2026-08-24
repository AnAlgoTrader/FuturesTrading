using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace Common;

public static class FuturesContract
{
    public static async Task<ContractDetails> GetFrontMonth(IbClient client, string symbol)
    {
        var contractDataList = await client.GetContractData(symbol, "FUT");
        return contractDataList
            .OrderBy(x => x.Contract.LastTradeDate)
            .FirstOrDefault(x =>
                DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd",
                    CultureInfo.InvariantCulture) > DateTime.Now);
    }
}
