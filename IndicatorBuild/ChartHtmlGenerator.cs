using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IndicatorBuild.Model;

namespace IndicatorBuild;

public static class ChartHtmlGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Build(IReadOnlyList<Candle> candles, string chartLibraryPath)
    {
        if (!File.Exists(chartLibraryPath))
            throw new FileNotFoundException($"Chart library not found: {chartLibraryPath}", chartLibraryPath);

        var library = File.ReadAllText(chartLibraryPath);
        var data = JsonSerializer.Serialize(
            candles.Select(c => new ChartCandle(c.Time, c.Open, c.High, c.Low, c.Close)),
            JsonOptions);

        return Template
            .Replace("__CHART_LIBRARY__", library)
            .Replace("__CANDLE_DATA__", data)
            .Replace("__CANDLE_COUNT__", candles.Count.ToString());
    }

    private record ChartCandle(long Time, double Open, double High, double Low, double Close);

    private const string Template = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
    <title>IndicatorBuild - Candles</title>
    <style>
        html, body { margin: 0; padding: 0; width: 100%; height: 100%; background: #131722; overflow: hidden; }
        #header {
            height: 32px; display: flex; align-items: center; padding: 0 12px;
            background: #1a2332; color: #d1d4dc;
            font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
            font-size: 12px; letter-spacing: 0.03em;
        }
        #chart { width: 100%; height: calc(100% - 32px); }
    </style>
</head>
<body>
<div id="header">historicalData.csv &mdash; __CANDLE_COUNT__ candles</div>
<div id="chart"></div>
<script>__CHART_LIBRARY__</script>
<script>
    const candles = __CANDLE_DATA__;

    const container = document.getElementById('chart');
    const chart = LightweightCharts.createChart(container, {
        width: container.clientWidth,
        height: container.clientHeight,
        layout: { background: { color: '#131722' }, textColor: '#d1d4dc' },
        grid: { vertLines: { color: '#1e222d' }, horzLines: { color: '#1e222d' } },
        rightPriceScale: { borderColor: '#2a2e39' },
        timeScale: { borderColor: '#2a2e39', timeVisible: true, secondsVisible: false },
        crosshair: { mode: LightweightCharts.CrosshairMode.Normal }
    });

    const candleSeries = chart.addSeries(LightweightCharts.CandlestickSeries, {
        upColor: '#26a69a',
        downColor: '#ef5350',
        wickUpColor: '#26a69a',
        wickDownColor: '#ef5350',
        borderVisible: false
    });

    candleSeries.setData(candles);
    chart.timeScale().fitContent();

    window.addEventListener('resize', () => {
        chart.resize(container.clientWidth, container.clientHeight);
    });
</script>
</body>
</html>
""";
}
