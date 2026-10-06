(function () {
    const container = document.getElementById('footprint-container');
    let hasUserScrolled = false;
    let initialScrollDone = false;

    if (container) {
        container.addEventListener('scroll', function () {
            // If user manually scrolled, don't jerk the scroll on every update unless close
            hasUserScrolled = true;
        }, { passive: true });
    }

    function formatTime(isoOrDateStr) {
        if (!isoOrDateStr) return '';
        const d = new Date(isoOrDateStr);
        if (isNaN(d.getTime())) return '';
        const hh = String(d.getUTCHours()).padStart(2, '0');
        const mm = String(d.getUTCMinutes()).padStart(2, '0');
        const ss = String(d.getUTCSeconds()).padStart(2, '0');
        return `${hh}:${mm}:${ss}`;
    }

    window.renderFootprint = function (columns) {
        if (!container) return;

        if (!columns || !Array.isArray(columns) || columns.length === 0) {
            container.innerHTML = '<div class="footprint-empty">Waiting for footprint data...</div>';
            initialScrollDone = false;
            return;
        }

        // Collect all distinct price levels across all columns
        const allLevels = [];
        columns.forEach(col => {
            if (col && Array.isArray(col.Rows)) {
                col.Rows.forEach(r => {
                    if (typeof r.Level === 'number' && !isNaN(r.Level)) {
                        allLevels.push(Math.round(r.Level * 100) / 100);
                    }
                });
            }
        });

        if (allLevels.length === 0) {
            container.innerHTML = '<div class="footprint-empty">No trade ticks recorded yet...</div>';
            return;
        }

        // Determine step / tick size
        const sortedUnique = Array.from(new Set(allLevels)).sort((a, b) => a - b);
        let tick = 0.25;
        if (sortedUnique.length > 1) {
            let minDiff = Infinity;
            for (let i = 1; i < sortedUnique.length; i++) {
                const diff = Math.round((sortedUnique[i] - sortedUnique[i - 1]) * 10000) / 10000;
                if (diff > 0.0001 && diff < minDiff) minDiff = diff;
            }
            if (minDiff < Infinity && minDiff >= 0.01) {
                tick = minDiff;
            }
        }

        const minPrice = sortedUnique[0];
        const maxPrice = sortedUnique[sortedUnique.length - 1];
        const totalSteps = Math.round((maxPrice - minPrice) / tick);

        const priceLevels = [];
        if (totalSteps >= 0 && totalSteps <= 400) {
            for (let p = maxPrice; p >= minPrice - 0.0001; p -= tick) {
                priceLevels.push(Math.round(p * 100) / 100);
            }
        } else {
            priceLevels.push(...sortedUnique.slice().reverse());
        }

        // Build lookup maps for fast access per column
        const colMaps = columns.map(col => {
            const map = new Map();
            if (col && Array.isArray(col.Rows)) {
                col.Rows.forEach(r => {
                    const key = Math.round(r.Level * 100) / 100;
                    map.set(key, r);
                });
            }
            return map;
        });

        // Current active price from the newest column
        const lastCol = columns[columns.length - 1];
        const activePrice = lastCol && lastCol.LastPrice ? Math.round(lastCol.LastPrice * 100) / 100 : null;

        // Build HTML table
        let html = '<div class="footprint-wrapper"><table class="footprint-table">';

        // THEAD: Headers for columns, with Price on the right
        html += '<thead><tr>';
        columns.forEach((col, idx) => {
            const timeStr = formatTime(col.Time);
            const isLatest = idx === columns.length - 1;
            let trendClass = 'trend-flat';
            let trendIcon = '―';

            if (col.Trend === 1 || col.IsUp) {
                trendClass = 'trend-up';
                trendIcon = '▲';
            } else if (col.Trend === 2 || (!col.IsUp && col.Trend !== 0)) {
                trendClass = 'trend-down';
                trendIcon = '▼';
            }

            const delta = col.DeltaSize !== undefined ? col.DeltaSize : 0;
            const roundedDelta = Math.round(delta);
            const deltaStr = roundedDelta > 0 ? `+${roundedDelta}` : `${roundedDelta}`;
            const deltaClass = roundedDelta > 0 ? 'delta-pos' : roundedDelta < 0 ? 'delta-neg' : 'delta-zero';

            html += `<th class="fp-col-header ${isLatest ? 'fp-latest-col' : ''}">
                <div class="fp-header-time">${timeStr}</div>
                <div class="fp-header-summary">
                    <span class="${trendClass}">${trendIcon}</span> <span class="fp-header-delta ${deltaClass}">${deltaStr}</span>
                </div>
            </th>`;
        });
        html += '<th class="fp-col-header fp-price-header">Price</th>';
        html += '</tr></thead>';

        // TBODY: Rows for each price level
        html += '<tbody>';
        priceLevels.forEach(level => {
            const isCurrentPrice = activePrice !== null && Math.abs(level - activePrice) < 0.001;
            html += `<tr class="fp-row ${isCurrentPrice ? 'fp-current-price-row' : ''}">`;

            // Cells for each column
            columns.forEach((col, cIdx) => {
                const rowData = colMaps[cIdx].get(level);
                if (rowData) {
                    const delta = rowData.DeltaSize !== undefined ? rowData.DeltaSize : (rowData.AskSize - rowData.BidSize);
                    const roundedDelta = Math.round(delta);
                    let deltaStr = roundedDelta > 0 ? `+${roundedDelta}` : `${roundedDelta}`;
                    let deltaCls = roundedDelta > 0 ? 'delta-pos' : roundedDelta < 0 ? 'delta-neg' : 'delta-zero';
                    let pocCls = rowData.IsPoc ? ' is-poc' : '';

                    html += `<td class="fp-cell ${deltaCls}${pocCls}" title="Bid: ${Math.round(rowData.BidSize)} | Ask: ${Math.round(rowData.AskSize)}">${deltaStr}</td>`;
                } else {
                    html += '<td class="fp-cell fp-empty"></td>';
                }
            });

            // Price ladder on the right
            html += `<td class="fp-cell fp-price ${isCurrentPrice ? 'fp-active-price' : ''}">${level.toFixed(2)}</td>`;
            html += '</tr>';
        });
        html += '</tbody></table></div>';

        // Preserve previous scroll position or auto-center on initial load
        const prevScrollTop = container.scrollTop;
        container.innerHTML = html;

        if (!initialScrollDone) {
            const activeRow = container.querySelector('.fp-current-price-row') || container.querySelector('.is-poc');
            if (activeRow) {
                activeRow.scrollIntoView({ block: 'center' });
            }
            initialScrollDone = true;
        } else {
            container.scrollTop = prevScrollTop;
        }
    };
})();
