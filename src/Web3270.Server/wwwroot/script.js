/* eslint-disable no-undef */
// web3270 — canvas-based TN3270 terminal driven by an ASP.NET Core SignalR hub.

(() => {
    const canvas = document.getElementById('terminal');
    const ctx = canvas.getContext('2d');
    const statusEl = document.getElementById('status');
    const hostEl = document.getElementById('host');
    const portEl = document.getElementById('port');
    const modelEl = document.getElementById('model');
    const connectBtn = document.getElementById('connect');
    const disconnectBtn = document.getElementById('disconnect');
    const ruleToggleBtn = document.getElementById('ruleToggle');
    const cursorPosEl = document.getElementById('cursorPos');

    const consoleModels = {
        2: { columns: 80, rows: 24, terminalType: 'IBM-3278-2-E' },
        3: { columns: 80, rows: 32, terminalType: 'IBM-3278-3-E' },
        4: { columns: 80, rows: 43, terminalType: 'IBM-3278-4-E' },
        5: { columns: 132, rows: 27, terminalType: 'IBM-3278-5-E' },
    };

    // Extended-color palette (set by SFE / SA — only present when host opts in).
    const COLOR_3270 = {
        0xF0: '#000000', // neutral / black background
        0xF1: '#3399FF', // blue
        0xF2: '#FF4444', // red
        0xF3: '#FF66CC', // pink
        0xF4: '#33FF33', // green
        0xF5: '#33FFFF', // turquoise
        0xF6: '#FFFF44', // yellow
        0xF7: '#FFFFFF', // white
        0xF8: '#000000', // black
        0xF9: '#000080',
        0xFA: '#FF8800',
        0xFB: '#9966FF',
        0xFC: '#99FF99',
        0xFD: '#99FFFF',
        0xFE: '#999999',
        0xFF: '#FFFFFF',
    };

    // Base 3279 palette used when no extended color is present, derived from
    // the basic field-attribute (protected + intensity).
    //   protected, normal    -> blue
    //   protected, high      -> white
    //   unprotected, normal  -> green
    //   unprotected, high    -> red
    function baseColor(cell) {
        if (cell.protected && cell.highlight)
            return '#FFFFFF';
        if (cell.protected)
            return '#3399FF';
        if (cell.highlight)
            return '#FF4444';
        return '#33FF33';
    }

    function foregroundColor(cell) {
        if (cell.hidden)
            return '#000000';
        // Extended foreground (0xF1-0xFF) wins over the base palette.
        if (cell.foreground >= 0xF1 && cell.foreground <= 0xFF)
            return COLOR_3270[cell.foreground] || baseColor(cell);
        return baseColor(cell);
    }

    function backgroundColor(cell) {
        if (cell.background >= 0xF1 && cell.background <= 0xFF)
            return COLOR_3270[cell.background] || '#000000';
        return '#000000';
    }

    let model = consoleModels[2];
    let columns = model.columns;
    let rows = model.rows;
    let snapshot = null;
    let cursorBlink = true;
    let cellWidth = 0;
    let cellHeight = 0;
    let optimalFontSize = 16;
    let connection = null;
    let ruleEnabled = false;

    // ---- Selection state ---------------------------------------------------
    // Selection coordinates are stored in normalised cell space (0..rows,
    // 0..columns). `selection` is null when nothing is highlighted.
    let selection = null;          // { row1, col1, row2, col2 }  inclusive
    let dragOrigin = null;         // {row, col} captured on mousedown
    let dragMoved = false;         // true once the user actually moved while holding
    const DRAG_PIXEL_THRESHOLD = 3;

    function setStatus(text, cls) {
        statusEl.textContent = text;
        statusEl.className = cls;
    }

    function flashStatus(text, cls, ms = 1500) {
        const prevText = statusEl.textContent;
        const prevCls = statusEl.className;
        setStatus(text, cls);
        setTimeout(() => setStatus(prevText, prevCls), ms);
    }

    function emptySnapshot() {
        const cells = new Array(rows * columns);
        for (let i = 0; i < cells.length; i++) {
            cells[i] = { glyph: ' ', protected: true, hidden: false, highlight: false, modified: false, foreground: 0, background: 0, highlightAttr: 0 };
        }
        return { rows, columns, cursor: 0, keyboardLocked: true, alarm: false, cells, fields: [] };
    }

    function resizeCanvas() {
        // Use the canvas's actual rendered size (driven by flex layout)
        // instead of viewport math — the toolbar can wrap and grow, and
        // the canvas needs to honour exactly what CSS gave it.
        const rect = canvas.getBoundingClientRect();
        canvas.width = Math.max(1, Math.floor(rect.width));
        canvas.height = Math.max(1, Math.floor(rect.height));
        cellWidth = canvas.width / columns;
        cellHeight = canvas.height / rows;
        optimalFontSize = computeFontSize();
        render();
    }

    function computeFontSize() {
        let size = 1;
        ctx.font = `${size}px monospace`;
        while (ctx.measureText('M').width < cellWidth - 1 && size < cellHeight - 1) {
            size++;
            ctx.font = `${size}px monospace`;
        }
        return Math.max(8, size - 1);
    }

    // ---- Mouse → cell mapping ---------------------------------------------

    function cellAtMouse(event) {
        const rect = canvas.getBoundingClientRect();
        const col = Math.max(0, Math.min(columns - 1,
            Math.floor((event.clientX - rect.left) / cellWidth)));
        const row = Math.max(0, Math.min(rows - 1,
            Math.floor((event.clientY - rect.top) / cellHeight)));
        return { row, col };
    }

    function normaliseSelection(o, e) {
        return {
            row1: Math.min(o.row, e.row),
            col1: Math.min(o.col, e.col),
            row2: Math.max(o.row, e.row),
            col2: Math.max(o.col, e.col),
        };
    }

    // True when `addr` lies inside the cyclic field [start, start+length).
    // 3270 buffers wrap around, so a field that starts near the end of the
    // buffer can extend through position 0.
    function containsAddress(field, addr, size) {
        if (field.length <= 0)
            return false;
        const end = (field.start + field.length) % size;
        if (field.start <= end)
            return addr >= field.start && addr < end;
        return addr >= field.start || addr < end;
    }

    function isCellSelected(row, col) {
        if (!selection)
            return false;
        return row >= selection.row1 && row <= selection.row2
            && col >= selection.col1 && col <= selection.col2;
    }

    function clearSelection() {
        if (selection === null)
            return;
        selection = null;
        render();
    }

    // ---- Render ------------------------------------------------------------

    function render() {
        const snap = snapshot ?? emptySnapshot();

        ctx.fillStyle = '#000';
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        ctx.font = `${optimalFontSize}px monospace`;
        ctx.textBaseline = 'middle';
        ctx.textAlign = 'center';

        for (let i = 0; i < snap.cells.length; i++) {
            const cell = snap.cells[i];
            const x = i % snap.columns;
            const y = Math.floor(i / snap.columns);
            const cx = x * cellWidth;
            const cy = y * cellHeight;

            const bg = backgroundColor(cell);
            if (bg !== '#000000') {
                ctx.fillStyle = bg;
                ctx.fillRect(cx, cy, cellWidth + 0.5, cellHeight + 0.5);
            }

            const fg = foregroundColor(cell);
            ctx.fillStyle = fg;

            const ch = cell.hidden ? ' ' : (cell.glyph || ' ');
            if (ch !== ' ')
                ctx.fillText(ch, cx + cellWidth / 2, cy + cellHeight / 2);

            if (cell.highlightAttr === 0xF4 && !cell.hidden) {
                ctx.strokeStyle = fg;
                ctx.lineWidth = 1;
                ctx.beginPath();
                ctx.moveTo(cx, cy + cellHeight - 2);
                ctx.lineTo(cx + cellWidth, cy + cellHeight - 2);
                ctx.stroke();
            }
            if (cell.highlightAttr === 0xF2 && !cell.hidden) {
                ctx.fillStyle = fg;
                ctx.globalAlpha = 0.25;
                ctx.fillRect(cx, cy, cellWidth, cellHeight);
                ctx.globalAlpha = 1;
            }
        }

        // Selection overlay — drawn on top of cell content so it's visible
        // regardless of foreground colour. Uses a high-contrast translucent
        // blue similar to native text-selection in macOS.
        if (selection) {
            ctx.fillStyle = 'rgba(80, 145, 255, 0.35)';
            const x1 = selection.col1 * cellWidth;
            const y1 = selection.row1 * cellHeight;
            const w = (selection.col2 - selection.col1 + 1) * cellWidth;
            const h = (selection.row2 - selection.row1 + 1) * cellHeight;
            ctx.fillRect(x1, y1, w, h);
            ctx.strokeStyle = 'rgba(80, 145, 255, 0.95)';
            ctx.lineWidth = 1;
            ctx.strokeRect(x1 + 0.5, y1 + 0.5, w - 1, h - 1);
        }

        // Rule: thin cross-hair through the cursor row/column. No
        // translucent halo — just two 1px lines for precision alignment,
        // x3270-style.
        if (ruleEnabled) {
            const cursorCol = snap.cursor % snap.columns;
            const cursorRow = Math.floor(snap.cursor / snap.columns);
            ctx.strokeStyle = 'rgba(120, 200, 255, 0.65)';
            ctx.lineWidth = 1;
            const yMid = Math.round(cursorRow * cellHeight + cellHeight / 2) + 0.5;
            const xMid = Math.round(cursorCol * cellWidth + cellWidth / 2) + 0.5;
            ctx.beginPath();
            ctx.moveTo(0, yMid);
            ctx.lineTo(canvas.width, yMid);
            ctx.moveTo(xMid, 0);
            ctx.lineTo(xMid, canvas.height);
            ctx.stroke();
        }

        if (cursorBlink) {
            const cx = (snap.cursor % snap.columns) * cellWidth;
            const cy = Math.floor(snap.cursor / snap.columns) * cellHeight;
            ctx.fillStyle = snap.keyboardLocked ? 'rgba(255,80,80,0.55)' : 'rgba(255,255,255,0.55)';
            ctx.fillRect(cx, cy + cellHeight - 4, cellWidth, 4);
        }
    }

    setInterval(() => { cursorBlink = !cursorBlink; render(); }, 500);

    let lastAlarm = false;
    function applySnapshot(s) {
        snapshot = s;
        if (s.rows !== rows || s.columns !== columns) {
            rows = s.rows;
            columns = s.columns;
            resizeCanvas();
        } else {
            render();
        }
        updateCursorPos();
        // Edge-trigger only: defensive against any stuck-true state from
        // the wire (the server already consumes the flag, but belt-and-
        // suspenders avoids the beep loop reported by the user).
        if (s.alarm && !lastAlarm)
            tryBeep();
        lastAlarm = !!s.alarm;
    }

    // Cursor position display — 1-indexed row/col following 3270 convention.
    // Padded to fixed width so the toolbar layout doesn't dance around as
    // the cursor moves.
    function updateCursorPos() {
        if (!snapshot) {
            cursorPosEl.textContent = '—';
            return;
        }
        const row = Math.floor(snapshot.cursor / snapshot.columns) + 1;
        const col = (snapshot.cursor % snapshot.columns) + 1;
        cursorPosEl.textContent =
            `R ${String(row).padStart(2, '0')}  C ${String(col).padStart(3, '0')}`;
    }

    // Single shared AudioContext reused across beeps — creating one per
    // beep leaks resources fast and Safari eventually stalls audio.
    let audioCtx = null;
    function tryBeep() {
        try {
            if (!audioCtx) {
                const Ctor = window.AudioContext || window.webkitAudioContext;
                if (!Ctor)
                    return;
                audioCtx = new Ctor();
            }
            // Some browsers leave the context suspended until a user
            // gesture; resume on demand.
            if (audioCtx.state === 'suspended')
                audioCtx.resume().catch(() => { /* ignore */ });

            const o = audioCtx.createOscillator();
            const g = audioCtx.createGain();
            o.type = 'sine';
            o.frequency.value = 880;
            // Quick attack / decay to avoid the click noise of a hard stop.
            const t = audioCtx.currentTime;
            g.gain.setValueAtTime(0.0001, t);
            g.gain.exponentialRampToValueAtTime(0.4, t + 0.005);
            g.gain.exponentialRampToValueAtTime(0.0001, t + 0.12);
            o.connect(g).connect(audioCtx.destination);
            o.start(t);
            o.stop(t + 0.13);
        } catch { /* ignore */ }
    }

    // ---- Selection text extraction ----------------------------------------
    // Walks the selection rectangle row-by-row, collecting visible glyphs.
    // Hidden cells (password fields) are emitted as a single space so we
    // never leak credentials through copy. Trailing spaces per row are
    // trimmed — that's how every terminal client behaves.
    function selectionToText() {
        if (!selection || !snapshot)
            return '';
        const lines = [];
        for (let r = selection.row1; r <= selection.row2; r++) {
            let line = '';
            for (let c = selection.col1; c <= selection.col2; c++) {
                const cell = snapshot.cells[r * snapshot.columns + c];
                if (!cell)
                    continue;
                line += cell.hidden ? ' ' : (cell.glyph || ' ');
            }
            lines.push(line.replace(/\s+$/, ''));
        }
        return lines.join('\n');
    }

    async function copySelection() {
        const text = selectionToText();
        if (!text) {
            flashStatus('nothing selected', 'error');
            return;
        }
        try {
            await navigator.clipboard.writeText(text);
            flashStatus(`copied ${text.length} chars`, 'connected');
        } catch {
            // Fallback: execCommand via a hidden textarea (Safari, older browsers)
            const ta = document.createElement('textarea');
            ta.value = text;
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select();
            try {
                document.execCommand('copy');
                flashStatus(`copied ${text.length} chars`, 'connected');
            } catch {
                flashStatus('copy failed', 'error');
            } finally {
                document.body.removeChild(ta);
            }
        }
    }

    async function pasteFromClipboard() {
        if (!connection)
            return;
        try {
            const text = await navigator.clipboard.readText();
            if (!text)
                return;
            // Strip newlines and tabs — TN3270 input fields are linear and
            // don't have a meaningful concept of multi-line paste; sending
            // them as Type would type literal control chars.
            const cleaned = text.replace(/[\r\n\t]+/g, ' ');
            sendKey({ kind: 'Type', value: cleaned });
        } catch {
            flashStatus('paste blocked', 'error');
        }
    }

    function selectAll() {
        if (!snapshot)
            return;
        selection = { row1: 0, col1: 0, row2: snapshot.rows - 1, col2: snapshot.columns - 1 };
        render();
    }

    // ---- SignalR transport -------------------------------------------------

    async function connect() {
        if (connection)
            await disconnect();

        const choice = consoleModels[parseInt(modelEl.value, 10)];
        model = choice;
        rows = choice.rows;
        columns = choice.columns;
        snapshot = emptySnapshot();
        clearSelection();
        resizeCanvas();

        connection = new signalR.HubConnectionBuilder()
            .withUrl('http://localhost:5001/hubs/terminal')
            .withAutomaticReconnect()
            .build();

        connection.on('ScreenUpdate', applySnapshot);
        connection.on('Connected',    () => setStatus('connected', 'connected'));
        connection.on('Disconnected', (msg) => { setStatus(`disconnected: ${msg}`, 'disconnected'); cleanup(); });
        connection.on('Error',        (msg) => setStatus(`error: ${msg}`, 'error'));

        setStatus('connecting…', 'connecting');
        try {
            await connection.start();
            await connection.invoke('Connect', {
                host: hostEl.value.trim(),
                port: parseInt(portEl.value, 10),
                terminalType: choice.terminalType,
                rows: choice.rows,
                columns: choice.columns,
            });
            connectBtn.disabled = true;
            disconnectBtn.disabled = false;
        } catch (err) {
            setStatus(`error: ${err}`, 'error');
            cleanup();
        }
    }

    async function disconnect() {
        if (!connection)
            return;
        try { await connection.invoke('Disconnect'); } catch { /* ignore */ }
        try { await connection.stop(); } catch { /* ignore */ }
        cleanup();
    }

    function cleanup() {
        connection = null;
        connectBtn.disabled = false;
        disconnectBtn.disabled = true;
    }

    function sendKey(payload) {
        if (!connection || connection.state !== signalR.HubConnectionState.Connected)
            return;
        connection.invoke('SendKey', payload).catch(() => { /* ignore */ });
    }

    // ---- Keyboard ----------------------------------------------------------

    // Maps a browser KeyboardEvent to a 3270 AID name. Conventions follow
    // ISPF / TSO defaults so paging keys feel natural in real workflows:
    //   PageUp   → PF7  (scroll up)
    //   PageDown → PF8  (scroll down)
    //   Escape   → Clear
    //   F1..F12  → PF1..PF12   (Shift = PF13..PF24)
    function functionKeyName(event) {
        if (event.key === 'Enter')
            return 'Enter';
        if (event.key === 'Escape')
            return 'Clear';
        if (event.key === 'PageUp')
            return 'PF7';
        if (event.key === 'PageDown')
            return 'PF8';
        if (event.key.startsWith('F') && /^F([1-9]|1[0-9]|2[0-4])$/.test(event.key)) {
            const n = parseInt(event.key.slice(1), 10);
            return event.shiftKey ? `PF${n + 12}` : `PF${n}`;
        }
        return null;
    }

    document.addEventListener('keydown', async (event) => {
        if (document.activeElement && ['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement.tagName))
            return;

        const mod = event.metaKey || event.ctrlKey;

        // Clipboard shortcuts work even when offline (copy is local).
        if (mod && event.key.toLowerCase() === 'c') {
            event.preventDefault();
            await copySelection();
            return;
        }
        if (mod && event.key.toLowerCase() === 'v') {
            event.preventDefault();
            await pasteFromClipboard();
            return;
        }
        if (mod && event.key.toLowerCase() === 'a') {
            event.preventDefault();
            selectAll();
            return;
        }

        if (event.key === 'Escape' && selection) {
            event.preventDefault();
            clearSelection();
            return;
        }

        if (!connection)
            return;

        const aid = functionKeyName(event);
        if (aid) {
            event.preventDefault();
            sendKey({ kind: 'Aid', value: aid });
            return;
        }

        if (event.key === 'Tab') {
            event.preventDefault();
            sendKey({ kind: 'Tab' });
            return;
        }

        if (event.key === 'Backspace') {
            event.preventDefault();
            sendKey({ kind: 'Backspace' });
            return;
        }

        if (event.key === 'Home') {
            // Jump the terminal cursor to the first unprotected field's
            // first content position — equivalent to x3270's Home() action.
            event.preventDefault();
            if (!snapshot)
                return;
            const target = snapshot.fields.find(f => !f.protected);
            if (target)
                sendKey({ kind: 'Cursor', address: target.start });
            return;
        }

        if (event.key === 'End') {
            // Move to the end of the current input field (one past the
            // last typed char), useful for quick edits.
            event.preventDefault();
            if (!snapshot)
                return;
            const here = snapshot.cursor;
            const field = snapshot.fields.find(f =>
                !f.protected && containsAddress(f, here, snapshot.cells.length));
            if (field) {
                // Walk forward in the field until we hit a null/space sequence
                // that runs to the end — that's where the user's text stops.
                let last = field.start;
                let p = field.start;
                for (let n = 0; n < field.length; n++) {
                    const cell = snapshot.cells[p];
                    if (cell && cell.glyph && cell.glyph !== ' ' && cell.glyph !== ' ')
                        last = (p + 1) % snapshot.cells.length;
                    p = (p + 1) % snapshot.cells.length;
                }
                sendKey({ kind: 'Cursor', address: last });
            }
            return;
        }

        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight'
         || event.key === 'ArrowUp'   || event.key === 'ArrowDown') {
            if (!snapshot)
                return;
            event.preventDefault();
            let addr = snapshot.cursor;
            if (event.key === 'ArrowLeft')
                addr = (addr - 1 + snapshot.cells.length) % snapshot.cells.length;
            if (event.key === 'ArrowRight')
                addr = (addr + 1) % snapshot.cells.length;
            if (event.key === 'ArrowUp')
                addr = (addr - snapshot.columns + snapshot.cells.length) % snapshot.cells.length;
            if (event.key === 'ArrowDown')
                addr = (addr + snapshot.columns) % snapshot.cells.length;
            sendKey({ kind: 'Cursor', address: addr });
            return;
        }

        if (event.key.length === 1 && !mod) {
            event.preventDefault();
            sendKey({ kind: 'Type', value: event.key });
        }
    });

    // ---- Mouse: selection + cursor placement ------------------------------

    canvas.addEventListener('mousedown', (event) => {
        if (event.button !== 0)
            return;            // ignore middle/right
        if (!snapshot)
            return;
        dragOrigin = cellAtMouse(event);
        dragMoved = false;
        // Tentatively start a 1x1 selection so the user gets immediate
        // visual feedback. If they click without moving it gets cleared
        // in mouseup and the cursor is placed instead.
        selection = normaliseSelection(dragOrigin, dragOrigin);
        render();
    });

    canvas.addEventListener('mousemove', (event) => {
        if (!dragOrigin)
            return;
        const cell = cellAtMouse(event);
        if (cell.row !== dragOrigin.row || cell.col !== dragOrigin.col)
            dragMoved = true;
        selection = normaliseSelection(dragOrigin, cell);
        render();
    });

    document.addEventListener('mouseup', (event) => {
        if (!dragOrigin)
            return;
        const wasDrag = dragMoved;
        const click = dragOrigin;
        dragOrigin = null;
        if (!wasDrag) {
            // pure click — clear any selection and move the terminal cursor
            selection = null;
            if (snapshot) {
                const addr = click.row * snapshot.columns + click.col;
                sendKey({ kind: 'Cursor', address: addr });
            }
            render();
        }
        // else: keep the selection visible until the next click or Escape.
        // event might be on document, ignore intentionally.
        void event;
    });

    // ---- Toolbar buttons ---------------------------------------------------
    document.querySelectorAll('.aid-bar button').forEach(btn => {
        btn.addEventListener('click', () => sendKey({ kind: 'Aid', value: btn.dataset.aid }));
    });
    connectBtn.addEventListener('click', connect);
    disconnectBtn.addEventListener('click', disconnect);
    ruleToggleBtn.addEventListener('click', () => {
        ruleEnabled = !ruleEnabled;
        ruleToggleBtn.classList.toggle('active', ruleEnabled);
        render();
    });
    window.addEventListener('resize', resizeCanvas);
    // The toolbar can wrap to extra lines (especially on narrow windows),
    // changing the canvas's CSS-computed height. Resize-observe the canvas
    // itself so we always match its real layout box.
    if ('ResizeObserver' in window) {
        const ro = new ResizeObserver(() => resizeCanvas());
        ro.observe(canvas);
    }

    // bootstrap
    snapshot = emptySnapshot();
    resizeCanvas();
    updateCursorPos();
    setStatus('disconnected', 'disconnected');
})();
