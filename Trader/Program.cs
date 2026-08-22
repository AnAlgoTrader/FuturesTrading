using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using Trader.Models;

namespace Trader;

internal class Program
{
    private static PriceUpdate _lastPrice;

    private static async Task Main()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "trader.log");
        using var logger = new Logger(logPath);

        using var client = new IbClient();
        client.OnNotification += msg =>
        {
            Console.WriteLine(msg);
            logger.WriteLine($"[IB] {msg}");
        };

        var aggregator = new CandleAggregator();
        var detector = new SignalDetector(windowSize: 1000, entryThreshold: 2.0);
        var store = new OrderStore(Path.Combine(AppContext.BaseDirectory, "order_state.json"));
        var manager = new OrderManager(client, store);

        client.Start("127.0.0.1", 4001, 0);

        var contractDataList = await client.GetContractData("MES", "FUT");
        var contractDetails = contractDataList
            .OrderBy(x => x.Contract.LastTradeDate)
            .FirstOrDefault(x =>
                DateTime.ParseExact(x.Contract.LastTradeDate, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture) > DateTime.Now);

        if (contractDetails == null)
        {
            client.NotifySubscriber("No valid MES contract found.");
            return;
        }

        client.NotifySubscriber($"Using contract - Exchange:{contractDetails.Contract.Exchange} LastDate:{contractDetails.Contract.LastTradeDate}");

        aggregator.OnCandleClosed += candle =>
        {
            client.NotifySubscriber(candle.ToString());
            var signal = detector.OnCandleClosed(candle);
            if (signal != null)
            {
                client.NotifySubscriber(signal.ToString());
                manager.OnSignal(signal, contractDetails.Contract, _lastPrice);
            }
        };

        client.OnMarketDataPriceUpdate += marketDataUpdate =>
        {
            var update = marketDataUpdate.First();
            if (update.Value.BidPrice <= 0 || update.Value.AskPrice <= 0)
                return;
            _lastPrice = update.Value;
            
            aggregator.OnTick(DateTime.Now, _lastPrice);
        };

        client.OnOrderStatusUpdate += status =>
        {
            client.NotifySubscriber($"OrderStatus - Id:{status.OrderId} Status:{status.Status} Filled:{status.Filled} Avg:{status.AvgFillPrice:F2}");
            manager.OnOrderStatus(status);
        };

        client.SubscribeToMarketData(1005, contractDetails);

        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.SetResult();
        };
        client.NotifySubscriber("Listening for signals. Press Ctrl+C to exit.");
        await tcs.Task;
    }
}
