using System;

namespace Indicator;

/// <summary>
/// Session reference levels (spec module A). Values are null until their source data exists;
/// the <c>*Broken</c> flags mark a level that price has traded through (lines extend until broken).
/// </summary>
public sealed record SessionLevels
{
    public DateOnly SessionDate { get; init; }

    public double? PreviousDayHigh { get; init; }
    public double? PreviousDayLow { get; init; }
    public double? PreviousDayClose { get; init; }

    public double? OvernightHigh { get; init; }
    public double? OvernightLow { get; init; }

    public double? InitialBalanceHigh { get; init; }
    public double? InitialBalanceLow { get; init; }

    public bool PreviousDayHighBroken { get; init; }
    public bool PreviousDayLowBroken { get; init; }
    public bool OvernightHighBroken { get; init; }
    public bool OvernightLowBroken { get; init; }
    public bool InitialBalanceHighBroken { get; init; }
    public bool InitialBalanceLowBroken { get; init; }
}
