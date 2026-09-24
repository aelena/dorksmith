// localStorage wrapper. Everything here is optional and per-browser; failures are swallowed
// (private windows, blocked storage) so the app always works without it.

const PREFIX = 'dorksmith:';

function read(key, fallback) {
  try {
    const raw = localStorage.getItem(PREFIX + key);
    return raw === null ? fallback : JSON.parse(raw);
  } catch { return fallback; }
}

function write(key, value) {
  try { localStorage.setItem(PREFIX + key, JSON.stringify(value)); } catch { /* ignore */ }
}

function remove(key) {
  try { localStorage.removeItem(PREFIX + key); } catch { /* ignore */ }
}

export const storage = {
  get theme() { return read('theme', 'auto'); },
  set theme(v) { write('theme', v); },

  get historyEnabled() { return read('history.enabled', false); },
  set historyEnabled(v) { write('history.enabled', !!v); if (!v) remove('history.items'); },

  /** Recent targets, most recent first. Only kept when the user opted in. */
  get history() { return this.historyEnabled ? read('history.items', []) : []; },
  pushHistory(entry) {
    if (!this.historyEnabled || !entry?.input) return;
    const items = this.history.filter(h => h.input !== entry.input);
    items.unshift({ input: entry.input, inputType: entry.inputType, intent: entry.intent, at: Date.now() });
    write('history.items', items.slice(0, 25));
  },
  clearHistory() { remove('history.items'); },

  /** Selection frequency for suggestion ranking (token -> count). Small, capped, anonymous. */
  get selectionCounts() { return read('suggest.counts', {}); },
  bumpSelection(token) {
    const counts = this.selectionCounts;
    counts[token] = (counts[token] || 0) + 1;
    const entries = Object.entries(counts).sort((a, b) => b[1] - a[1]).slice(0, 100);
    write('suggest.counts', Object.fromEntries(entries));
  },
};
