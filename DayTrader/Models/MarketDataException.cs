using System;

namespace DayTrader.Models;

public class MarketDataException : Exception
{
    public string DataSource { get; }

    public MarketDataException(string dataSource, string message)
        : base($"{dataSource}: {message}")
    {
        DataSource = dataSource;
    }

    public MarketDataException(string dataSource, string message, Exception inner)
        : base($"{dataSource}: {message}", inner)
    {
        DataSource = dataSource;
    }

    public static double Require(string source, double value, double min, double max)
    {
        if (!double.IsFinite(value))
            throw new MarketDataException(source, $"value is not finite ({value})");
        if (value < min || value > max)
            throw new MarketDataException(source, $"value {value} is outside the plausible range [{min}, {max}]");
        return value;
    }

    public static void RequireCount(string source, int count, int minimum)
    {
        if (count < minimum)
            throw new MarketDataException(source, $"expected at least {minimum} data points, got {count}");
    }
}