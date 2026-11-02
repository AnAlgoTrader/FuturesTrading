using System.Collections.Generic;

namespace FootPrintTrader.UI.Models;

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
