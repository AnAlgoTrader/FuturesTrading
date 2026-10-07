namespace Indicator;

/// <summary>
/// A confirmed, not-yet-broken swing point (spec module C).
/// </summary>
public readonly record struct SwingPoint(long Time, double Price, bool IsHigh);
