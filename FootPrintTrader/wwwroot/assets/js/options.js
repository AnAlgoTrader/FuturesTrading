// Options window client.
// Renders the SPXW chain pushed by Program.FetchOptionsChainAsync. Loaded by options.html.

let latestChainData = null;
let currentStagedSpread = null;
let liveSpreadQuote = null;
let spreadSubscriptionState = 'Idle';

function sendToHost(message) {
    if (window.external && window.external.sendMessage) {
        window.external.sendMessage(typeof message === 'string' ? message : JSON.stringify(message));
    }
}

function receiveFromBackground(message) {
    try {
        const { Type, Data } = JSON.parse(message);
        if (Type === 'OPTIONS_CHAIN') {
            renderChain(Data);
        } else if (Type === 'OPTIONS_ERROR') {
            showError(Data && Data.message);
        } else if (Type === 'OPTIONS_SPREAD_QUOTE') {
            applySpreadQuote(Data);
        } else if (Type === 'OPTIONS_SPREAD_STATUS') {
            applySpreadStatus(Data);
        }
    } catch (err) {
        console.error('Failed to parse background message:', err);
    }
}

function applySpreadQuote(data) {
    if (!currentStagedSpread || !data) return;

    // Ignore quotes that belong to a spread we already replaced.
    if (Math.abs(toNumber(data.shortStrike) - currentStagedSpread.shortStrike) > 0.01) return;
    if (Math.abs(toNumber(data.longStrike) - currentStagedSpread.longStrike) > 0.01) return;

    liveSpreadQuote = data;
    renderSpreadTable();
}

function applySpreadStatus(data) {
    if (!data) return;

    if (data.state) spreadSubscriptionState = data.state;

    switch (data.state) {
        case 'Subscribing':
            setLiveTag('warn', 'CONNECTING');
            break;
        case 'Live':
            setLiveTag('live', 'LIVE');
            break;
        case 'Delayed':
            setLiveTag('warn', 'DELAYED');
            break;
        case 'Error':
            liveSpreadQuote = null;
            setLiveTag('error', 'NO DATA');
            showError(data.message);
            break;
        case 'Cleared':
            liveSpreadQuote = null;
            setLiveTag('idle', 'CBOE');
            break;
    }
}

function setLiveTag(state, text) {
    const tag = document.getElementById('opt-live-tag');
    const label = document.getElementById('opt-live-text');
    if (label) label.textContent = text;
    if (!tag) return;

    tag.classList.remove('live-warn', 'live-error', 'live-idle');
    if (state === 'warn') tag.classList.add('live-warn');
    else if (state === 'error') tag.classList.add('live-error');
    else if (state === 'idle') tag.classList.add('live-idle');
}

function renderChain(data) {
    if (!data || !Array.isArray(data.rows) || !data.rows.length) {
        showError('Options chain is empty.');
        return;
    }

    latestChainData = data;

    const symbol = data.symbol || '-';
    const expiry = data.expirationDate || '-';
    const spot = toNumber(data.underlyingPrice);

    setText('opt-symbol', symbol);
    setText('opt-expiry', expiry);
    setText('opt-spot', spot > 0 ? spot.toFixed(2) : '-');
    setText('opt-updated', data.updatedAt || '-');
    document.title = `Options - ${symbol} ${expiry}`;

    // The ATM row is the strike closest to the underlying price.
    let atmStrike = toNumber(data.rows[0].strike);
    let bestDistance = Infinity;
    for (const row of data.rows) {
        const distance = Math.abs(toNumber(row.strike) - spot);
        if (distance < bestDistance) {
            bestDistance = distance;
            atmStrike = toNumber(row.strike);
        }
    }

    const body = document.getElementById('chain-body');
    const wrap = document.getElementById('table-wrap');
    const scrollTop = wrap ? wrap.scrollTop : 0;
    const fragment = document.createDocumentFragment();

    for (const row of data.rows) {
        const strike = toNumber(row.strike);
        const callItm = spot > 0 && strike < spot;
        const putItm = spot > 0 && strike > spot;

        const tr = document.createElement('tr');

        appendCell(tr, fmtPrice(row.callBid), callItm, 'itm-call');
        appendCell(tr, fmtPrice(row.callAsk), callItm, 'itm-call');
        appendCell(tr, fmtIv(row.callIv), callItm, 'itm-call');
        appendCell(tr, fmtGreek(row.callDelta, 3), callItm, 'itm-call', row.callDelta);
        appendCell(tr, fmtGreek(row.callGamma, 4), callItm, 'itm-call');
        appendCell(tr, fmtOpenInterest(row.callOpenInterest), callItm, 'itm-call');

        const strikeCell = document.createElement('td');
        strikeCell.className = 'strike-col';
        strikeCell.textContent = strike.toFixed(2);
        tr.appendChild(strikeCell);

        appendCell(tr, fmtGex(row.gex), false, null, row.gex, 'gex-col');

        appendCell(tr, fmtPrice(row.putBid), putItm, 'itm-put');
        appendCell(tr, fmtPrice(row.putAsk), putItm, 'itm-put');
        appendCell(tr, fmtIv(row.putIv), putItm, 'itm-put');
        appendCell(tr, fmtGreek(row.putDelta, 3), putItm, 'itm-put', row.putDelta);
        appendCell(tr, fmtGreek(row.putGamma, 4), putItm, 'itm-put');
        appendCell(tr, fmtOpenInterest(row.putOpenInterest), putItm, 'itm-put');

        tr.appendChild(createTradeCell(strike));

        if (strike === atmStrike) tr.classList.add('atm');

        fragment.appendChild(tr);
    }

    body.replaceChildren(fragment);
    if (wrap) wrap.scrollTop = scrollTop;

    const empty = document.getElementById('empty-state');
    if (empty) empty.style.display = 'none';

    setStatus(`${data.rows.length} strikes`, false);

    // Refresh staged spread prices if one is active
    if (currentStagedSpread) {
        refreshStagedSpreadPrices();
    }
}

