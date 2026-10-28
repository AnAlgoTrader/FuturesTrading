    // Pure renderer: every number, colour and highlight comes from the JSON. No footprint maths in here.
    const AXIS = 60;
    const wrap = document.getElementById('wrap'), cv = document.getElementById('cv'), ctx = cv.getContext('2d');
    let R = .25, dec = 2, FOOT = 56, labels = ['Delta', 'Vol'], custom = {}, W = 0, H = 0, bars = [], minP = 0, maxP = 1, hover = null;
    const V = {colW: 92, ppu: 16, x0: 0, top: 0};
    const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
    const isNum = v => v !== null && v !== '' && v !== undefined && isFinite(+v);
    const fmt = v => typeof v === 'number' ? (Math.abs(v) >= 1000 ? (v / 1000).toFixed(1) + 'k' : String(v)) : (v == null ? '' : String(v));
    const CMAP = {background: 'panel', text: 'text', muted: 'muted', grid: 'grid', up: 'up', down: 'down', bar: 'vol'};

    function parseColors(c) {
      const o = {}; if (!c || typeof c !== 'object') return o;
      for (const k in c) {
        if (!CMAP[k]) throw new Error(`unknown color "${k}" (use: ${Object.keys(CMAP).join(', ')})`);
        if (typeof c[k] !== 'string' || !CSS.supports('color', c[k])) throw new Error(`invalid color for "${k}": ${c[k]}`);
        o[CMAP[k]] = c[k];
      }
      return o;
    }
    function parseTag(t) {
      if (!t) return null;
      if (typeof t === 'string') return { text: t };
      if (typeof t === 'object' && t.text != null) return Object.assign({}, t, { text: String(t.text) });
      return null;
    }
    function parseBar(r, idx, nR) {
      if (!r || !Array.isArray(r.rows) || !r.rows.length) throw new Error(`bar ${idx + 1} has no "rows"`);
      const rows = [], map = {}; let pmin = Infinity, pmax = -Infinity;
      for (const e of r.rows) {
        const price = +e.price; if (!isFinite(price)) throw new Error(`bar ${idx + 1} has a row without a price`);
        const o = Object.assign({}, e, {price, bidBar: clamp(+e.bidBar || 0, 0, 1), askBar: clamp(+e.askBar || 0, 0, 1)});
        rows.push(o); map[Math.round(price / nR)] = o; pmin = Math.min(pmin, price); pmax = Math.max(pmax, price);
      }
      const label = r.label != null ? String(r.label) : '';
      return {
        id: r.id != null ? String(r.id) : label + '|' + (r.group || ''), rows, map, pmin, pmax,
        hi: isNum(r.high) ? +r.high : pmax, lo: isNum(r.low) ? +r.low : pmin,
        open: isNum(r.open) ? +r.open : null, close: isNum(r.close) ? +r.close : null, color: r.color,
        footer: Array.isArray(r.footer) ? r.footer : [], label, group: r.group ? String(r.group) : '',
        header: parseTag(r.header),
        candleFooter: parseTag(r.candleFooter || r.barFooter || r.footerCell)
      };
    }
    function refreshMeta() {
      let fn = 0; minP = Infinity; maxP = -Infinity;
      for (const b of bars) {minP = Math.min(minP, b.lo - R); maxP = Math.max(maxP, b.hi + R); fn = Math.max(fn, b.footer.length)}
      while (labels.length < fn) labels.push('');
      FOOT = 16 * (labels.length + 1) + 8;
    }
    function load(obj) {
      const raw = Array.isArray(obj) ? obj : obj && obj.bars, meta = Array.isArray(obj) ? {} : obj;
      if (!Array.isArray(raw) || !raw.length) throw new Error('expected a non-empty "bars" array');
      const nR = +(meta.rowSize || meta.tickSize) || .25, nc = parseColors(meta.colors);
      const nb = raw.map((r, i) => parseBar(r, i, nR));
      R = nR; dec = isNum(meta.priceDecimals) ? +meta.priceDecimals : (String(nR).split('.')[1] || '').length;
      custom = nc; wrap.style.background = nc.panel || '';
      labels = Array.isArray(meta.footerLabels) ? meta.footerLabels.map(String) : [];
      const oldFirst = bars.length ? bars[0].id : null, hv = hover;
      bars = nb; refreshMeta();
      if (oldFirst === null) {                                              // first snapshot: frame the chart
        V.colW = 92;
        V.x0 = Math.max(0, bars.length * V.colW - (W - AXIS) + 30);
        fit();
        hover = null;
      } else {
        if (bars[0].id !== oldFirst) { V.x0 -= V.colW; if (hv) hv.i--; }     // history slid one slot: keep visible bars in place
        hover = hv && hv.i >= 0 && bars[hv.i] && bars[hv.i].map[hv.k] ? hv : null;
      }
      limits(); redraw();
    }
    // Replace the last bar (append=false) or add a new one (append=true). The view never moves by itself.
    function put(raw, append) {
      if (!bars.length) throw new Error('call load() with a snapshot first');
      const nb = parseBar(raw, append ? bars.length : bars.length - 1, R);
      if (append) bars.push(nb); else bars[bars.length - 1] = nb;
      refreshMeta();
      limits(); redraw();
    }
    function limits() {
      if (!bars.length) return;
      const cw = W - AXIS, ch = H - FOOT;
      V.ppu = clamp(V.ppu, .3 / R, 32 / R);
      V.x0 = clamp(V.x0, 0, Math.max(0, bars.length * V.colW - cw + 30));
      V.top = clamp(V.top, minP, maxP + ch / V.ppu);
    }
    function fit() {
      if (!bars.length) return;
      const cw = W - AXIS, ch = H - FOOT, i0 = Math.max(0, Math.floor(V.x0 / V.colW)), i1 = Math.min(bars.length - 1, Math.floor((V.x0 + cw) / V.colW));
      let hi = -Infinity, lo = Infinity; for (let i = i0; i <= i1; i++) {hi = Math.max(hi, bars[i].hi + R); lo = Math.min(lo, bars[i].lo - R)}
      const pad = Math.max(R * 3, (hi - lo) * .06); V.ppu = clamp(ch / (hi - lo + 2 * pad), .3 / R, 32 / R); V.top = hi + pad;
    }
    function box(c, w, x1, x2, y, h) {if (!c) return; ctx.strokeStyle = c; ctx.lineWidth = w || 2; ctx.strokeRect(x1 + 1, y + 1.5, x2 - x1 - 2, h - 2); ctx.lineWidth = 1}

    function draw() {
      const dpr = devicePixelRatio || 1;
      if (cv.width !== W * dpr || cv.height !== H * dpr) {cv.width = W * dpr; cv.height = H * dpr; cv.style.width = W + 'px'; cv.style.height = H + 'px'}
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      const cs = getComputedStyle(document.documentElement), C = n => cs.getPropertyValue(n).trim();
      const col = {text: C('--text'), muted: C('--muted'), grid: C('--grid'), up: C('--up'), down: C('--down'), vol: C('--vol'), panel: C('--panel')}; Object.assign(col, custom);
      ctx.clearRect(0, 0, W, H);
      ctx.font = '11px ui-monospace,Menlo,Consolas,monospace'; ctx.textBaseline = 'middle';
      if (!bars.length) {ctx.fillStyle = col.muted; ctx.textAlign = 'center'; ctx.fillText('Waiting for data\u2026', W / 2, H / 2); return }
      const cw = W - AXIS, ch = H - FOOT, ppu = V.ppu, cwid = V.colW, rowH = R * ppu, showNum = rowH >= 12 && cwid >= 60, pBot = V.top - ch / ppu;
      const i0 = Math.max(0, Math.floor(V.x0 / cwid)), i1 = Math.min(bars.length - 1, Math.floor((V.x0 + cw) / cwid));
      const yP = p => (V.top - p) * ppu;

      function drawCandleCell(tag, y, cl, cr, mid) {
        if (y + rowH < 0 || y > ch) return;
        const w = cr - cl;
        if (w < 6) return;

        const text = (tag && tag.text) ? String(tag.text).trim() : '';

        if (text) {
          let textCol = tag.color;
          let bgCol = tag.bg || tag.bgColor;
          let borderCol = tag.border || tag.borderColor;

          if (!textCol || !bgCol || !borderCol) {
            const u = text.toUpperCase();
            if (u.includes('SAB') || u.includes('BRK')) {
              textCol = textCol || col.up;
              bgCol = bgCol || 'rgba(47, 207, 147, 0.22)';
              borderCol = borderCol || 'rgba(47, 207, 147, 0.75)';
            } else if (u.includes('BAB') || u.includes('CDIV')) {
              textCol = textCol || col.down;
              bgCol = bgCol || 'rgba(240, 100, 123, 0.22)';
              borderCol = borderCol || 'rgba(240, 100, 123, 0.75)';
            } else if (u.includes('SLW') || u.includes('SLOW')) {
              textCol = textCol || (custom.warn || '#f2b705');
              bgCol = bgCol || 'rgba(242, 183, 5, 0.22)';
              borderCol = borderCol || 'rgba(242, 183, 5, 0.75)';
            } else {
              textCol = textCol || col.text;
              bgCol = bgCol || 'rgba(138, 147, 166, 0.18)';
              borderCol = borderCol || 'rgba(138, 147, 166, 0.5)';
            }
          }

          ctx.fillStyle = bgCol;
          ctx.fillRect(cl + 1, y + 1, w - 2, rowH - 2);

          ctx.strokeStyle = borderCol;
          ctx.lineWidth = 1.5;
          ctx.strokeRect(cl + 1, y + 1, w - 2, rowH - 2);
          ctx.lineWidth = 1;

          if (cwid >= 28 && rowH >= 8) {
            ctx.save();
            ctx.beginPath();
            ctx.rect(cl + 2, y, w - 4, rowH);
            ctx.clip();
            ctx.fillStyle = textCol;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            const fSize = clamp(Math.floor(Math.min(rowH - 2, 11)), 8, 11);
            ctx.font = `bold ${fSize}px ui-monospace,Menlo,Consolas,monospace`;
            ctx.fillText(text, mid, y + rowH / 2 + 0.5);
            ctx.restore();
          }
        } else {
          ctx.fillStyle = 'rgba(23, 28, 38, 0.45)';
          ctx.fillRect(cl + 1, y + 1, w - 2, rowH - 2);
          ctx.strokeStyle = col.grid;
          ctx.lineWidth = 1;
          ctx.strokeRect(cl + 1, y + 1, w - 2, rowH - 2);
        }
      }

      ctx.save(); ctx.beginPath(); ctx.rect(0, 0, cw, ch); ctx.clip();
      for (let i = i0; i <= i1; i++) {
        const b = bars[i], x = i * cwid - V.x0, cl = x + 9, cr = x + cwid - 4, mid = (cl + cr) / 2, hw = (cr - cl) / 2 - 2;
        for (const r of b.rows) {
          if (r.price - R / 2 > V.top || r.price + R / 2 < pBot) continue;
          const y = yP(r.price + R / 2);
          ctx.fillStyle = col.vol;
          if (r.bidBar) ctx.fillRect(mid - r.bidBar * hw, y + 1, r.bidBar * hw, rowH - 1);
          if (r.askBar) ctx.fillRect(mid, y + 1, r.askBar * hw, rowH - 1);
          if (r.bidFill) {ctx.fillStyle = r.bidFill; ctx.fillRect(cl, y + 1, mid - cl, rowH - 1)}
          if (r.askFill) {ctx.fillStyle = r.askFill; ctx.fillRect(mid, y + 1, cr - mid, rowH - 1)}
          box(r.bidBorder, r.borderWidth, cl, mid, y, rowH); box(r.askBorder, r.borderWidth, mid, cr, y, rowH); box(r.border, r.borderWidth, cl, cr, y, rowH); box(r.priceBorder, r.borderWidth, cl, cr, y, rowH);
          if (showNum) {ctx.fillStyle = col.text; ctx.textAlign = 'right'; ctx.fillText(fmt(r.bid), mid - 5, y + rowH / 2 + 1); ctx.textAlign = 'left'; ctx.fillText(fmt(r.ask), mid + 5, y + rowH / 2 + 1)}
        }
        if (showNum) {ctx.strokeStyle = col.grid; ctx.beginPath(); ctx.moveTo(Math.round(mid) + .5, yP(b.pmax + R / 2)); ctx.lineTo(Math.round(mid) + .5, yP(b.pmin - R / 2)); ctx.stroke()}
        drawCandleCell(b.header, yP(b.pmax + 1.5 * R), cl, cr, mid);
        drawCandleCell(b.candleFooter, yP(b.pmin - 0.5 * R), cl, cr, mid);
        const yH = yP(b.hi + R / 2), yL = yP(b.lo - R / 2);
        ctx.fillStyle = b.color || (b.close !== null && b.open !== null && b.close < b.open ? col.down : col.up);
        ctx.fillRect(x + 3, yH, 1, yL - yH);
        if (b.open !== null && b.close !== null) {const yT = yP(Math.max(b.open, b.close) + R / 2), yB = yP(Math.min(b.open, b.close) - R / 2); ctx.fillRect(x + 1.5, yT, 5, Math.max(yB - yT, 2))}
      }
      ctx.restore();

      // footer: server-supplied rows of text, then the time row
      ctx.save(); ctx.beginPath(); ctx.rect(0, ch, cw, FOOT); ctx.clip();
      ctx.fillStyle = col.panel; ctx.fillRect(0, ch, cw, FOOT);
      ctx.strokeStyle = col.grid; ctx.beginPath(); ctx.moveTo(0, ch + .5); ctx.lineTo(cw, ch + .5); ctx.stroke();
      const every = Math.ceil(56 / cwid), n = labels.length; ctx.textAlign = 'center';
      for (let i = i0; i <= i1; i++) {
        const b = bars[i], cx = i * cwid - V.x0 + cwid / 2;
        if (cwid >= 44) for (let j = 0; j < n; j++) {
          const f = b.footer[j]; if (f == null) continue; const o = typeof f === 'object' ? f : {text: f};
          ctx.fillStyle = o.color || col.text; ctx.fillText(fmt(o.text), cx, ch + 11 + 16 * j);
        }
        if (b.group || i % every === 0) {ctx.fillStyle = b.group ? col.text : col.muted; ctx.fillText((b.group ? b.group + ' ' : '') + b.label, cx, ch + 11 + 16 * n)}
      }
      ctx.restore();

      // price axis
      ctx.fillStyle = col.panel; ctx.fillRect(cw, 0, AXIS, H); ctx.strokeStyle = col.grid; ctx.beginPath(); ctx.moveTo(cw + .5, 0); ctx.lineTo(cw + .5, H); ctx.stroke();
      ctx.fillStyle = col.muted; ctx.textAlign = 'left';
      const m = Math.max(1, Math.ceil(30 / rowH));
      for (let k = Math.ceil(pBot / R); k <= Math.floor(V.top / R); k++) {if (k % m === 0) {const y = yP(k * R); if (y > 6 && y < ch - 4) ctx.fillText((k * R).toFixed(dec), cw + 6, y)} }
      for (let j = 0; j < n; j++)ctx.fillText(labels[j], cw + 6, ch + 11 + 16 * j);
      ctx.fillText('Time', cw + 6, ch + 11 + 16 * n);


      // hover readout (displays the row's own values; no calculation)
      const hr = hover && bars[hover.i] && bars[hover.i].map[hover.k];
      if (hr) {
        const r = hr, y = yP(r.price);
        if (y > 0 && y < ch) {ctx.fillStyle = col.text; ctx.fillRect(cw + 1, y - 9, AXIS - 1, 18); ctx.fillStyle = col.panel; ctx.fillText(r.price.toFixed(dec), cw + 6, y)}
        const t = `${r.price.toFixed(dec)}   ${fmt(r.bid)} × ${fmt(r.ask)}${r.tip ? '   ' + r.tip : ''}`, tw = ctx.measureText(t).width + 16;
        ctx.fillStyle = col.text; ctx.globalAlpha = .9; ctx.fillRect(8, 8, tw, 22); ctx.globalAlpha = 1; ctx.fillStyle = col.panel; ctx.fillText(t, 16, 19);
      }
    }
    let q = 0; const redraw = () => {q || (q = requestAnimationFrame(() => {q = 0; draw()}))};
    function size() {W = wrap.clientWidth; H = wrap.clientHeight}
    function zoomY(f, y) {if (!bars.length) return; const a = V.top - y / V.ppu; V.ppu = clamp(V.ppu * f, .3 / R, 32 / R); V.top = a + y / V.ppu; limits(); redraw()}
    function zoomX(f, x) {if (!bars.length) return; const u = (V.x0 + x) / V.colW; V.colW = clamp(V.colW * f, 24, 220); V.x0 = u * V.colW - x; limits(); redraw()}

    const P = new Map();
    const pt = e => {const r = cv.getBoundingClientRect(); return {x: e.clientX - r.left, y: e.clientY - r.top}};
    function setHover(p) {
      hover = null; if (!bars.length) return;
      const cw = W - AXIS, ch = H - FOOT; if (p.x >= cw || p.y >= ch) return;
      const i = Math.floor((p.x + V.x0) / V.colW); if (i < 0 || i >= bars.length) return;
      const k = Math.round((V.top - p.y / V.ppu) / R); if (bars[i].map[k]) hover = {i, k};
    }
    cv.addEventListener('pointerdown', e => {cv.setPointerCapture(e.pointerId); P.set(e.pointerId, pt(e))});
    cv.addEventListener('pointermove', e => {
      const p = pt(e);
      if (!P.has(e.pointerId)) {setHover(p); redraw(); return }
      if (P.size === 2) {
        const [a, b] = [...P.values()]; P.set(e.pointerId, p);
        const [c, d] = [...P.values()], dx0 = Math.abs(a.x - b.x), dy0 = Math.abs(a.y - b.y), dx1 = Math.abs(c.x - d.x), dy1 = Math.abs(c.y - d.y);
        if (dy0 > 20 && dy1 > 20) zoomY(dy1 / dy0, (c.y + d.y) / 2);
        if (dx0 > 20 && dx1 > 20) zoomX(dx1 / dx0, (c.x + d.x) / 2);
        return;
      }
      if (!bars.length) return;
      const o = P.get(e.pointerId); V.x0 -= p.x - o.x; V.top += (p.y - o.y) / V.ppu; P.set(e.pointerId, p); limits(); hover = null; redraw();
    });
    const up = e => {P.delete(e.pointerId); if (e.pointerType !== 'mouse') {hover = null; redraw()} };
    cv.addEventListener('pointerup', up); cv.addEventListener('pointercancel', up);
    cv.addEventListener('pointerleave', () => {if (!P.size) {hover = null; redraw()} });
    cv.addEventListener('wheel', e => {
      e.preventDefault(); if (!bars.length) return;
      const p = pt(e), d = e.deltaY !== 0 ? e.deltaY : e.deltaX, f = Math.exp(-d * .0015);
      // Shift / Ctrl / Alt + wheel = horizontal (time) zoom. Plain wheel = vertical
      // (price) zoom. Horizontal trackpad scroll = pan. Some browsers turn Shift+wheel
      // into a horizontal scroll (deltaY = 0), so fall back to deltaX for the zoom step.
      if (e.shiftKey || e.ctrlKey || e.metaKey || e.altKey) zoomX(f, p.x);
      else if (Math.abs(e.deltaX) > Math.abs(e.deltaY)) {V.x0 += e.deltaX; limits(); redraw()}
      else zoomY(f, p.y);
    }, {passive: false});
    window.addEventListener('keydown', e => {
      const t = e.target;
      if (!bars.length || e.ctrlKey || e.metaKey || e.altKey ||
          (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable))) return;
      if (e.key === '+' || e.key === '=') {zoomXCentre(1.25); e.preventDefault()}
      else if (e.key === '-' || e.key === '_') {zoomXCentre(.8); e.preventDefault()}
      else if (e.key === '0') {showAll(); e.preventDefault()}
    });
    cv.addEventListener('dblclick', () => {fit(); limits(); redraw()});
    new ResizeObserver(() => {size(); limits(); redraw()}).observe(wrap);
    matchMedia('(prefers-color-scheme: dark)').addEventListener('change', redraw);

    const msg = document.getElementById('msg'); let mt = 0;
    function toast(t) {msg.textContent = t; msg.style.display = t ? 'block' : 'none'; clearTimeout(mt); if (t) mt = setTimeout(() => msg.style.display = 'none', 6000)}
    // ---- Public API for the host application -------------------------------------------------
    // Each call accepts an object or a JSON string and returns {ok:true} or {ok:false,error:"..."}.
    const parse = d => typeof d === 'string' ? JSON.parse(d) : d;
    const api = fn => (...a) => {try {fn(...a); return {ok: true}} catch (e) {console.error(e); toast(e.message); return {ok: false, error: e.message}} };
    function showAll() {                               // fit every bar in the window (columns may get very narrow)
      if (!bars.length) return false;
      size();
      const available = Math.max(AXIS + 60, W - AXIS);
      V.colW = clamp(available / bars.length, 4, 220);
      V.x0 = 0;
      fit(); limits(); redraw();
      return true;
    }
    function showLatest(count) {                       // frame the latest bars at a readable width
      if (!bars.length) return false;
      size();
      const available = Math.max(AXIS + 60, W - AXIS);
      const n = Math.max(1, Math.min(bars.length, count || 12));
      V.colW = clamp(available / n, 60, 220);
      V.x0 = Math.max(0, bars.length * V.colW - available + 30);
      fit(); limits(); redraw();
      return true;
    }
    function zoomXCentre(f) {if (bars.length) zoomX(f, (W - AXIS) / 2)}
    function zoomYCentre(f) {if (bars.length) zoomY(f, (H - FOOT) / 2)}
    window.footprint = {
      load: api(d => load(parse(d))),                 // full snapshot: {rowSize, colors, footerLabels, bars:[...]}
      updateLast: api(b => put(parse(b), false)),      // replace the last (live) bar with a rebuilt version
      append: api(b => put(parse(b), true)),           // start a new bar after the last one
      showAll: api(() => showAll()),                   // fit every bar in the window
      showLatest: api(n => showLatest(n)),             // frame the latest N bars at readable width
      zoomX: api(f => zoomXCentre(+f || 1.25)),        // horizontal (time) zoom
      zoomY: api(f => zoomYCentre(+f || 1.25))         // vertical (price) zoom
    };

    size(); draw();
  
