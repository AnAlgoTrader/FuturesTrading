using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;

namespace DayTrader.Helper;

public class ContractHelper
{
    public static async Task<ContractDetails> GetQQQContract(IbClient client)
    {
        var contractDataList = await client.GetContractData("QQQ", "STK");
        return contractDataList.OrderBy(x => x.Contract.LastTradeDate).FirstOrDefault(x => x.Contract.Exchange == "SMART" && x.Contract.Currency == "USD")!;
    }
}