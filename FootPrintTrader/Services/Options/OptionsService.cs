using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FootPrintTrader.Services.Logging;
using FootPrintTrader.UI.Models;
using Ib.Csharp.Api.Client.Implementation.Helper;
using Ib.Csharp.Api.Client.Implementation.Model;

namespace FootPrintTrader.Services.Options;

public class OptionsService
{
    private readonly Logger _logger;
    private int _optionsFetchInProgress;
    private volatile OptionsChainUpdate _lastOptionsChain;

    public OptionsChainUpdate LastOptionsChain => _lastOptionsChain;

    public event Action<OptionsChainUpdate> OnOptionsChainUpdated;
    public event Action<string> OnOptionsError;

    public OptionsService(Logger logger)
    {
        _logger = logger;
    }

    public async Task FetchOptionsChainAsync()
    {
        if (Interlocked.Exchange(ref _optionsFetchInProgress, 1) == 1) return;

        try
        {
            var chain = await CboeHelper.GetTodaySpxwChain();
            if (chain == null)
            {
                const string message = "No SPXW chain found for today.";
                _logger?.WriteLine($"[OPTIONS] {message}");
                OnOptionsError?.Invoke(message);
                return;
            }

            var update = ConvertChain(chain, DateTime.Now);
            _lastOptionsChain = update;
            OnOptionsChainUpdated?.Invoke(update);
            _logger?.WriteLine($"[OPTIONS] {update.Symbol} {update.ExpirationDate} - {update.Rows.Count} strikes @ {update.UnderlyingPrice:F2}");
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[OPTIONS] Fetch failed: {ex}");
            OnOptionsError?.Invoke("Options chain unavailable. Check cboeData.json.");
        }
        finally
        {
            Interlocked.Exchange(ref _optionsFetchInProgress, 0);
        }
    }

    public static OptionsChainUpdate ConvertChain(CboeChain chain, DateTime timestamp)
    {
        var source = chain.Data ?? new List<OptionChainData>();
        var spot = source.Count > 0 ? source[0].UnderlyingPrice : 0;

        return new OptionsChainUpdate
        {
            Symbol = chain.Symbol ?? "",
            ExpirationDate = chain.ExpirationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            UnderlyingPrice = spot,
            UpdatedAt = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Rows = source
                .OrderByDescending(row => row.Strike)
                .Select(row => new OptionsChainRow
                {
                    Strike = row.Strike,
                    CallBid = row.CallBid,
                    CallAsk = row.CallAsk,
                    CallIv = row.CallIv,
                    CallDelta = row.CallDelta,
                    CallGamma = row.CallGamma,
                    CallOpenInterest = row.CallOpenInterest,
                    PutBid = row.PutBid,
                    PutAsk = row.PutAsk,
                    PutIv = row.PutIv,
                    PutDelta = row.PutDelta,
                    PutGamma = row.PutGamma,
                    PutOpenInterest = row.PutOpenInterest,
                    Gex = ComputeGex(row, spot)
                })
                .ToList()
        };
    }

    public static double ComputeGex(OptionChainData row, double spot)
    {
        if (spot <= 0) return 0;

        const double contractMultiplier = 100.0;
        const double onePercent = 0.01;

        return (row.CallGamma * row.CallOpenInterest - row.PutGamma * row.PutOpenInterest)
               * contractMultiplier * spot * spot * onePercent;
    }
}
