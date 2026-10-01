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
    CANDLE_SET: candleSet,
    CANDLE_UPDATE: candleUpdate,
    MARKET_DATA: marketData,
    ORDER_LINE: orderLine
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

function setStat(id, value){
    document.getElementById(id).textContent = value;
}

function candleSet(data){
    window.setData(data.Candles);
}

function candleUpdate(data){
    window.updateData(data.Time, data.Open, data.High, data.Low, data.Close, data.Vwap, data.Color);
}

function marketData(data){
    setPrice('btn-buy', data.Ask);
    setPrice('btn-sell', data.Bid);
}

function setPrice(id, value){
    document.getElementById(id).textContent = value > 0 ? value.toFixed(2) : '-';
}

function orderLine(data){
    window.setOrderLine(data.Price, data.Color, data.Title, data.ChartPrice);
}