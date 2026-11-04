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

/// <summary>
/// Command sent by the options window when a spread is staged or cleared.
/// The window owns the staging state; this tells the host which contracts to stream.
/// </summary>
public class OptionsSpreadCommand
{
    public string Action { get; set; } = "";        // "stageSpread"
    public string Strategy { get; set; } = "";      // "BULL PUT" or "BEAR CALL"
    public double ShortStrike { get; set; }
    public double LongStrike { get; set; }
    public string ShortRight { get; set; } = "";    // "P" or "C"
    public string LongRight { get; set; } = "";     // "P" or "C"
}

/// <summary>
/// Live bid/ask for the two legs of the currently staged spread.
/// A zero value means that side has not been received yet.
/// </summary>
public class OptionsSpreadQuoteUpdate
{
    public string Strategy { get; set; } = "";
    public double ShortStrike { get; set; }
    public double LongStrike { get; set; }
    public double ShortBid { get; set; }
    public double ShortAsk { get; set; }
    public double LongBid { get; set; }
    public double LongAsk { get; set; }
    public string UpdatedAt { get; set; } = "";
}

/// <summary>
/// Subscription lifecycle for the staged spread: Subscribed, Live, Delayed, Cleared, Error.
/// </summary>
public class OptionsSpreadStatusUpdate
{
    public string State { get; set; } = "";
    public string Message { get; set; } = "";
    public string Strategy { get; set; } = "";
    public double ShortStrike { get; set; }
    public double LongStrike { get; set; }
}
