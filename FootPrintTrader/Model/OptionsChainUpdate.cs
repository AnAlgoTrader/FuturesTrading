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

        return new OptionsChainUpdate
        {
            Symbol = chain.Symbol ?? "",
            ExpirationDate = chain.ExpirationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            UnderlyingPrice = source.Count > 0 ? source[0].UnderlyingPrice : 0,
            UpdatedAt = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Rows = source
                .OrderBy(row => row.Strike)
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
                    PutOpenInterest = row.PutOpenInterest
                })
                .ToList()
        };
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
}

public class OptionsErrorUpdate
{
    public string Message { get; set; } = "";
}
