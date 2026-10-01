const dateFormatter = new Intl.DateTimeFormat('en-GB', {
    weekday: 'short',
    day: '2-digit',
    month: 'short'
});

function callBackground(message){
    window.external.sendMessage(message);
}

function receiveFromBackground(message){
    const accountInfo = JSON.parse(message);

    setStat('stat-updated-on', dateFormatter.format(new Date(accountInfo.UpdateOn)));
    setStat('stat-funds', accountInfo.AvailableFunds);
    setStat('stat-buying-power', accountInfo.BuyingPower);
    setStat('stat-eur', accountInfo.CashEuro);
    setStat('stat-usd', accountInfo.CashUsd);
}

function setStat(id, value){
    document.getElementById(id).textContent = value;
}