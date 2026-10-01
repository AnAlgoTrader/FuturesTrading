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

function receiveFromBackground(message){
    const accountInfo = JSON.parse(message);

    setStat('stat-updated-on', formatUpdatedOn(new Date(accountInfo.UpdateOn)));
    setStat('stat-funds', accountInfo.AvailableFunds);
    setStat('stat-buying-power', accountInfo.BuyingPower);
    setStat('stat-eur', accountInfo.CashEuro);
    setStat('stat-usd', accountInfo.CashUsd);
}

function setStat(id, value){
    document.getElementById(id).textContent = value;
}