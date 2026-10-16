function callBackground(message) {
    if (window.external && window.external.sendMessage) {
        window.external.sendMessage(message);
    }
}

const handlers = {
    FOOTPRINT_SNAPSHOT: footprintSnapshot,
    MARKET_DATA: marketData,
    POSITION_UPDATE: positionUpdate
};

let lastSignature = null;

function receiveFromBackground(message) {
    try {
        const { Type, Data } = JSON.parse(message);
        if (handlers[Type]) {
            handlers[Type](Data);
        }
    } catch (err) {
        console.error("Failed to parse background message:", err);
    }
}

function marketData(data) {
    if (!data) return;
    const ask = data.ask ?? data.Ask;
    const bid = data.bid ?? data.Bid;
    setPrice('price-buy', ask);
    setPrice('price-sell', bid);
}

function setPrice(id, value) {
    const el = document.getElementById(id);
    if (!el) return;
    const num = Number(value);
    const text = (!isNaN(num) && num > 0) ? num.toFixed(2) : '-';
    if (el.textContent !== text) {
        el.textContent = text;
    }
}

function positionUpdate(data) {
    if (!data) return;

    const symbol = data.symbol ?? data.Symbol ?? 'MES';
    const direction = data.direction ?? data.Direction ?? '';
    const netQuantity = data.netQuantity ?? data.NetQuantity ?? 0;
    const averagePrice = data.averagePrice ?? data.AveragePrice ?? 0;
    const currentPrice = data.currentPrice ?? data.CurrentPrice ?? 0;
    const unrealizedPnL = data.unrealizedPnL ?? data.UnrealizedPnL ?? 0;
    const pointsPnL = data.pointsPnL ?? data.PointsPnL ?? 0;

    const symbolEl = document.getElementById('pos-symbol');
    const directionEl = document.getElementById('pos-direction');
    const qtyEl = document.getElementById('pos-qty');
    const avgPriceEl = document.getElementById('pos-avg-price');
    const currPriceEl = document.getElementById('pos-curr-price');
    const pnlEl = document.getElementById('pos-pnl');
    const ptsEl = document.getElementById('pos-pts');
    const flattenBtn = document.getElementById('btn-flatten');

    if (symbolEl) symbolEl.textContent = symbol;

    const isFlat = !netQuantity || netQuantity === 0;

    if (isFlat) {
        if (directionEl) {
            directionEl.className = 'status-pill neutral';
            directionEl.textContent = 'FLAT';
        }
        if (qtyEl) qtyEl.textContent = '0';
        if (avgPriceEl) avgPriceEl.textContent = '-';
        if (currPriceEl) currPriceEl.textContent = (currentPrice > 0) ? Number(currentPrice).toFixed(2) : '-';
        if (pnlEl) {
            pnlEl.textContent = '$0.00';
            pnlEl.className = 'pos-pnl';
        }
        if (ptsEl) ptsEl.textContent = '(0.00 pts)';
        if (flattenBtn) flattenBtn.style.display = 'none';
    } else {
        const isLong = String(direction).toUpperCase() === 'BUY';
        if (directionEl) {
            directionEl.className = `status-pill ${isLong ? 'bullish' : 'bearish'}`;
            directionEl.textContent = isLong ? 'LONG' : 'SHORT';
        }
        if (qtyEl) qtyEl.textContent = String(Math.abs(netQuantity));
        if (avgPriceEl) avgPriceEl.textContent = (averagePrice > 0) ? Number(averagePrice).toFixed(2) : '-';
        if (currPriceEl) currPriceEl.textContent = (currentPrice > 0) ? Number(currentPrice).toFixed(2) : '-';

        const pnl = Number(unrealizedPnL) || 0;
        const pts = Number(pointsPnL) || 0;
        const pnlSign = pnl >= 0 ? '+' : '−';
        const ptsSign = pts >= 0 ? '+' : '−';

        if (pnlEl) {
            pnlEl.textContent = `${pnlSign}$${Math.abs(pnl).toFixed(2)}`;
            pnlEl.className = `pos-pnl ${pnl >= 0 ? 'val-up' : 'val-down'}`;
        }
        if (ptsEl) {
            ptsEl.textContent = `(${ptsSign}${Math.abs(pts).toFixed(2)} pts)`;
        }
        if (flattenBtn) flattenBtn.style.display = 'inline-block';
    }
}

function footprintSnapshot(data) {
    if (!window.footprint || !Array.isArray(data.bars) || !data.bars.length) return;

    const bucketEl = document.getElementById('bucket-size');
    if (bucketEl && data.tradesPerBar) bucketEl.textContent = data.tradesPerBar;

    // Update the rich Order Flow side panel
    if (data.context) {
        updateSidePanel(data.context, data.rowSize || 0.25);
    }
    if (data.interpretation) {
        updateInterpretationPanel(data.interpretation);
    }

    const bars = data.bars;
    const signature = bars.length + ':' + bars[0].id;

    if (signature !== lastSignature) {
        lastSignature = signature;
        window.footprint.load(data);
    } else {
        window.footprint.updateLast(bars[bars.length - 1]);
    }
}

function fmtNum(n) {
    if (n == null || isNaN(n)) return "-";
    const abs = Math.abs(n);
    if (abs >= 1000) return (n / 1000).toFixed(1) + "k";
    return String(Math.round(n * 10) / 10);
}

function fmtSigned(n) {
    if (n == null || isNaN(n)) return "-";
    const sign = n > 0 ? "+" : n < 0 ? "−" : "";
    return sign + fmtNum(Math.abs(n));
}

