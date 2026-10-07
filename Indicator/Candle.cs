namespace Indicator;

/// <summary>
/// A single OHLCV bar. <see cref="Time"/> is Unix seconds (UTC).
/// </summary>
public readonly record struct Candle(long Time, double Open, double High, double Low, double Close, double Volume);
