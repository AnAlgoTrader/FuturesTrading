const dateFormatter = new Intl.DateTimeFormat('en-GB', {
    weekday: 'short',
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false
});

function formatUpdatedOn(date) {
    const parts = Object.fromEntries(dateFormatter.formatToParts(date).map(p => [p.type, p.value]));
    return parts.weekday + ' ' + parts.day + ' ' + parts.month + ' ' + parts.hour + ':' + parts.minute;
}

function callBackground(message){
    window.external.sendMessage(message);
}

const handlers = {
    ACCOUNT_UPDATE: accountUpdate,
    CONTRACT_UPDATE: contractUpdate,
    CANDLE_SET: candleSet,
    CANDLE_UPDATE: candleUpdate,
    MARKET_DATA: marketData,
    ORDER_LINE: orderLine,
    FOOTPRINT_UPDATE: footprintUpdate
};

function receiveFromBackground(message){
    const { Type, Data } = JSON.parse(message);

    if (handlers[Type]) {
        handlers[Type](Data);
    }
}

function accountUpdate(data){
    setStat('stat-updated-on', formatUpdatedOn(new Date(data.UpdateOn)));
    setStat('stat-funds', data.AvailableFunds);
    setStat('stat-buying-power', data.BuyingPower);
    setStat('stat-eur', data.CashEuro);
    setStat('stat-usd', data.CashUsd);
}

function contractUpdate(data){
    setStat('stat-expiration', formatExpiration(new Date(data.Expiration + 'T00:00:00')));
}

function formatExpiration(date) {
    const parts = Object.fromEntries(dateFormatter.formatToParts(date).map(p => [p.type, p.value]));
    return parts.weekday + ' ' + parts.day + ' ' + parts.month;
}

function setStat(id, value){
    document.getElementById(id).textContent = value;
}

function candleSet(data){
    window.setData(data.Candles);
}

function candleUpdate(data){
    window.updateData(data.Time, data.Open, data.High, data.Low, data.Close, data.Vwap);
}

function marketData(data){
    setPrice('price-buy', data.Ask);
    setPrice('price-sell', data.Bid);
}

function setPrice(id, value){
    const el = document.getElementById(id);
    if (!el) return;
    const text = value > 0 ? value.toFixed(2) : '-';
    if (el.textContent !== text) {
        el.textContent = text;
    }
}

function orderLine(data){
    window.setOrderLine(data.Price, data.Color, data.Title, data.ChartPrice);
}

function footprintUpdate(data){
    if (window.renderFootprint) {
        window.renderFootprint(data);
    }
}