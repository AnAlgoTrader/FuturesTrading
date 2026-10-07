using System;
using System.Collections.Generic;
using IndicatorBuild.Model;

namespace IndicatorBuild.Indicators;

public class BarContext
{
    public Candle Candle { get; }
    public double? Atr { get; }
    public double? AvgVolume { get; }
    public IReadOnlyList<SwingPoint> NewSwings { get; }

    public BarContext(Candle candle, double? atr = null, double? avgVolume = null,
        IReadOnlyList<SwingPoint>? newSwings = null)
    {
        Candle = candle;
        Atr = atr;
        AvgVolume = avgVolume;
        NewSwings = newSwings ?? Array.Empty<SwingPoint>();
    }
}