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

    const consoleModels = {
        2: {columns: 80, rows: 24, terminalType: 'IBM-3278-2-E'},
        3: {columns: 80, rows: 32, terminalType: 'IBM-3278-3-E'},
        4: {columns: 80, rows: 43, terminalType: 'IBM-3278-4-E'},
        5: {columns: 132, rows: 27, terminalType: 'IBM-3278-5-E'},
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
        if (cell.protected && cell.highlight) return '#FFFFFF';
        if (cell.protected) return '#3399FF';
        if (cell.highlight) return '#FF4444';
        return '#33FF33';
    }

    function foregroundColor(cell) {
        if (cell.hidden) return '#000000';
        // Extended foreground (0xF1-0xFF) wins over the base palette.
        if (cell.foreground >= 0xF1 && cell.foreground <= 0xFF) {
            return COLOR_3270[cell.foreground] || baseColor(cell);
        }
        return baseColor(cell);
    }

    function backgroundColor(cell) {
        if (cell.background >= 0xF1 && cell.background <= 0xFF) {
            return COLOR_3270[cell.background] || '#000000';
        }
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

    function setStatus(text, cls) {
        statusEl.textContent = text;
        statusEl.className = cls;
    }

    function emptySnapshot() {
        const cells = new Array(rows * columns);
        for (let i = 0; i < cells.length; i++) {
            cells[i] = {
                glyph: ' ',
                protected: true,
                hidden: false,
                highlight: false,
                modified: false,
                foreground: 0,
                background: 0,
                highlightAttr: 0
            };
        }
        return {rows, columns, cursor: 0, keyboardLocked: true, alarm: false, cells, fields: []};
    }

    function resizeCanvas() {
        canvas.width = window.innerWidth;
        canvas.height = window.innerHeight - 40;
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
            if (ch !== ' ') {
                ctx.fillText(ch, cx + cellWidth / 2, cy + cellHeight / 2);
            }

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

        if (cursorBlink) {
            const cx = (snap.cursor % snap.columns) * cellWidth;
            const cy = Math.floor(snap.cursor / snap.columns) * cellHeight;
            ctx.fillStyle = snap.keyboardLocked ? 'rgba(255,80,80,0.55)' : 'rgba(255,255,255,0.55)';
            ctx.fillRect(cx, cy + cellHeight - 4, cellWidth, 4);
        }
    }

    setInterval(() => {
        cursorBlink = !cursorBlink;
        render();
    }, 500);

    function applySnapshot(s) {
        snapshot = s;
        if (s.rows !== rows || s.columns !== columns) {
            rows = s.rows;
            columns = s.columns;
            resizeCanvas();
        } else {
            render();
        }
        if (s.alarm) tryBeep();
    }

    function tryBeep() {
        try {
            const ac = new (window.AudioContext || window.webkitAudioContext)();
            const o = ac.createOscillator();
            o.type = 'sine';
            o.frequency.value = 880;
            o.connect(ac.destination);
            o.start();
            o.stop(ac.currentTime + 0.08);
        } catch { /* ignore */
        }
    }

    // ---- SignalR transport -------------------------------------------------

    async function connect() {
        if (connection) await disconnect();

        const choice = consoleModels[parseInt(modelEl.value, 10)];
        model = choice;
        rows = choice.rows;
        columns = choice.columns;
        snapshot = emptySnapshot();
        resizeCanvas();

        connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/terminal')
            .withAutomaticReconnect()
            .build();

        connection.on('ScreenUpdate', applySnapshot);
        connection.on('Connected', () => setStatus('connected', 'connected'));
        connection.on('Disconnected', (msg) => {
            setStatus(`disconnected: ${msg}`, 'disconnected');
            cleanup();
        });
        connection.on('Error', (msg) => setStatus(`error: ${msg}`, 'error'));

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
        if (!connection) return;
        try {
            await connection.invoke('Disconnect');
        } catch { /* ignore */
        }
        try {
            await connection.stop();
        } catch { /* ignore */
        }
        cleanup();
    }

    function cleanup() {
        connection = null;
        connectBtn.disabled = false;
        disconnectBtn.disabled = true;
    }

    function sendKey(payload) {
        if (!connection || connection.state !== signalR.HubConnectionState.Connected) return;
        connection.invoke('SendKey', payload).catch(() => { /* ignore */
        });
    }

    // ---- Keyboard ----------------------------------------------------------

    const aidKeys = new Set(['Enter', 'Clear', 'PA1', 'PA2', 'PA3',
        'PF1', 'PF2', 'PF3', 'PF4', 'PF5', 'PF6', 'PF7', 'PF8', 'PF9', 'PF10', 'PF11', 'PF12',
        'PF13', 'PF14', 'PF15', 'PF16', 'PF17', 'PF18', 'PF19', 'PF20', 'PF21', 'PF22', 'PF23', 'PF24']);

    function functionKeyName(event) {
        if (event.key === 'Enter') return 'Enter';
        if (event.key === 'Escape') return 'Clear';
        if (event.key.startsWith('F') && /^F([1-9]|1[0-9]|2[0-4])$/.test(event.key)) {
            const n = parseInt(event.key.slice(1), 10);
            return event.shiftKey ? `PF${n + 12}` : `PF${n}`;
        }
        return null;
    }

    document.addEventListener('keydown', (event) => {
        if (document.activeElement && ['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement.tagName)) return;
        if (!connection) return;

        const aid = functionKeyName(event);
        if (aid) {
            event.preventDefault();
            sendKey({kind: 'Aid', value: aid});
            return;
        }

        if (event.key === 'Tab') {
            event.preventDefault();
            sendKey({kind: 'Tab'});
            return;
        }

        if (event.key === 'Backspace') {
            event.preventDefault();
            sendKey({kind: 'Backspace'});
            return;
        }

        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight'
            || event.key === 'ArrowUp' || event.key === 'ArrowDown') {
            if (!snapshot) return;
            event.preventDefault();
            let addr = snapshot.cursor;
            if (event.key === 'ArrowLeft') addr = (addr - 1 + snapshot.cells.length) % snapshot.cells.length;
            if (event.key === 'ArrowRight') addr = (addr + 1) % snapshot.cells.length;
            if (event.key === 'ArrowUp') addr = (addr - snapshot.columns + snapshot.cells.length) % snapshot.cells.length;
            if (event.key === 'ArrowDown') addr = (addr + snapshot.columns) % snapshot.cells.length;
            sendKey({kind: 'Cursor', address: addr});
            return;
        }

        if (event.key.length === 1 && !event.ctrlKey && !event.metaKey) {
            event.preventDefault();
            sendKey({kind: 'Type', value: event.key});
        }
    });

    canvas.addEventListener('click', (event) => {
        if (!snapshot) return;
        const rect = canvas.getBoundingClientRect();
        const x = Math.floor((event.clientX - rect.left) / cellWidth);
        const y = Math.floor((event.clientY - rect.top) / cellHeight);
        const addr = y * snapshot.columns + x;
        sendKey({kind: 'Cursor', address: addr});
    });

    document.querySelectorAll('.aid-bar button').forEach(btn => {
        btn.addEventListener('click', () => sendKey({kind: 'Aid', value: btn.dataset.aid}));
    });
    connectBtn.addEventListener('click', connect);
    disconnectBtn.addEventListener('click', disconnect);
    window.addEventListener('resize', resizeCanvas);

    // bootstrap
    snapshot = emptySnapshot();
    resizeCanvas();
    setStatus('disconnected', 'disconnected');
})();
