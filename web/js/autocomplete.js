// Operator-aware autocomplete for the target box. Entirely client-side: sources are the operator,
// intent, file-type and platform catalogs, a small defensive keyword list, and (opt-in) local history.
// Ranking: prefix match > substring match, plus static weight, intent context and local selection frequency.
import { $, el, clear, show } from './dom.js';
import { storage } from './storage.js';
import { allOperators } from './operators-ui.js';
import { generatorApi, inferInputType } from './generator-ui.js';

const MAX_ITEMS = 8;

const DEFENSIVE_SNIPPETS = [
  { token: 'intitle:"index of"', desc: 'Directory listing marker', weight: 60 },
  { token: 'inurl:admin', desc: 'Admin paths', weight: 40 },
  { token: 'inurl:login', desc: 'Login pages', weight: 40 },
  { token: 'filetype:log', desc: 'Log files', weight: 45 },
  { token: 'filetype:env', desc: 'Environment files', weight: 45 },
  { token: 'filetype:sql', desc: 'SQL dumps', weight: 40 },
  { token: 'inurl:swagger', desc: 'API documentation', weight: 35 },
  { token: '-inurl:login', desc: 'Exclude login pages', weight: 30 },
  { token: '-site:', desc: 'Exclude a host', weight: 30 },
];

const state = {
  items: [],
  active: -1,
  open: false,
  input: null,
  list: null,
};

/* ------------------------------------------------------------------ candidates */
function currentToken(input) {
  const caret = input.selectionStart ?? input.value.length;
  const before = input.value.slice(0, caret);
  const start = Math.max(before.lastIndexOf(' '), before.lastIndexOf('('), before.lastIndexOf('"')) + 1;
  return { text: before.slice(start), start, caret, before };
}

function score(candidate, needle) {
  const t = candidate.match.toLowerCase();
  const n = needle.toLowerCase();
  let s = 0;
  if (!n) s = 10;
  else if (t.startsWith(n)) s = 100;
  else if (t.includes(n)) s = 50;
  else return -1;
  s += (candidate.weight || 0) / 10;
  s += (storage.selectionCounts[candidate.token] || 0) * 5;
  s += candidate.contextBonus || 0;
  return s;
}

