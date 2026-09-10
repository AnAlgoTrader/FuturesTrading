using System;
using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;

namespace DayTrader.Helper;

public class ContractHelper
{
    public static async Task<ContractDetails> GetChartContract(IbClient client)
    {
        var contractDataList = await client.GetContractData(new Contract
        {
            Symbol = TradingConfig.ChartSymbol,
            SecType = TradingConfig.ChartSecurityType,
            Currency = TradingConfig.ChartCurrency,
            Exchange = TradingConfig.ChartExchange
        });
        return contractDataList.OrderBy(x => x.Contract.LastTradeDate).FirstOrDefault(x =>
            MatchesCurrency(x.Contract, TradingConfig.ChartCurrency))!;
    }

    public static async Task<ContractDetails> GetTradeContract(IbClient client)
    {
        if (TradingConfig.IsSingleInstrument)
            return await GetChartContract(client);

        var contractDataList = await client.GetContractData(new Contract
        {
            Symbol = TradingConfig.TradeSymbol,
            SecType = TradingConfig.TradeSecurityType,
            Currency = TradingConfig.TradeCurrency,
            Exchange = TradingConfig.TradeExchange
        });
        return contractDataList.OrderBy(x => x.Contract.LastTradeDate).FirstOrDefault(x =>
            DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) > DateTime.Now)!;
    }

    private static bool MatchesCurrency(Contract contract, string currency) =>
        string.Equals(contract.Currency, currency, StringComparison.OrdinalIgnoreCase);
}