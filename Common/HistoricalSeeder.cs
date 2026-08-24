using System;
using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using IBApi;

namespace Common;

public static class HistoricalSeeder
{
    private const int RequestId = 1200;
    private const string Duration = "1 D";
    private const string BarSize = "1 min";
    private const int TimeoutMs = 20000;

    public static async Task SeedFromHistory(IbClient client, Contract contract, SignalDetector detector)
    {
        try
        {
            var bars = await client.GetHistoricalData(RequestId, contract, Duration, BarSize, "TRADES", TimeoutMs, false);
            var closes = bars.Where(b => b.Close > 0).Select(b => b.Close).ToList();
            detector.Seed(closes);
            client.NotifySubscriber(
                $"Warmup seeded - {closes.Count} historical bars | detector {(detector.Ready ? "READY" : "still warming up on live bars")}");
        }
        catch (Exception e)
        {
            client.NotifySubscriber($"Historical warmup failed - {e.Message} | detector needs 60 live bars before signaling.");
        }
    }
}