function appendCell(tr, text, itm, itmClass, rawValue, extraClass) {
    const td = document.createElement('td');
    td.textContent = text;
    if (itm && itmClass) td.classList.add(itmClass);
    if (extraClass) td.classList.add(extraClass);

    const value = toNumber(rawValue);
    if (value > 0) td.classList.add('val-up');
    else if (value < 0) td.classList.add('val-down');

    tr.appendChild(td);
}

function createTradeCell(strike) {
    const td = document.createElement('td');
    td.className = 'trade-col';

    const actions = document.createElement('div');
    actions.className = 'trade-actions';

    for (const side of ['buy', 'sell']) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = `trade-btn trade-${side}`;
        btn.textContent = side.toUpperCase();
        btn.dataset.side = side;
        btn.dataset.strike = strike.toFixed(2);
        btn.title = side === 'buy'
            ? `Stage Bull Put Spread (Long) with short strike ${strike.toFixed(2)}`
            : `Stage Bear Call Spread (Short) with short strike ${strike.toFixed(2)}`;
        btn.onclick = () => stageSpread(side, strike);
        actions.appendChild(btn);
    }

    td.appendChild(actions);
    return td;
}

function stageSpread(side, strike) {
    if (!latestChainData || !Array.isArray(latestChainData.rows)) return;

    const shortStrike = toNumber(strike);
    const isBullish = side === 'buy';
    const shortType = isBullish ? 'PUT' : 'CALL';
    const longType = isBullish ? 'PUT' : 'CALL';

    // Find row for the short strike
    const shortRow = latestChainData.rows.find(r => Math.abs(toNumber(r.strike) - shortStrike) < 0.01);
    if (!shortRow) return;

    // Minimum spread possible: always the immediate adjacent strike in the target direction
    let longRow = null;
    if (isBullish) {
        // Bull Put Spread: long leg is the immediate lower strike
        const lowerRows = latestChainData.rows.filter(r => toNumber(r.strike) < shortStrike - 0.01);
        if (lowerRows.length === 0) {
            showError(`Cannot stage Bull Put Spread: no lower strike available for ${shortStrike}`);
            return;
        }
        // Highest strike strictly below shortStrike
        longRow = lowerRows.reduce((max, r) => toNumber(r.strike) > toNumber(max.strike) ? r : max);
    } else {
        // Bear Call Spread: long leg is the immediate higher strike
        const higherRows = latestChainData.rows.filter(r => toNumber(r.strike) > shortStrike + 0.01);
        if (higherRows.length === 0) {
            showError(`Cannot stage Bear Call Spread: no higher strike available for ${shortStrike}`);
            return;
        }
        // Lowest strike strictly above shortStrike
        longRow = higherRows.reduce((min, r) => toNumber(r.strike) < toNumber(min.strike) ? r : min);
    }

    const longStrike = toNumber(longRow.strike);

    const isSameSpread = !!currentStagedSpread
        && Math.abs(currentStagedSpread.shortStrike - shortStrike) < 0.01
        && Math.abs(currentStagedSpread.longStrike - longStrike) < 0.01
        && currentStagedSpread.shortType === shortType
        && currentStagedSpread.longType === longType;

    if (isSameSpread && spreadSubscriptionState !== 'Error') {
        // Already streaming this exact spread; keep the live values on screen.
        renderSpreadTable();
        return;
    }

    currentStagedSpread = {
        side,
        strategy: isBullish ? 'BULL PUT' : 'BEAR CALL',
        tone: isBullish ? 'bullish' : 'bearish',
        shortStrike,
        shortType,
        longStrike,
        longType
    };

    liveSpreadQuote = null;
    spreadSubscriptionState = 'Subscribing';
    setLiveTag('warn', 'CONNECTING');
    renderSpreadTable();

    // Ask the host to stream live IB quotes for the two legs of this spread.
    sendToHost({
        action: 'stageSpread',
        strategy: currentStagedSpread.strategy,
        shortStrike,
        longStrike,
        shortRight: shortType[0],
        longRight: longType[0]
    });
}

