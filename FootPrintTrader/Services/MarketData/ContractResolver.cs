using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FootPrintTrader.Configuration;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;

namespace FootPrintTrader.Services.MarketData;

public class ContractResolver
{
    public static async Task<ContractDetails> ResolveContractAsync(IbClient client, MarketProfile market, string symbol)
    {
        var isTrade = string.Equals(symbol, market.TradeSymbol, StringComparison.OrdinalIgnoreCase);
        var multiplier = isTrade ? market.TradeMultiplier : market.DataMultiplier;
        var tradingClass = isTrade ? market.TradeTradingClass : market.DataTradingClass;
        var ibSymbol = market.UnderlyingSymbol ?? symbol;

        var contractDataList = await client.GetContractData(new Contract
        {
            Symbol = ibSymbol,
            SecType = market.SecurityType,
            Currency = market.Currency,
            Exchange = market.Exchange,
            Multiplier = multiplier.ToString(CultureInfo.InvariantCulture),
            TradingClass = tradingClass
        });

        return contractDataList.OrderBy(x => x.Contract.LastTradeDate).FirstOrDefault(x =>
            DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd", CultureInfo.InvariantCulture) > DateTime.Now)!;
    }
}
