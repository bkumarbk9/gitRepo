/*
  Copyright (C) 2026 Bharath K A (bkumarbk9)
  SPDX-License-Identifier: AGPL-3.0-only

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License version 3
  as published by the Free Software Foundation.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

// Browser client. The server owns the sheet: the client sends edits over a WebSocket and draws
// whatever the server sends back, so every browser on the same sheet shows the same thing.
(() => {
  'use strict';

  const DEFAULT_SHEET = 'test';
  const SHEET_NAME_RULE = /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/;
  const MAX_RECONNECT_DELAY_MS = 15000;
  const IDLE_TIMEOUT_MS = 30 * 60 * 1000;    // disconnect after 30 minutes without use
  const ACTIVITY_SIGNAL_MS = 60 * 1000;      // tell the server "still here" at most once a minute
  const IDLE_CLOSE_CODE = 4000;              // the server's close code for an idle connection

  const el = {
    openForm: document.getElementById('open-form'),
    sheetName: document.getElementById('sheet-name'),
    copyLink: document.getElementById('copy-link'),
    status: document.getElementById('status'),
    statusText: document.getElementById('status-text'),
    users: document.getElementById('users'),
    cellName: document.getElementById('cell-name'),
    formulaInput: document.getElementById('formula-input'),
    gridWrap: document.getElementById('grid-wrap'),
    grid: document.getElementById('grid'),
    message: document.getElementById('message'),
    idlePanel: document.getElementById('idle-panel'),
    idleText: document.getElementById('idle-text'),
    reconnect: document.getElementById('reconnect'),
  };

  const state = {
    sheet: '',
    rows: 0,
    cols: 0,
    cells: new Map(),          // "B2" -> { raw, value, kind }
    computedColumns: new Set(),
    selected: { row: 0, col: 0 },
    editor: null,              // the <input> while a cell is being edited
    socket: null,
    reconnectAttempts: 0,
    reconnectTimer: 0,
    idle: false,               // disconnected for inactivity; waits for the Reconnect button
    draft: null,               // { row, col, text } of an edit that was open when the page went idle
    lastActivity: Date.now(),
    lastActivitySignal: 0,
  };

  let tdGrid = [];             // tdGrid[row][col] -> <td>
  let colHeaders = [];
  let rowHeaders = [];
  let messageTimer = 0;

  // ---- Addresses -----------------------------------------------------------------------------

  function columnName(col) {
    let name = '';
    for (let n = col + 1; n > 0; n = Math.floor((n - 1) / 26)) {
      name = String.fromCharCode(65 + ((n - 1) % 26)) + name;
    }
    return name;
  }

  const address = (row, col) => columnName(col) + (row + 1);

  function parseAddress(text) {
    const match = /^([A-Z]{1,3})([1-9][0-9]*)$/.exec(text);
    if (!match) return null;
    let col = 0;
    for (const ch of match[1]) col = col * 26 + (ch.charCodeAt(0) - 64);
    return { row: Number(match[2]) - 1, col: col - 1 };
  }

  const cellAt = (row, col) => state.cells.get(address(row, col));
  const isComputed = (row, col) => row > 0 && state.computedColumns.has(col);

  // ---- Status and messages -------------------------------------------------------------------

  function setStatus(kind, text) {
    el.status.dataset.state = kind;
    el.statusText.textContent = text;
  }

  function showMessage(text, kind = 'info') {
    el.message.textContent = text;
    el.message.dataset.kind = kind;
    clearTimeout(messageTimer);
    messageTimer = setTimeout(() => { el.message.textContent = ''; }, 5000);
  }

  function setUsers(count) {
    el.users.hidden = !count;
    el.users.textContent = count === 1 ? '1 person viewing' : `${count} people viewing`;
  }

  // ---- Grid ----------------------------------------------------------------------------------

  function buildGrid() {
    el.grid.replaceChildren();
    tdGrid = [];
    colHeaders = [];
    rowHeaders = [];

    const thead = document.createElement('thead');
    const headRow = document.createElement('tr');
    headRow.append(document.createElement('th'));
    for (let col = 0; col < state.cols; col++) {
      const th = document.createElement('th');
      th.scope = 'col';
      th.textContent = columnName(col);
      colHeaders.push(th);
      headRow.append(th);
    }
    thead.append(headRow);

    const tbody = document.createElement('tbody');
    for (let row = 0; row < state.rows; row++) {
      const tr = document.createElement('tr');
      const th = document.createElement('th');
      th.scope = 'row';
      th.textContent = String(row + 1);
      rowHeaders.push(th);
      tr.append(th);

      const tds = [];
      for (let col = 0; col < state.cols; col++) {
        const td = document.createElement('td');
        td.dataset.row = String(row);
        td.dataset.col = String(col);
        tds.push(td);
        tr.append(td);
      }
      tdGrid.push(tds);
      tbody.append(tr);
    }

    el.grid.append(thead, tbody);
  }

  function renderCell(row, col, flash = false) {
    const td = tdGrid[row]?.[col];
    if (!td || td.contains(state.editor)) return;

    const cell = cellAt(row, col);
    td.textContent = cell ? cell.value : '';
    td.title = cell && cell.raw !== cell.value ? cell.raw : '';
    td.classList.toggle('number', cell?.kind === 'number');
    td.classList.toggle('error', cell?.kind === 'error');
    td.classList.toggle('computed', isComputed(row, col));
    td.classList.toggle('header-row', row === 0);

    if (flash) {
      td.classList.remove('flash');
      void td.offsetWidth;  // restart the animation
      td.classList.add('flash');
    }
  }

  function renderAll() {
    for (let row = 0; row < state.rows; row++) {
      for (let col = 0; col < state.cols; col++) renderCell(row, col);
    }
  }

  function select(row, col, { scroll = true } = {}) {
    row = Math.max(0, Math.min(state.rows - 1, row));
    col = Math.max(0, Math.min(state.cols - 1, col));

    const prev = state.selected;
    tdGrid[prev.row]?.[prev.col]?.classList.remove('selected');
    colHeaders[prev.col]?.classList.remove('active');
    rowHeaders[prev.row]?.classList.remove('active');

    state.selected = { row, col };
    const td = tdGrid[row]?.[col];
    if (!td) return;
    td.classList.add('selected');
    colHeaders[col].classList.add('active');
    rowHeaders[row].classList.add('active');
    if (scroll) td.scrollIntoView({ block: 'nearest', inline: 'nearest' });

    el.cellName.textContent = address(row, col);
    updateFormulaBar();
  }

  function updateFormulaBar() {
    const { row, col } = state.selected;
    if (document.activeElement !== el.formulaInput) {
      el.formulaInput.value = cellAt(row, col)?.raw ?? '';
    }
    el.formulaInput.readOnly = isComputed(row, col);
  }

  // ---- Editing -------------------------------------------------------------------------------

  function canEdit(row, col) {
    if (!isOpen()) {
      showMessage('Not connected; edits are paused until the connection is back.', 'error');
      return false;
    }
    if (isComputed(row, col)) {
      showMessage(`${address(row, col)} is calculated by the column formula in ${address(0, col)}.`, 'error');
      return false;
    }
    return true;
  }

  function startEdit(initialText) {
    const { row, col } = state.selected;
    if (state.editor || !canEdit(row, col)) return;

    const td = tdGrid[row][col];
    const input = document.createElement('input');
    input.className = 'cell-editor';
    input.spellcheck = false;
    input.setAttribute('aria-label', `Edit ${address(row, col)}`);
    input.value = initialText ?? cellAt(row, col)?.raw ?? '';

    state.editor = input;
    td.textContent = '';
    td.append(input);
    input.focus();
    input.setSelectionRange(input.value.length, input.value.length);

    input.addEventListener('input', () => { el.formulaInput.value = input.value; });
    input.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        finishEdit(true);
        select(state.selected.row + (event.shiftKey ? -1 : 1), state.selected.col);
      } else if (event.key === 'Tab') {
        event.preventDefault();
        finishEdit(true);
        select(state.selected.row, state.selected.col + (event.shiftKey ? -1 : 1));
      } else if (event.key === 'Escape') {
        event.preventDefault();
        finishEdit(false);
      }
    });
    input.addEventListener('blur', () => finishEdit(true, { refocus: false }));
  }

  function finishEdit(save, { refocus = true } = {}) {
    const input = state.editor;
    if (!input) return;
    state.editor = null;

    const { row, col } = state.selected;
    input.remove();
    if (save) sendEdit(row, col, input.value);
    renderCell(row, col);
    updateFormulaBar();
    if (refocus) el.gridWrap.focus({ preventScroll: true });
  }

  function sendEdit(row, col, value) {
    const key = address(row, col);
    const current = state.cells.get(key)?.raw ?? '';
    if (value === current || !isOpen()) return;

    state.socket.send(JSON.stringify({ type: 'edit', cell: key, value }));

    // Show the typed text straight away; the server's reply replaces it with the computed value
    if (value.trim() === '') {
      state.cells.delete(key);
    } else {
      state.cells.set(key, { raw: value, value: value.startsWith('=') ? '…' : value, kind: 'text' });
    }
    renderCell(row, col);
  }

  // ---- Keyboard and mouse --------------------------------------------------------------------

  el.gridWrap.addEventListener('keydown', (event) => {
    if (state.editor || event.target !== el.gridWrap) return;

    const { row, col } = state.selected;
    const moves = {
      ArrowUp: [-1, 0], ArrowDown: [1, 0], ArrowLeft: [0, -1], ArrowRight: [0, 1],
      PageUp: [-20, 0], PageDown: [20, 0],
    };

    if (moves[event.key]) {
      event.preventDefault();
      const [dr, dc] = moves[event.key];
      select(row + dr, col + dc);
    } else if (event.key === 'Tab') {
      event.preventDefault();
      select(row, col + (event.shiftKey ? -1 : 1));
    } else if (event.key === 'Home') {
      event.preventDefault();
      select(event.ctrlKey ? 0 : row, 0);
    } else if (event.key === 'Enter' || event.key === 'F2') {
      event.preventDefault();
      startEdit();
    } else if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      if (canEdit(row, col)) sendEdit(row, col, '');
      updateFormulaBar();
    } else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
      // Typing a character starts editing and replaces the cell, as in other spreadsheets
      event.preventDefault();
      startEdit(event.key);
    }
  });

  el.grid.addEventListener('mousedown', (event) => {
    const td = event.target.closest('td');
    if (!td || td.contains(state.editor)) return;
    event.preventDefault();
    finishEdit(true, { refocus: false });
    select(Number(td.dataset.row), Number(td.dataset.col), { scroll: false });
    el.gridWrap.focus({ preventScroll: true });
  });

  el.grid.addEventListener('dblclick', (event) => {
    if (event.target.closest('td')) startEdit();
  });

  el.formulaInput.addEventListener('focus', () => {
    const { row, col } = state.selected;
    if (!isComputed(row, col) && !isOpen()) canEdit(row, col);
  });

  el.formulaInput.addEventListener('keydown', (event) => {
    const { row, col } = state.selected;
    if (event.key === 'Enter') {
      event.preventDefault();
      if (canEdit(row, col)) sendEdit(row, col, el.formulaInput.value);
      el.gridWrap.focus({ preventScroll: true });
      select(row + 1, col);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      el.gridWrap.focus({ preventScroll: true });
      updateFormulaBar();
    }
  });

  el.formulaInput.addEventListener('blur', updateFormulaBar);

  // ---- Connection ----------------------------------------------------------------------------

  const isOpen = () => state.socket?.readyState === WebSocket.OPEN;

  function connect() {
    clearTimeout(state.reconnectTimer);
    const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = new WebSocket(`${scheme}//${location.host}/ws?sheet=${encodeURIComponent(state.sheet)}`);
    state.socket = socket;
    setStatus('connecting', state.reconnectAttempts ? 'Reconnecting…' : 'Connecting…');

    socket.addEventListener('open', () => {
      state.reconnectAttempts = 0;
      setStatus('connected', 'Connected');
    });

    socket.addEventListener('message', (event) => {
      if (socket !== state.socket) return;
      let message;
      try {
        message = JSON.parse(event.data);
      } catch {
        return;
      }
      handleMessage(message);
    });

    socket.addEventListener('close', (event) => {
      if (socket !== state.socket) return;  // replaced by a newer connection
      setUsers(0);

      if (event.code === IDLE_CLOSE_CODE) {
        goIdle(event.reason);
        return;
      }
      finishEdit(false, { refocus: false });

      if (event.code === 1008) {
        setStatus('offline', 'Sheet is full');
        showMessage(event.reason || 'This sheet has too many people on it. Try again later.', 'error');
      }

      const delay = Math.min(MAX_RECONNECT_DELAY_MS, 1000 * 2 ** state.reconnectAttempts) * (0.75 + Math.random() / 2);
      state.reconnectAttempts++;
      if (event.code !== 1008) setStatus('offline', `Offline — retrying in ${Math.round(delay / 1000)}s`);
      state.reconnectTimer = setTimeout(connect, delay);
    });
  }

  function handleMessage(message) {
    switch (message.type) {
      case 'snapshot': {
        const resized = message.rows !== state.rows || message.cols !== state.cols;
        state.rows = message.rows;
        state.cols = message.cols;
        state.cells = new Map(Object.entries(message.cells));
        state.computedColumns = new Set(message.computedColumns);
        if (resized) buildGrid();
        renderAll();
        select(state.selected.row, state.selected.col, { scroll: false });
        setUsers(message.users);
        restoreDraft();
        break;
      }
      case 'cells': {
        const computed = new Set(message.computedColumns);
        const computedChanged = computed.size !== state.computedColumns.size
          || [...computed].some((col) => !state.computedColumns.has(col));
        state.computedColumns = computed;

        for (const [key, cell] of Object.entries(message.cells)) {
          if (cell.kind === 'blank' && cell.raw === '') state.cells.delete(key);
          else state.cells.set(key, cell);
          const pos = parseAddress(key);
          if (pos) renderCell(pos.row, pos.col, !computedChanged);
        }
        if (computedChanged) renderAll();
        updateFormulaBar();
        break;
      }
      case 'presence':
        setUsers(message.users);
        break;
      case 'error':
        showMessage(message.message, 'error');
        break;
    }
  }

  // ---- Idle disconnect ---------------------------------------------------------------------
  // A forgotten tab would keep the server running (and billing) for ever, so after 30 minutes
  // without use the page disconnects and offers a Reconnect button. Everything typed is already
  // saved; an edit that was still open is reopened after reconnecting.

  function noteActivity() {
    state.lastActivity = Date.now();
    if (isOpen() && state.lastActivity - state.lastActivitySignal >= ACTIVITY_SIGNAL_MS) {
      state.lastActivitySignal = state.lastActivity;
      state.socket.send(JSON.stringify({ type: 'active' }));
    }
  }

  function goIdle(serverReason = '') {
    if (state.idle) return;
    state.idle = true;
    clearTimeout(state.reconnectTimer);

    // Keep an unfinished edit so it can be reopened, without saving it
    if (state.editor) {
      state.draft = { ...state.selected, text: state.editor.value };
      finishEdit(false, { refocus: false });
    }

    const socket = state.socket;
    state.socket = null;
    socket?.close(1000, 'Idle');

    setUsers(0);
    setStatus('offline', 'Disconnected');
    const minutes = Math.round(IDLE_TIMEOUT_MS / 60000);
    const reason = serverReason || `Disconnected after ${minutes} minutes of inactivity`;
    el.idleText.textContent = `${reason}. Your changes are saved`
      + (state.draft ? `, and the edit you started in ${address(state.draft.row, state.draft.col)} will reopen.` : '.');
    el.idlePanel.hidden = false;
    el.reconnect.focus();
  }

  function reconnectAfterIdle() {
    el.idlePanel.hidden = true;
    state.idle = false;
    state.reconnectAttempts = 0;
    state.lastActivity = Date.now();
    state.lastActivitySignal = state.lastActivity;
    el.gridWrap.focus({ preventScroll: true });
    connect();
  }

  function restoreDraft() {
    const draft = state.draft;
    if (!draft) return;
    state.draft = null;
    select(draft.row, draft.col);
    if (!isComputed(draft.row, draft.col)) startEdit(draft.text);
  }

  el.reconnect.addEventListener('click', reconnectAfterIdle);

  for (const type of ['pointerdown', 'keydown', 'wheel', 'touchstart']) {
    window.addEventListener(type, noteActivity, { capture: true, passive: true });
  }
  let lastMove = 0;
  window.addEventListener('pointermove', () => {
    const now = Date.now();
    if (now - lastMove > 5000) { lastMove = now; noteActivity(); }
  }, { passive: true });

  setInterval(() => {
    if (!state.idle && Date.now() - state.lastActivity >= IDLE_TIMEOUT_MS) goIdle();
  }, 15000);

  function openSheet(name) {
    if (!SHEET_NAME_RULE.test(name)) {
      showMessage('Sheet names use 1-64 letters, digits, "-" or "_".', 'error');
      return;
    }

    state.sheet = name.toLowerCase();
    el.sheetName.value = state.sheet;
    document.title = `${state.sheet} — Cloud Spreadsheet`;

    const url = new URL(location.href);
    url.searchParams.set('sheet', state.sheet);
    history.replaceState(null, '', url);

    const old = state.socket;
    state.socket = null;
    old?.close(1000, 'Opening another sheet');
    state.reconnectAttempts = 0;
    connect();
  }

  el.openForm.addEventListener('submit', (event) => {
    event.preventDefault();
    openSheet(el.sheetName.value.trim());
  });

  el.copyLink.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(location.href);
      showMessage('Link copied. Anyone with it can view and edit this sheet.');
    } catch {
      showMessage(`Copy this link to share: ${location.href}`);
    }
  });

  // Reconnect at once when the tab becomes visible or the network returns
  const reconnectNow = () => {
    if (state.idle) return;  // only the Reconnect button reconnects an idle page
    if (!state.socket || state.socket.readyState === WebSocket.CLOSED) {
      state.reconnectAttempts = Math.min(state.reconnectAttempts, 1);
      connect();
    }
  };
  window.addEventListener('online', reconnectNow);

  // Leave the sheet when the page is left, so others' "people viewing" count is right even if the
  // browser keeps this page in its back/forward cache; rejoin if the page is shown again
  window.addEventListener('pagehide', () => {
    clearTimeout(state.reconnectTimer);
    const socket = state.socket;
    state.socket = null;
    socket?.close(1000, 'Page closed');
  });
  window.addEventListener('pageshow', (event) => {
    if (event.persisted) reconnectNow();
  });
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') reconnectNow();
  });

  const requested = new URLSearchParams(location.search).get('sheet');
  openSheet(requested && SHEET_NAME_RULE.test(requested) ? requested : DEFAULT_SHEET);
  el.gridWrap.focus({ preventScroll: true });
})();
