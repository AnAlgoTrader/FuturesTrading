using System.Text;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using SupportingData.Helper;

namespace SupportingData;

internal class Program
{
    private static async Task Main()
    {
        await GetLastMonthVolume();
    }

    private static async Task GetLastMonthVolume()
    {
        using var client = new IbClient();
        client.OnNotification += Console.WriteLine;
        client.Start("127.0.0.1", 4001, 0);

        var contractDataList = await client.GetContractData("ES", "FUT");
        var contract = contractDataList.OrderBy(x => x.Contract.LastTradeDate).FirstOrDefault(x =>
            DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture) > DateTime.Now);

        var historicalDataList = await client.GetHistoricalData(1020, contract!.Contract, $"1 {nameof(Duration.M)}", $"1 {nameof(BarSize.min)}", "TRADES", 600000, false);
        
        var output = new StringBuilder();
        const string headers = "Date, Open, High, Low, Close, Volume";
        output.AppendLine(headers);

        foreach (var bar in historicalDataList)
        {
            if (bar.Volume != 0)
                output.AppendLine($"{bar.Time},{bar.Open},{bar.High},{bar.Low},{bar.Close},{bar.Volume}");
        }
        await File.WriteAllTextAsync(SupportingDataHelper.LastMonthVolumeFile, output.ToString());
        Console.WriteLine($"Historical data saved in {SupportingDataHelper.LastMonthVolumeFile}");
    }
}