// Operator guide: searchable, filterable cards built from the operator catalog.
import { $, $$, el, clear, setText, debounce } from './dom.js';
import { api } from './api.js';

const SUPPORT_ORDER = { official: 0, working: 1, unreliable: 2, unknown: 3, deprecated: 4 };
const FILTERS = {
  all: () => true,
  reliable: op => op.support === 'official' || op.support === 'working',
  unreliable: op => op.support === 'unreliable' || op.support === 'unknown',
  deprecated: op => op.support === 'deprecated',
};

let operators = [];
let engine = 'google';

export function supportBadge(support) {
  return el('span', { class: `badge support badge-${support}`, text: support });
}

function matches(op, needle) {
  if (!needle) return true;
  const hay = [op.token, op.name, op.description, op.category, op.syntax, op.example, ...(op.caveats || [])].join(' ').toLowerCase();
  return hay.includes(needle);
}

function renderCard(op) {
  const tpl = $('#tpl-operator').content.firstElementChild.cloneNode(true);
  setText('.op-token', op.token, tpl);
  const badge = $('.support', tpl);
  badge.className = `badge support badge-${op.support}`;
  badge.textContent = op.support;
  setText('.op-name', op.name, tpl);
  setText('.op-description', op.description, tpl);
  setText('.op-syntax', op.syntax, tpl);
  setText('.op-example', op.example || '—', tpl);
  const caveats = $('.op-caveats', tpl);
  caveats.textContent = (op.caveats || []).join(' ');
  if (!op.generate && op.support !== 'deprecated') caveats.textContent = (caveats.textContent + ' Not emitted by the generator.').trim();
  const src = $('.op-source', tpl);
  clear(src);
  src.append('Category: ', el('span', { text: op.category }));
  (op.sourceUrls || []).forEach((u, i) => {
    let host = u; try { host = new URL(u).hostname.replace(/^www\./, ''); } catch { /* keep raw */ }
    src.append(i === 0 ? ' · Source: ' : ', ', el('a', { href: u, target: '_blank', rel: 'noopener noreferrer', text: host }));
  });
  tpl.dataset.support = op.support;
  return tpl;
}

function render() {
  const needle = $('#op-search').value.trim().toLowerCase();
  const filter = FILTERS[$$('input[name="op-filter"]').find(r => r.checked)?.value || 'all'];
  const list = operators
    .filter(filter)
    .filter(op => matches(op, needle))
    .sort((a, b) => (SUPPORT_ORDER[a.support] - SUPPORT_ORDER[b.support]) || a.token.localeCompare(b.token));
  const root = clear($('#op-list'));
  list.forEach(op => root.append(renderCard(op)));
  setText('#op-count', list.length === operators.length
    ? `${operators.length} operators in the ${engine} catalog`
    : `${list.length} of ${operators.length} operators`);
  if (list.length === 0) root.append(el('p', { class: 'muted', text: 'No operators match.' }));
}

export async function initOperatorsUi() {
  try {
    const catalog = await api.operators(engine);
    operators = catalog.operators || [];
    engine = catalog.engine || engine;
    setText('#op-engine', `${catalog.engineName || engine} · ${catalog.catalogVersion}`);
  } catch (e) {
    setText('#op-count', `Could not load the operator catalog: ${e.message}`);
    return;
  }
  $('#op-search').addEventListener('input', debounce(render, 80));
  $$('input[name="op-filter"]').forEach(r => r.addEventListener('change', render));
  render();
}

/** Lookup used by other modules for chips and warnings. */
export function operatorByToken(token) {
  return operators.find(o => o.token === token) || null;
}
export function allOperators() { return operators; }