function candidatesFor(token, whole) {
  const items = [];
  const inputType = generatorApi.inputType;
  const lower = token.toLowerCase();

  // filetype: → extensions
  const ftMatch = /^(-?)(filetype:|ext:)(.*)$/i.exec(token);
  if (ftMatch && generatorApi.fileTypes) {
    const needle = ftMatch[3];
    for (const e of generatorApi.fileTypes.extensions) {
      items.push({ kind: 'extension', token: `${ftMatch[1]}filetype:${e.ext}`, match: e.ext, desc: e.label, weight: e.weight, needle, replaceToken: true });
    }
    return rank(items, needle);
  }

  // before:/after: → date syntax
  if (/^(before|after):?/i.test(token)) {
    const today = new Date().toISOString().slice(0, 10);
    const lastYear = `${new Date().getUTCFullYear() - 1}-01-01`;
    for (const op of ['after:', 'before:']) {
      if (!op.startsWith(lower.replace(/:.*$/, '')) && !lower.startsWith(op.slice(0, -1))) continue;
      items.push({ kind: 'operator', token: `${op}${op === 'after:' ? lastYear : today}`, match: op, desc: `${op}YYYY-MM-DD — edit the date`, weight: 80, needle: token, replaceToken: true });
    }
    return rank(items, token.replace(/:.*$/, ''));
  }

  // whole-input suggestions: intents for the detected type, platforms for usernames
  if (!whole.includes(' ') && token === whole) {
    const detected = inferInputType(whole, generatorApi.fileTypes ? new Set(generatorApi.fileTypes.extensions.map(e => e.ext)) : null);
    if (detected === 'domain' || detected === 'username' || detected === 'email' || detected === 'url') {
      for (const intent of generatorApi.intents.filter(i => i.compatibleInputTypes.includes(detected))) {
        items.push({ kind: 'intent', token: intent.id, match: intent.label, desc: intent.description, weight: 50, contextBonus: 40, needle: '', apply: () => { generatorApi.setInputType(detected); generatorApi.setIntent(intent.id); } });
      }
    }
    if (detected === 'username') {
      items.push({ kind: 'intent', token: 'username-search-tab', match: 'Open Username Search', desc: 'Expand the handle into profile URLs on the Username Search tab', weight: 90, contextBonus: 60, needle: '', apply: () => { $('#handle').value = whole; location.hash = '#handles'; } });
    }
    if (items.length && whole.length >= 3) return rank(items, '').slice(0, 5);
  }

  // operators by prefix / substring on the current token
  if (token.length >= 1 && !/^["(]/.test(token)) {
    const bare = token.replace(/^-/, '');
    for (const op of allOperators()) {
      if (!op.takesValue || !op.token.endsWith(':')) continue;
      if (op.support === 'deprecated') continue;
      items.push({ kind: 'operator', token: (token.startsWith('-') ? '-' : '') + op.token, match: op.token, desc: `${op.name}${op.support !== 'official' ? ` · ${op.support}` : ''}`, weight: op.generate ? 90 : 40, support: op.support, needle: bare, replaceToken: true, keepTyping: true });
    }
    if (inputType === 'domain' || generatorApi.intents.find(i => i.id === $('#intent').value)?.safety === 'defensive-exposure') {
      for (const s of DEFENSIVE_SNIPPETS) items.push({ kind: 'snippet', token: s.token, match: s.token, desc: s.desc, weight: s.weight, needle: bare, replaceToken: true });
    }
    for (const h of storage.history) {
      items.push({ kind: 'history', token: h.input, match: h.input, desc: `${h.inputType} · ${h.intent}`, weight: 20, needle: token, apply: () => { state.input.value = h.input; generatorApi.setInputType(h.inputType); generatorApi.setIntent(h.intent); state.input.dispatchEvent(new Event('input', { bubbles: true })); } });
    }
    return rank(items, bare);
  }
  return [];
}

function rank(items, needle) {
  return items
    .map(c => ({ c, s: score(c, needle) }))
    .filter(x => x.s >= 0)
    .sort((a, b) => b.s - a.s || a.c.match.localeCompare(b.c.match))
    .slice(0, MAX_ITEMS)
    .map(x => x.c);
}

/* ------------------------------------------------------------------ rendering */
function render() {
  const list = clear(state.list);
  state.items.forEach((item, i) => {
    const li = el('li', { role: 'option', id: `sg-${i}`, 'aria-selected': String(i === state.active) }, [
      el('span', { class: 'sg-token', text: item.match }),
      el('span', { class: 'sg-desc', text: item.desc }),
      el('span', { class: 'sg-kind', text: item.kind }),
    ]);
    li.addEventListener('mousedown', (e) => { e.preventDefault(); select(i); });
    li.addEventListener('mousemove', () => { if (state.active !== i) { state.active = i; updateActive(); } });
    list.append(li);
  });
  const open = state.items.length > 0;
  show(list, open);
  state.open = open;
  state.input.setAttribute('aria-expanded', String(open));
  updateActive();
}

function updateActive() {
  [...state.list.children].forEach((li, i) => li.setAttribute('aria-selected', String(i === state.active)));
  if (state.active >= 0) {
    state.input.setAttribute('aria-activedescendant', `sg-${state.active}`);
    state.list.children[state.active]?.scrollIntoView({ block: 'nearest' });
  } else {
    state.input.removeAttribute('aria-activedescendant');
  }
}

export function closeSuggestions() {
  state.items = [];
  state.active = -1;
  render();
}

function select(i) {
  const item = state.items[i];
  if (!item) return;
  storage.bumpSelection(item.token);
  if (item.apply) { item.apply(); closeSuggestions(); return; }
  const input = state.input;
  const { start, caret } = currentToken(input);
  const after = input.value.slice(caret);
  const insert = item.token + (item.keepTyping ? '' : ' ');
  input.value = input.value.slice(0, start) + insert + after;
  const pos = start + insert.length;
  input.setSelectionRange(pos, pos);
  input.dispatchEvent(new Event('input', { bubbles: true }));
  if (item.keepTyping) update(); else closeSuggestions();
}

function update() {
  const input = state.input;
  const whole = input.value.trim();
  const { text } = currentToken(input);
  state.items = whole ? candidatesFor(text, whole) : [];
  state.active = state.items.length ? 0 : -1;
  render();
}

function onKeydown(e) {
  if (e.ctrlKey || e.metaKey || e.altKey) return; // modifier shortcuts (Ctrl+Enter generate) are not ours
  if (!state.open) {
    if (e.key === 'ArrowDown' && state.input.value.trim()) { e.preventDefault(); update(); }
    return;
  }
  switch (e.key) {
    case 'ArrowDown': e.preventDefault(); state.active = (state.active + 1) % state.items.length; updateActive(); break;
    case 'ArrowUp': e.preventDefault(); state.active = (state.active - 1 + state.items.length) % state.items.length; updateActive(); break;
    case 'Enter': if (state.active >= 0) { e.preventDefault(); select(state.active); } break;
    case 'Tab': if (state.active >= 0) { e.preventDefault(); select(state.active); } break;
    case 'Escape': e.preventDefault(); e.stopPropagation(); closeSuggestions(); break;
    default: break;
  }
}

export function initAutocomplete(input = $('#input'), list = $('#suggestions')) {
  state.input = input;
  state.list = list;
  input.addEventListener('input', update);
  input.addEventListener('keydown', onKeydown);
  input.addEventListener('blur', () => setTimeout(closeSuggestions, 120));
  input.addEventListener('focus', () => { if (input.value.trim()) update(); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && state.open) closeSuggestions(); });
}