function updateSidePanel(ctx, tickSize) {
    if (!ctx) return;

    const dec = (String(tickSize).split('.')[1] || '').length || 2;

    // 1. Bias Card & Headline
    const biasCard = document.getElementById('bias-card');
    const biasHeadline = document.getElementById('bias-headline');
    const biasAction = document.getElementById('bias-action');

    const tone = ctx.biasTone || 'neutral';
    if (biasCard) {
        biasCard.style.display = '';
        biasCard.className = `card bias-card tone-${tone}`;
    }
    if (biasHeadline) biasHeadline.textContent = ctx.bias || 'Balanced';
    if (biasAction) biasAction.textContent = ctx.action || '-';

    // 2. Signals Container
    const signalsCount = document.getElementById('signals-count');
    const signalsContainer = document.getElementById('signals-container');

    const signals = Array.isArray(ctx.signals) ? ctx.signals : [];
    if (signalsCount) signalsCount.textContent = signals.length;

    if (signalsContainer) {
        if (!signals.length) {
            signalsContainer.innerHTML = '<div class="empty-state">No active order flow signals</div>';
        } else {
            signalsContainer.innerHTML = signals.map(sig => {
                const sTone = sig.tone || 'neutral';
                return `
                    <div class="signal-item tone-${sTone}">
                        <div class="signal-header">
                            <span class="signal-title">${escapeHtml(sig.title)}</span>
                        </div>
                    </div>
                `;
            }).join('');
        }
    }

    // 3. Live Bar Metrics
    const mDelta = document.getElementById('m-delta');
    const mDeltaPct = document.getElementById('m-delta-pct');
    const mVol = document.getElementById('m-vol');
    const mPoc = document.getElementById('m-poc');
    const mPocZone = document.getElementById('m-poc-zone');
    const mPocShift = document.getElementById('m-poc-shift');

    if (mDelta) {
        mDelta.textContent = fmtSigned(ctx.currentDelta);
        mDelta.className = `metric-val ${ctx.currentDelta > 0 ? 'val-up' : ctx.currentDelta < 0 ? 'val-down' : ''}`;
    }

    if (mDeltaPct) {
        const pct = ctx.deltaPercent != null ? ctx.deltaPercent : 0;
        const sign = pct > 0 ? "+" : "";
        mDeltaPct.textContent = sign + pct.toFixed(1) + "%";
        mDeltaPct.className = `metric-val ${pct > 0 ? 'val-up' : pct < 0 ? 'val-down' : ''}`;
    }

    if (mVol) mVol.textContent = fmtNum(ctx.currentVolume);
    if (mPoc) mPoc.textContent = Number(ctx.pocPrice).toFixed(dec);
    if (mPocZone) mPocZone.textContent = ctx.pocPosition || '-';
    if (mPocShift) {
        mPocShift.textContent = ctx.pocMigration || '-';
        if (ctx.pocMigration && ctx.pocMigration.includes('▲')) mPocShift.className = 'metric-val val-up';
        else if (ctx.pocMigration && ctx.pocMigration.includes('▼')) mPocShift.className = 'metric-val val-down';
        else mPocShift.className = 'metric-val';
    }

    // 4. Structure & Multi-Bar Metrics
    const mCumDelta = document.getElementById('m-cum-delta');
    const mDiv = document.getElementById('m-divergence');

    if (mCumDelta) {
        mCumDelta.textContent = fmtSigned(ctx.cumulativeDelta);
        mCumDelta.className = `metric-val ${ctx.cumulativeDelta > 0 ? 'val-up' : ctx.cumulativeDelta < 0 ? 'val-down' : ''}`;
    }

    if (mDiv) {
        const divText = ctx.deltaDivergence || 'None';
        mDiv.textContent = divText;
        if (divText.includes('Bullish')) mDiv.className = 'metric-val val-up';
        else if (divText.includes('Bearish')) mDiv.className = 'metric-val val-down';
        else mDiv.className = 'metric-val';
    }
}

function escapeHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function updateInterpretationPanel(interp) {
    if (!interp) return;

    const card = document.getElementById('interpretation-card');
    const headline = document.getElementById('interp-headline');
    const action = document.getElementById('interp-action');
    const actionRow = document.getElementById('interp-action-row') || (action ? action.parentElement : null);
    const details = document.getElementById('interp-details');
    const factorsEl = document.getElementById('interp-factors');

    const tone = interp.tone || 'neutral';
    if (card) {
        card.style.display = '';
        card.className = `card interpretation-card tone-${tone}`;
    }

    const isNeutral = tone === 'neutral' || (!interp.action && !interp.title);

    if (isNeutral) {
        if (headline) headline.textContent = '-';
        if (action) action.textContent = '';
        if (actionRow) actionRow.style.display = 'none';
        if (details) {
            details.textContent = '';
            details.style.display = 'none';
        }
        if (factorsEl) {
            factorsEl.innerHTML = '';
            factorsEl.style.display = 'none';
        }
        return;
    }

    if (headline) {
        headline.textContent = interp.title || '-';
    }

    if (action && actionRow) {
        if (interp.action) {
            action.textContent = interp.action;
            actionRow.style.display = '';
        } else {
            action.textContent = '';
            actionRow.style.display = 'none';
        }
    }

    if (details) {
        details.textContent = interp.details || '';
        details.style.display = interp.details ? '' : 'none';
    }

    if (factorsEl) {
        const factors = Array.isArray(interp.factors) ? interp.factors : [];
        if (factors.length > 0) {
            factorsEl.innerHTML = factors.map(f => `<div class="interp-factor-item">${escapeHtml(f)}</div>`).join('');
            factorsEl.style.display = '';
        } else {
            factorsEl.innerHTML = '';
            factorsEl.style.display = 'none';
        }
    }
}
