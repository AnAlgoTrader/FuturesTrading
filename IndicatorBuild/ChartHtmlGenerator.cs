using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndicatorBuild.Model;
using IndicatorBuild.Rendering;

namespace IndicatorBuild;

public static class ChartHtmlGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Build(ChartModel model, string chartLibraryPath)
    {
        if (!File.Exists(chartLibraryPath))
            throw new FileNotFoundException($"Chart library not found: {chartLibraryPath}", chartLibraryPath);

        var library = File.ReadAllText(chartLibraryPath);
        var candleData = JsonSerializer.Serialize(model.Candles, JsonOptions);
        var vwapData = JsonSerializer.Serialize(model.Vwap, JsonOptions);
        var markerData = JsonSerializer.Serialize(model.Markers, JsonOptions);
        var zoneData = JsonSerializer.Serialize(model.Zones, JsonOptions);

        return Template
            .Replace("__CHART_LIBRARY__", library)
            .Replace("__CANDLE_DATA__", candleData)
            .Replace("__VWAP_DATA__", vwapData)
            .Replace("__MARKER_DATA__", markerData)
            .Replace("__ZONE_DATA__", zoneData)
            .Replace("__CANDLE_COUNT__", model.Candles.Count.ToString());
    }

    private const string Template = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
    <title>IndicatorBuild - Structure Events</title>
    <style>
        html, body { margin: 0; padding: 0; width: 100%; height: 100%; background: #131722; overflow: hidden; }
        #header {
            height: 32px; display: flex; align-items: center; padding: 0 12px;
            background: #1a2332; color: #d1d4dc;
            font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
            font-size: 12px; letter-spacing: 0.03em;
        }
        #chart { width: 100%; height: calc(100% - 32px); position: relative; }
        .zoneBox { position: absolute; pointer-events: none; }
    </style>
</head>
<body>
<div id="header">historicalData.csv &mdash; __CANDLE_COUNT__ candles</div>
<div id="chart"></div>
<script>__CHART_LIBRARY__</script>
<script>
    const candles = __CANDLE_DATA__;
    const vwap = __VWAP_DATA__;
    const markers = __MARKER_DATA__;
    const zones = __ZONE_DATA__;

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
        upColor: '#808080',
        downColor: '#808080',
        wickUpColor: '#808080',
        wickDownColor: '#808080',
        borderVisible: false
    });

    candleSeries.setData(candles);
    candleSeries.setMarkers(markers);

    const vwapSeries = chart.addSeries(LightweightCharts.LineSeries, {
        color: '#FF9900',
        lineWidth: 1,
        priceLineVisible: false,
        crosshairMarkerVisible: false,
        lastValueVisible: false
    });
    vwapSeries.setData(vwap);

    chart.timeScale().fitContent();

    const zoneBoxes = [];
    function renderZones() {
        zoneBoxes.forEach(el => el.remove());
        zoneBoxes.length = 0;
        for (const z of zones) {
            const x1 = chart.timeScale().timeToCoordinate(z.start);
            const x2 = chart.timeScale().timeToCoordinate(z.end);
            const y1 = candleSeries.priceToCoordinate(z.top);
            const y2 = candleSeries.priceToCoordinate(z.bottom);
            if (x1 === null || x2 === null || y1 === null || y2 === null) continue;
            if (Math.abs(y2 - y1) < 2) continue;
            const el = document.createElement('div');
            el.className = 'zoneBox';
            el.style.left = x1 + 'px';
            el.style.width = (x2 - x1) + 'px';
            el.style.top = Math.min(y1, y2) + 'px';
            el.style.height = Math.abs(y2 - y1) + 'px';
            el.style.background = z.color;
            container.appendChild(el);
            zoneBoxes.push(el);
        }
    }

    chart.timeScale().subscribeVisibleLogicalRangeChange(() => {
        chart.resize(container.clientWidth, container.clientHeight);
        renderZones();
    });

    window.addEventListener('resize', () => {
        chart.resize(container.clientWidth, container.clientHeight);
        renderZones();
    });

    renderZones();
</script>
</body>
</html>
""";
}
