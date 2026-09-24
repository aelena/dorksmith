// Syntax highlighting for generated queries. Builds DOM nodes with textContent only — no innerHTML.
import { el } from './dom.js';

const OPERATOR_PREFIX = /^([a-zA-Z]+:)(.*)$/;

function tokenize(query) {
  const tokens = [];
  let i = 0;
  while (i < query.length) {
    const ch = query[i];
    if (/\s/.test(ch)) { let j = i; while (j < query.length && /\s/.test(query[j])) j++; tokens.push({ kind: 'ws', text: query.slice(i, j) }); i = j; continue; }
    if (ch === '"') { const end = query.indexOf('"', i + 1); const j = end < 0 ? query.length : end + 1; tokens.push({ kind: 'str', text: query.slice(i, j) }); i = j; continue; }
    if (ch === '(' || ch === ')') { tokens.push({ kind: 'paren', text: ch }); i++; continue; }
    let j = i;
    while (j < query.length && !/\s/.test(query[j]) && query[j] !== ')' && query[j] !== '(') {
      if (query[j] === '"') { const end = query.indexOf('"', j + 1); j = end < 0 ? query.length : end + 1; continue; }
      j++;
    }
    tokens.push({ kind: 'word', text: query.slice(i, j) });
    i = j;
  }
  return tokens;
}

/** Render `query` into `container` (cleared first) with operator/string spans. */
export function renderQuery(container, query) {
  while (container.firstChild) container.removeChild(container.firstChild);
  for (const t of tokenize(query)) {
    if (t.kind === 'ws' || t.kind === 'paren') { container.append(document.createTextNode(t.text)); continue; }
    if (t.kind === 'str') { container.append(el('span', { class: 'str', text: t.text })); continue; }
    let text = t.text;
    let neg = '';
    if (text.length > 1 && text[0] === '-') { neg = '-'; text = text.slice(1); }
    if (neg) container.append(el('span', { class: 'op', text: neg }));
    if (text === 'OR' || text === 'AND' || text.startsWith('AROUND(')) { container.append(el('span', { class: 'op', text })); continue; }
    const m = OPERATOR_PREFIX.exec(text);
    if (m) {
      container.append(el('span', { class: 'op', text: m[1] }));
      const value = m[2];
      if (value.startsWith('"')) container.append(el('span', { class: 'str', text: value }));
      else container.append(document.createTextNode(value));
      continue;
    }
    container.append(document.createTextNode(text));
  }
  return container;
}
