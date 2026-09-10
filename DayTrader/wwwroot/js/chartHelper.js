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

    const candlesSeries = chart.addSeries(LightweightCharts.CandlestickSeries, {
        upColor: '#26a69a',
        downColor: '#ef5350',
        borderVisible: false,
        wickUpColor: '#26a69a',
        wickDownColor: '#ef5350'
    });

    const vwapSeries = chart.addSeries(LightweightCharts.LineSeries, {
        color: '#FF9900',
        lineWidth: 1,
        priceLineVisible: false,
        crosshairMarkerVisible: false,
        lastValueVisible: false
    });

    let activePriceLine = null;
    let lastOrderPrice = 0;
    const closeBtn = document.getElementById('close-btn');

    window.updateData = function (time, open, high, low, close, vwap){
        candlesSeries.update({ time: time, open: open, high: high, low: low, close: close });
        vwapSeries.update({ time: time, value: vwap });
    };

    function updateCloseButtonPosition() {
        if (!lastOrderPrice || lastOrderPrice <= 0) {
            closeBtn.style.display = 'none';
            return;
        }
        const y = candlesSeries.priceToCoordinate(lastOrderPrice);
        if (y === null || y === undefined || isNaN(y) || y < 0 || y > container.clientHeight) {
            closeBtn.style.display = 'none';
        } else {
            closeBtn.style.display = 'flex';
            closeBtn.style.top = y + 'px';
        }
    }

    closeBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        e.preventDefault();
        try {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage('closeTrade');
            }
        } catch (err) {}
        try {
            if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.postAvWebViewMessage) {
                window.webkit.messageHandlers.postAvWebViewMessage.postMessage('closeTrade');
            }
        } catch (err) {}
    });

    window.setOrderLine = function (price, color, title, spyPrice) {
        if (!price || price <= 0 || !spyPrice || spyPrice <= 0) {
            lastOrderPrice = 0;
            if (activePriceLine) {
                try {
                    candlesSeries.removePriceLine(activePriceLine);
                } catch (e) {}
                activePriceLine = null;
            }
            updateCloseButtonPosition();
            return;
        }

        lastOrderPrice = spyPrice;

        const lineOptions = {
            price: spyPrice,
            color: color || '#26a69a',
            lineWidth: 2,
            lineStyle: LightweightCharts.LineStyle.Dashed,
            axisLabelVisible: true,
            title: title || ''
        };

        if (!activePriceLine) {
            activePriceLine = candlesSeries.createPriceLine(lineOptions);
        } else {
            activePriceLine.applyOptions(lineOptions);
        }

        updateCloseButtonPosition();
    };

    window.addEventListener('resize', function () {
        chart.resize(container.clientWidth, container.clientHeight);
        updateCloseButtonPosition();
    });

    chart.timeScale().subscribeVisibleLogicalRangeChange(function () {
        updateCloseButtonPosition();
    });
})();
