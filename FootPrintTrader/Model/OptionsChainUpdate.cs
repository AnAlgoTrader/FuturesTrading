using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ib.Csharp.Api.Client.Implementation.Model;

namespace FootPrintTrader.Model;

/// <summary>
/// UI-ready snapshot of the SPXW option chain. Keeps only the fields the
/// options table renders, so no IB contract objects are serialized.
/// </summary>
public class OptionsChainUpdate
{
    public string Symbol { get; set; } = "";
    public string ExpirationDate { get; set; } = "";
    public double UnderlyingPrice { get; set; }
    public string UpdatedAt { get; set; } = "";
    public List<OptionsChainRow> Rows { get; set; } = new();

    public static OptionsChainUpdate From(CboeChain chain, DateTime timestamp)
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

    /// <summary>
    /// Dealer gamma exposure per 1% move at this strike:
    /// (call gamma * call OI - put gamma * put OI) * 100 * spot² * 0.01.
    /// Assumes dealers are long call gamma and short put gamma.
    /// </summary>
    private static double ComputeGex(OptionChainData row, double spot)
    {
        if (spot <= 0) return 0;

        const double contractMultiplier = 100.0;
        const double onePercent = 0.01;

        return (row.CallGamma * row.CallOpenInterest - row.PutGamma * row.PutOpenInterest)
               * contractMultiplier * spot * spot * onePercent;
    }
}

public class OptionsChainRow
{
    public double Strike { get; set; }

    public double CallBid { get; set; }
    public double CallAsk { get; set; }
    public double CallIv { get; set; }
    public double CallDelta { get; set; }
    public double CallGamma { get; set; }
    public double CallOpenInterest { get; set; }

    public double PutBid { get; set; }
    public double PutAsk { get; set; }
    public double PutIv { get; set; }
    public double PutDelta { get; set; }
    public double PutGamma { get; set; }
    public double PutOpenInterest { get; set; }

    /// <summary>Gamma exposure per 1% underlying move, in dollars.</summary>
    public double Gex { get; set; }
}

public class OptionsErrorUpdate
{
    public string Message { get; set; } = "";
}
