(function () {
    const CANDLE_COLOR = '#808080';

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
        upColor: CANDLE_COLOR,
        downColor: CANDLE_COLOR,
        wickUpColor: CANDLE_COLOR,
        wickDownColor: CANDLE_COLOR,
        borderVisible: false
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

    window.setData = function (candles) {
        candlesSeries.setData(candles.map(function (c) {
            return {
                time: c.Time,
                open: c.Open,
                high: c.High,
                low: c.Low,
                close: c.Close,
                color: c.Color || undefined
            };
        }));
        vwapSeries.setData(candles.map(function (c) {
            return { time: c.Time, value: c.Vwap };
        }));
        chart.timeScale().scrollToRealTime();
    };

    window.updateData = function (data){
        candlesSeries.update({
            time: data.Time,
            open: data.Open,
            high: data.High,
            low: data.Low,
            close: data.Close,
            color: data.Color || undefined
        });
        vwapSeries.update({ time: data.Time, value: data.Vwap });
        window.setOverlay(data);
    };

    const overlayLines = {};

    function upsertOverlayLine(key, price, options) {
        if (price === null || price === undefined || !(price > 0)) {
            removeOverlayLine(key);
            return;
        }
        const existing = overlayLines[key];
        if (existing) {
            if (existing.price !== price) {
                existing.line.applyOptions({ price: price });
                existing.price = price;
            }
            return;
        }
        overlayLines[key] = {
            line: candlesSeries.createPriceLine(Object.assign({ price: price }, options)),
            price: price
        };
    }

    function removeOverlayLine(key) {
        const existing = overlayLines[key];
        if (!existing) return;
        try { candlesSeries.removePriceLine(existing.line); } catch (e) {}
        delete overlayLines[key];
    }

    const dashed = LightweightCharts.LineStyle.Dashed;
    const dotted = LightweightCharts.LineStyle.Dotted;

    const levelDefs = [
        { key: 'pdh', get: l => l.PreviousDayHigh, broken: l => l.PreviousDayHighBroken, color: '#d1d4dc', title: 'PDH', style: dashed },
        { key: 'pdl', get: l => l.PreviousDayLow, broken: l => l.PreviousDayLowBroken, color: '#d1d4dc', title: 'PDL', style: dashed },
        { key: 'pdc', get: l => l.PreviousDayClose, broken: () => false, color: '#787b86', title: 'PDC', style: dotted },
        { key: 'onh', get: l => l.OvernightHigh, broken: l => l.OvernightHighBroken, color: '#2962ff', title: 'ONH', style: dashed },
        { key: 'onl', get: l => l.OvernightLow, broken: l => l.OvernightLowBroken, color: '#2962ff', title: 'ONL', style: dashed },
        { key: 'ibh', get: l => l.InitialBalanceHigh, broken: l => l.InitialBalanceHighBroken, color: '#ab47bc', title: 'IBH', style: dashed },
        { key: 'ibl', get: l => l.InitialBalanceLow, broken: l => l.InitialBalanceLowBroken, color: '#ab47bc', title: 'IBL', style: dashed }
    ];

    window.setOverlay = function (data) {
        if (!data || !data.Levels) return;
        const levels = data.Levels;

        levelDefs.forEach(function (def) {
            const price = def.broken(levels) ? null : def.get(levels);
            upsertOverlayLine(def.key, price, {
                color: def.color,
                lineWidth: 1,
                lineStyle: def.style,
                axisLabelVisible: true,
                title: def.title
            });
        });
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
        callBackground('closeTrade');
    });

    window.setOrderLine = function (price, color, title, chartPrice) {
        if (!price || price <= 0 || !chartPrice || chartPrice <= 0) {
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

        lastOrderPrice = chartPrice;

        const lineOptions = {
            price: chartPrice,
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
