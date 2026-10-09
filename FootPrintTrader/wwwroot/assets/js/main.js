function callBackground(message) {
    if (window.external && window.external.sendMessage) {
        window.external.sendMessage(message);
    }
}

const handlers = {
    FOOTPRINT_SNAPSHOT: footprintSnapshot
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

function footprintSnapshot(data) {
    if (!window.footprint || !Array.isArray(data.bars) || !data.bars.length) return;

    // Suppress canvas HUD overlay since we now display everything in the dedicated side panel
    if (window.footprint.setAnalysis) {
        window.footprint.setAnalysis([]);
    }

    // Update the rich Order Flow side panel
    if (data.context) {
        updateSidePanel(data.context, data.rowSize || 0.25);
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
    const biasPill = document.getElementById('bias-pill');
    const biasHeadline = document.getElementById('bias-headline');
    const biasAuction = document.getElementById('bias-auction');
    const biasNarrative = document.getElementById('bias-narrative');

    const tone = ctx.biasTone || 'neutral';
    if (biasCard) {
        biasCard.className = `card bias-card tone-${tone}`;
    }
    if (biasPill) {
        biasPill.className = `status-pill ${tone}`;
        biasPill.textContent = (ctx.biasTone || 'Neutral').toUpperCase();
    }
    if (biasHeadline) biasHeadline.textContent = ctx.bias || 'Balanced';
    if (biasAuction) biasAuction.textContent = ctx.auctionStatus || 'Two-Way Auction Balance';
    if (biasNarrative) biasNarrative.textContent = ctx.narrative || 'Analyzing order flow...';

    // 2. Signals Container
    const signalsCount = document.getElementById('signals-count');
    const signalsContainer = document.getElementById('signals-container');

    const signals = Array.isArray(ctx.signals) ? ctx.signals : [];
    if (signalsCount) signalsCount.textContent = signals.length;

    if (signalsContainer) {
        if (!signals.length) {
            signalsContainer.innerHTML = '<div class="empty-state">No active signals on current bar</div>';
        } else {
            signalsContainer.innerHTML = signals.map(sig => {
                const sTone = sig.tone || 'neutral';
                const priceBadge = sig.price ? `<span class="signal-price">${sig.price}</span>` : '';
                return `
                    <div class="signal-item tone-${sTone}">
                        <div class="signal-header">
                            <span class="signal-title">${escapeHtml(sig.title)}</span>
                            ${priceBadge}
                        </div>
                        <div class="signal-desc">${escapeHtml(sig.description)}</div>
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

// Side Panel Toggle
document.addEventListener('DOMContentLoaded', () => {
    const toggleBtn = document.getElementById('toggle-sidebar-btn');
    const sidebar = document.getElementById('sidebar');

    if (toggleBtn && sidebar) {
        toggleBtn.addEventListener('click', () => {
            const isCollapsed = sidebar.classList.toggle('collapsed');
            toggleBtn.textContent = isCollapsed ? '◧ Context' : '◨ Context';
            setTimeout(() => {
                window.dispatchEvent(new Event('resize'));
            }, 250);
        });
    }
});
