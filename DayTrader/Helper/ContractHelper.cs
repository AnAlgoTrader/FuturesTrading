using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;

namespace DayTrader.Helper;

public class ContractHelper
{
    public static async Task<ContractDetails> GetContract(IbClient client)
    {
        var contractDataList = await client.GetContractData(new Contract
        {
            Symbol = TradingConfig.Symbol,
            SecType = TradingConfig.SecurityType,
            Currency = TradingConfig.Currency,
            Exchange = TradingConfig.Exchange
        });
        return contractDataList.OrderBy(x => x.Contract.LastTradeDate).FirstOrDefault(x =>
            DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd", CultureInfo.InvariantCulture) > DateTime.Now)!;
    }
}