function refreshStagedSpreadPrices() {
    if (!currentStagedSpread || !latestChainData) return;
    renderSpreadTable();
}

function renderSpreadTable() {
    const row = document.getElementById('spread-row');
    const stratEl = document.getElementById('sp-strategy');
    const shortLegEl = document.getElementById('sp-short-leg');
    const shortBidEl = document.getElementById('sp-short-bid');
    const shortAskEl = document.getElementById('sp-short-ask');
    const longLegEl = document.getElementById('sp-long-leg');
    const longBidEl = document.getElementById('sp-long-bid');
    const longAskEl = document.getElementById('sp-long-ask');
    const netCreditEl = document.getElementById('sp-net-credit');
    const maxRiskEl = document.getElementById('sp-max-risk');
    const btnClear = document.getElementById('btn-spread-clear');
    const btnTrade = document.getElementById('btn-spread-trade');

    if (!currentStagedSpread) {
        if (row) row.className = 'empty';
        if (stratEl) stratEl.innerHTML = '<span class="spread-badge badge-empty">NO SPREAD</span>';
        if (shortLegEl) shortLegEl.innerHTML = '<span class="dim">-</span>';
        if (shortBidEl) shortBidEl.innerHTML = '<span class="dim">-</span>';
        if (shortAskEl) shortAskEl.innerHTML = '<span class="dim">-</span>';
        if (longLegEl) longLegEl.innerHTML = '<span class="dim">-</span>';
        if (longBidEl) longBidEl.innerHTML = '<span class="dim">-</span>';
        if (longAskEl) longAskEl.innerHTML = '<span class="dim">-</span>';
        if (netCreditEl) netCreditEl.innerHTML = '<span class="dim">-</span>';
        if (maxRiskEl) maxRiskEl.innerHTML = '<span class="dim">-</span>';
        if (btnClear) btnClear.disabled = true;
        if (btnTrade) btnTrade.disabled = true;
        return;
    }

    const { strategy, tone, shortStrike, shortType, longStrike, longType } = currentStagedSpread;
    const isPut = shortType === 'PUT';

    const shortRow = latestChainData?.rows?.find(r => Math.abs(toNumber(r.strike) - shortStrike) < 0.01);
    const longRow = latestChainData?.rows?.find(r => Math.abs(toNumber(r.strike) - longStrike) < 0.01);

    const hasLive = !!liveSpreadQuote
        && Math.abs(toNumber(liveSpreadQuote.shortStrike) - shortStrike) < 0.01
        && Math.abs(toNumber(liveSpreadQuote.longStrike) - longStrike) < 0.01
        && (toNumber(liveSpreadQuote.shortBid) > 0 || toNumber(liveSpreadQuote.shortAsk) > 0)
        && (toNumber(liveSpreadQuote.longBid) > 0 || toNumber(liveSpreadQuote.longAsk) > 0);

    const shortBid = hasLive ? toNumber(liveSpreadQuote.shortBid)
        : (shortRow ? toNumber(isPut ? shortRow.putBid : shortRow.callBid) : 0);
    const shortAsk = hasLive ? toNumber(liveSpreadQuote.shortAsk)
        : (shortRow ? toNumber(isPut ? shortRow.putAsk : shortRow.callAsk) : 0);
    const longBid = hasLive ? toNumber(liveSpreadQuote.longBid)
        : (longRow ? toNumber(isPut ? longRow.putBid : longRow.callBid) : 0);
    const longAsk = hasLive ? toNumber(liveSpreadQuote.longAsk)
        : (longRow ? toNumber(isPut ? longRow.putAsk : longRow.callAsk) : 0);

    // Natural credit = Short Bid - Long Ask; Mid credit = Short Mid - Long Mid
    const shortMid = (shortBid > 0 && shortAsk > 0) ? (shortBid + shortAsk) / 2 : (shortBid || shortAsk);
    const longMid = (longBid > 0 && longAsk > 0) ? (longBid + longAsk) / 2 : (longBid || longAsk);
    const credit = (shortBid > 0 && longAsk > 0) ? (shortBid - longAsk) : (shortMid - longMid);

    const width = Math.abs(shortStrike - longStrike);
    const totalCredit = credit > 0 ? (credit * 100) : 0;
    const risk = Math.max(0, width - Math.max(0, credit)) * 100;

    if (row) row.className = `active ${tone}`;
    if (stratEl) stratEl.innerHTML = `<span class="spread-badge badge-${tone}">${strategy}</span>`;

    if (shortLegEl) shortLegEl.textContent = `${shortStrike.toFixed(0)} ${shortType[0]}`;
    if (shortBidEl) shortBidEl.textContent = fmtPrice(shortBid);
    if (shortAskEl) shortAskEl.textContent = fmtPrice(shortAsk);
    if (longLegEl) longLegEl.textContent = `${longStrike.toFixed(0)} ${longType[0]}`;
    if (longBidEl) longBidEl.textContent = fmtPrice(longBid);
    if (longAskEl) longAskEl.textContent = fmtPrice(longAsk);

    if (netCreditEl) {
        if (credit > 0) {
            netCreditEl.textContent = `+$${credit.toFixed(2)}`;
            netCreditEl.title = `${hasLive ? 'Live' : 'Cboe'} $${totalCredit.toFixed(0)} per contract (Short Bid ${fmtPrice(shortBid)} - Long Ask ${fmtPrice(longAsk)})`;
        } else if (credit !== 0) {
            netCreditEl.textContent = `$${credit.toFixed(2)}`;
            netCreditEl.title = '';
        } else {
            netCreditEl.innerHTML = '<span class="dim">-</span>';
        }
    }

    if (maxRiskEl) {
        if (risk > 0) {
            maxRiskEl.textContent = `$${risk.toFixed(0)}`;
            maxRiskEl.title = `${width.toFixed(1)} pt spread - $${totalCredit.toFixed(0)} credit per contract`;
        } else {
            maxRiskEl.innerHTML = '<span class="dim">-</span>';
        }
    }

    if (btnClear) btnClear.disabled = false;
    if (btnTrade) {
        btnTrade.disabled = true; // Staging only in this phase
        btnTrade.title = "Spread staging complete. IBKR data request & trade execution in next phase.";
    }
}

