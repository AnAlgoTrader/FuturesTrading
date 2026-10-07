namespace IndicatorBuild.Model;

public record Candle(
    long Time,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume);
