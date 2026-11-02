// Options window client.
// Renders the SPXW chain pushed by Program.FetchOptionsChainAsync. Loaded by options.html.

function receiveFromBackground(message) {
    try {
        const { Type, Data } = JSON.parse(message);
        if (Type === 'OPTIONS_CHAIN') {
            renderChain(Data);
        } else if (Type === 'OPTIONS_ERROR') {
            showError(Data && Data.message);
        }
    } catch (err) {
        console.error('Failed to parse background message:', err);
    }
}

function renderChain(data) {
    if (!data || !Array.isArray(data.rows) || !data.rows.length) {
        showError('Options chain is empty.');
        return;
    }

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

        appendCell(tr, fmtPrice(row.putBid), putItm, 'itm-put');
        appendCell(tr, fmtPrice(row.putAsk), putItm, 'itm-put');
        appendCell(tr, fmtIv(row.putIv), putItm, 'itm-put');
        appendCell(tr, fmtGreek(row.putDelta, 3), putItm, 'itm-put', row.putDelta);
        appendCell(tr, fmtGreek(row.putGamma, 4), putItm, 'itm-put');
        appendCell(tr, fmtOpenInterest(row.putOpenInterest), putItm, 'itm-put');

        if (strike === atmStrike) tr.classList.add('atm');

        fragment.appendChild(tr);
    }

    body.replaceChildren(fragment);
    if (wrap) wrap.scrollTop = scrollTop;

    const empty = document.getElementById('empty-state');
    if (empty) empty.style.display = 'none';

    setStatus(`${data.rows.length} strikes`, false);
}

function appendCell(tr, text, itm, itmClass, rawValue) {
    const td = document.createElement('td');
    td.textContent = text;
    if (itm && itmClass) td.classList.add(itmClass);

    const value = toNumber(rawValue);
    if (value > 0) td.classList.add('val-up');
    else if (value < 0) td.classList.add('val-down');

    tr.appendChild(td);
}

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