function clearSpread() {
    const hadSpread = !!currentStagedSpread;
    currentStagedSpread = null;
    liveSpreadQuote = null;
    spreadSubscriptionState = 'Idle';
    renderSpreadTable();
    setLiveTag('idle', 'CBOE');

    if (hadSpread) sendToHost({ action: 'clearSpread' });
}

// Bind clear button
document.addEventListener('DOMContentLoaded', () => {
    const btnClear = document.getElementById('btn-spread-clear');
    if (btnClear) btnClear.addEventListener('click', clearSpread);
});

function showError(message) {
    const text = message || 'Options chain unavailable.';
    setStatus(text, true);

    const body = document.getElementById('chain-body');
    const empty = document.getElementById('empty-state');
    const hasRows = body && body.children.length > 0;

    if (empty && !hasRows) {
        empty.textContent = text;
        empty.style.display = 'flex';
    }
}

function setStatus(text, isError) {
    const status = document.getElementById('status-text');
    if (!status) return;
    status.textContent = text;
    status.className = isError ? 'status-error' : '';

    const count = document.getElementById('status-count');
    if (count && isError) count.textContent = '';
}

function setText(id, value) {
    const el = document.getElementById(id);
    if (el) el.textContent = value;
}

function toNumber(value) {
    const n = Number(value);
    return Number.isFinite(n) ? n : 0;
}

function fmtPrice(value) {
    const n = toNumber(value);
    return n > 0 ? n.toFixed(2) : '-';
}

function fmtIv(value) {
    const n = toNumber(value);
    return n > 0 ? (n * 100).toFixed(2) + '%' : '-';
}

function fmtGreek(value, decimals) {
    const n = toNumber(value);
    return n !== 0 ? n.toFixed(decimals) : '-';
}

function fmtOpenInterest(value) {
    const n = toNumber(value);
    return n > 0 ? Math.round(n).toLocaleString() : '-';
}

function fmtGex(value) {
    const n = toNumber(value);
    if (n === 0) return '-';
    const sign = n > 0 ? '+' : '\u2212';
    return sign + '$' + compactNumber(Math.abs(n));
}

function compactNumber(n) {
    if (n >= 1e9) return (n / 1e9).toFixed(2) + 'B';
    if (n >= 1e6) return (n / 1e6).toFixed(2) + 'M';
    if (n >= 1e3) return (n / 1e3).toFixed(1) + 'K';
    return n.toFixed(0);
}
