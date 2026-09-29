// Application shell: view switching, theme, global keyboard shortcuts, public config in the notices.
import { $, $$, setText, toast, isTypingContext } from './dom.js';
import { storage } from './storage.js';
import { api } from './api.js';
import { initOperatorsUi } from './operators-ui.js';
import { initGeneratorUi } from './generator-ui.js';
import { initAutocomplete } from './autocomplete.js';
import { initHandlesUi } from './handles-ui.js';

const VIEWS = ['generator', 'handles', 'operators', 'about'];
// Alt + initial letter. Physical key codes keep it layout-independent; AltGr arrives as Ctrl+Alt and is ignored.
const VIEW_KEYS = { KeyG: 'generator', KeyU: 'handles', KeyO: 'operators', KeyA: 'about' };

/* ---------- Views (tabs) ---------- */
export function showView(name) {
  if (!VIEWS.includes(name)) name = 'generator';
  for (const v of VIEWS) {
    const tab = $(`#tab-${v}`);
    const panel = $(`#panel-${v}`);
    const active = v === name;
    tab.setAttribute('aria-selected', String(active));
    tab.tabIndex = active ? 0 : -1;
    if (active) panel.removeAttribute('hidden'); else panel.setAttribute('hidden', '');
  }
  if (location.hash !== `#${name}`) history.replaceState(null, '', `#${name}`);
  document.dispatchEvent(new CustomEvent('dorksmith:view', { detail: { view: name } }));
}

function initTabs() {
  const tabs = $$('.tabs [role="tab"]');
  tabs.forEach((tab, i) => {
    tab.addEventListener('click', () => showView(tab.dataset.view));
    tab.addEventListener('keydown', (e) => {
      const dir = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
      if (!dir) return;
      e.preventDefault();
      const next = tabs[(i + dir + tabs.length) % tabs.length];
      next.focus();
      showView(next.dataset.view);
    });
  });
  $$('[data-view]:not([role="tab"])').forEach(b => b.addEventListener('click', () => showView(b.dataset.view)));
  window.addEventListener('hashchange', () => showView(location.hash.slice(1)));
  showView(location.hash.slice(1) || 'generator');
}

/* ---------- Theme ---------- */
const THEMES = ['auto', 'light', 'dark'];
function applyTheme(t) {
  document.documentElement.dataset.theme = t;
  const btn = $('#theme-toggle');
  btn.textContent = t === 'dark' ? '●' : t === 'light' ? '○' : '◐';
  btn.setAttribute('aria-label', `Colour theme: ${t}. Activate to change.`);
}
function initTheme() {
  applyTheme(storage.theme);
  $('#theme-toggle').addEventListener('click', () => {
    const next = THEMES[(THEMES.indexOf(storage.theme) + 1) % THEMES.length];
    storage.theme = next;
    applyTheme(next);
    toast(`Theme: ${next}`);
  });
}

/* ---------- Global keyboard ---------- */
function initShortcuts() {
  document.addEventListener('keydown', (e) => {
    if (e.key === '/' && !isTypingContext(e.target) && !e.ctrlKey && !e.metaKey && !e.altKey) {
      e.preventDefault();
      showView('generator');
      $('#input').focus();
      $('#input').select();
    }
    if (e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey && VIEW_KEYS[e.code]) {
      e.preventDefault();
      showView(VIEW_KEYS[e.code]);
      $(`#tab-${VIEW_KEYS[e.code]}`).focus();
    }
  });
}

/* ---------- Public config → notices ---------- */
async function initNotices() {
  try {
    const cfg = await api.config();
    setText('#about-catalog-version', cfg.catalogVersion);
    setText('#footer-catalog-version', cfg.catalogVersion);
    fetch('js/engine/VERSION').then(r => (r.ok ? r.text() : '')).then(v => setText('#about-engine-version', (v || '').trim().split(' ')[1] || 'dev')).catch(() => setText('#about-engine-version', 'dev'));
    $('#variants').max = String(cfg.maxVariants);
    if (!cfg.usernameSearchEnabled) { $('#tab-handles').setAttribute('hidden', ''); }
    document.dispatchEvent(new CustomEvent('dorksmith:config', { detail: cfg }));
  } catch (e) {
    setText('#results-status', 'The engine failed to load. Run `npm run build` at the repository root and reload.');
  }
}

/* ---------- History opt-in (About) ---------- */
function initHistoryControls() {
  const box = $('#history-opt-in');
  box.checked = storage.historyEnabled;
  box.addEventListener('change', () => { storage.historyEnabled = box.checked; toast(box.checked ? 'Local history enabled' : 'Local history disabled and cleared'); });
  $('#history-clear').addEventListener('click', () => { storage.clearHistory(); toast('History cleared'); });
}

initTheme();
initTabs();
initShortcuts();
initHistoryControls();
initNotices();
initOperatorsUi().then(() => initGeneratorUi()).then(() => initAutocomplete());
initHandlesUi();
