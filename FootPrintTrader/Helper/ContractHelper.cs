using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FootPrintTrader.Model;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;

namespace FootPrintTrader.Helper;

public class ContractHelper
{
    public static async Task<ContractDetails> GetContract(IbClient client, MarketProfile market, string symbol)
    {
        var contractDataList = await client.GetContractData(new Contract
        {
            Symbol = symbol,
            SecType = market.SecurityType,
            Currency = market.Currency,
            Exchange = market.Exchange,
            Multiplier = string.Equals(symbol, market.TradeSymbol, StringComparison.OrdinalIgnoreCase) ? market.TradeMultiplier : market.DataMultiplier
        });
        return contractDataList.OrderBy(x => x.Contract.LastTradeDate).FirstOrDefault(x =>
            DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd", CultureInfo.InvariantCulture) > DateTime.Now)!;
    }
}
