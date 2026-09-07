(function () {
    const container = document.getElementById('chart');
    const chart = LightweightCharts.createChart(container, {
        width: container.clientWidth || 800,
        height: container.clientHeight || 600,
        layout: {background: {color: '#131722'}, textColor: '#d1d4dc'},
        grid: {vertLines: {color: '#1e222d'}, horzLines: {color: '#1e222d'}},
        crosshair: {
            mode: LightweightCharts.CrosshairMode.Normal,
            vertLine: {visible: false, labelVisible: false},
            horzLine: {visible: false, labelVisible: false}
        },
        rightPriceScale: {borderColor: '#2a2e39'},
        timeScale: {borderColor: '#2a2e39', timeVisible: true, secondsVisible: false}
    });

    const candles = chart.addSeries(LightweightCharts.CandlestickSeries, {
        upColor: '#26a69a',
        downColor: '#ef5350',
        borderVisible: false,
        wickUpColor: '#26a69a',
        wickDownColor: '#ef5350'
    });

    const vwap = chart.addSeries(LightweightCharts.LineSeries, { color: '#2962FF' });

    window.notifyUI = function (message) {
        document.getElementById('message').textContent = message;
    }
    
    window.updateData = function (time, open, high, low, close, vwap){
        candles.update({ time: time, open: open, high: high, low: low, close: close });
        vwap.update({ time: time, value: vwap });
    }
})();