// Quote-aware operator scanner with plain-language warnings. Port of Dorksmith.Api.Generation.QueryValidator.
import type { OperatorCatalog, OperatorDefinition } from './types.js';
import { isLetter } from './normalizer.js';

export const GOOGLE_WORD_LIMIT = 32;

export interface QueryAnalysis { operators: string[]; warnings: string[]; hasErrors: boolean }

interface Token { text: string; quoted: boolean }

export function operatorIndex(catalog: OperatorCatalog): Map<string, OperatorDefinition> {
  const m = new Map<string, OperatorDefinition>();
  for (const op of catalog.operators) if (!m.has(op.token)) m.set(op.token, op);
  return m;
}

export function analyzeQuery(query: string, catalog: OperatorCatalog, byToken: Map<string, OperatorDefinition> = operatorIndex(catalog)): QueryAnalysis {
  const operators: string[] = [];
  const warnings: string[] = [];
  let hasErrors = false;
  const use = (token: string) => { if (!operators.includes(token)) operators.push(token); };

  let depth = 0;
  const tokens = tokenize(query);
  const quoteCount = (query.match(/"/g) || []).length;
  if (quoteCount > 0) use('"');
  if (quoteCount % 2 === 1) { warnings.push('Unbalanced double quotes: the last phrase will not be treated as exact.'); hasErrors = true; }

  for (const raw of tokens) {
    if (raw.quoted) { if (raw.text.includes('*')) use('*'); continue; }
    const t = raw.text;
    if (!t) continue;

    if (t === 'OR') { use('OR'); continue; }
    if (t === 'AND') { use('AND'); continue; }
    if (t === '|') { use('|'); continue; }
    if (t === 'or' || t === 'and') {
      warnings.push(`Lower-case '${t}' is treated as an ordinary word; use upper-case ${t.toUpperCase()} for a boolean operator.`);
      continue;
    }

    let body = t;
    while (body.length > 0 && body[0] === '(') { depth++; use('('); body = body.slice(1); }
    let closing = 0;
    while (body.length > 0 && body[body.length - 1] === ')') { closing++; body = body.slice(0, -1); }
    depth -= closing;

    if (body.startsWith('AROUND(')) { use('AROUND('); continue; }
    if (body.length > 1 && body[0] === '-') { use('-'); body = body.slice(1); }
    else if (body.length > 1 && body[0] === '+') { use('+'); body = body.slice(1); }
    else if (body.length > 1 && body[0] === '~') { use('~'); body = body.slice(1); }
    if (body.length > 1 && body[0] === '#') { use('#'); continue; }
    if (body.length > 1 && body[0] === '@') { use('@'); continue; }
    if (body === '*') { use('*'); continue; }
    if (body.includes('..') && /\d/.test(body)) { use('..'); continue; }
    if (body.length > 1 && body[0] === '$' && /^[\d.,]+$/.test(body.slice(1))) { use('$'); continue; }

    const colon = body.indexOf(':');
    if (colon > 0 && [...body.slice(0, colon)].every(isLetter)) {
      const prefix = body.slice(0, colon + 1).toLowerCase();
      const value = body.slice(colon + 1);
      const op = byToken.get(prefix);
      if (op) {
        use(prefix);
        if (prefix === 'filetype:' && value.startsWith('.')) warnings.push('filetype: values take no leading dot (filetype:pdf).');
        if (prefix === 'site:' && value.includes('://')) warnings.push('site: values take no scheme (site:example.com).');
        if ((prefix === 'before:' || prefix === 'after:') && !(value.length === 4 || value.length === 10)) warnings.push(`${prefix} expects YYYY-MM-DD or YYYY.`);
        if (op.takesValue && value.length === 0) { warnings.push(`${prefix} has no value.`); hasErrors = true; }
      } else if (body.slice(0, colon).length <= 15 && !body.includes('://')) {
        warnings.push(`'${prefix}' is not a known ${catalog.engineName} operator and will be searched as plain text.`);
      }
    }
  }

  if (depth !== 0) { warnings.push('Unbalanced parentheses.'); hasErrors = true; }

  for (const token of operators) {
    const op = byToken.get(token);
    if (!op) continue;
    switch (op.support) {
      case 'deprecated': warnings.push(`${op.token} is deprecated: ${op.caveats?.[0] ?? 'it no longer works.'}`); break;
      case 'unreliable': warnings.push(`${op.token} is unreliable: ${op.caveats?.[0] ?? 'results are inconsistent.'}`); break;
      case 'unknown': warnings.push(`${op.token} has unknown support status.`); break;
    }
  }

  const wordCount = tokens.filter(t => t.text.length > 0).length;
  if (wordCount > GOOGLE_WORD_LIMIT) warnings.push(`Query has ${wordCount} terms; Google ignores everything after the first ${GOOGLE_WORD_LIMIT}.`);

  return { operators, warnings, hasErrors };
}

function tokenize(query: string): Token[] {
  const tokens: Token[] = [];
  let i = 0;
  while (i < query.length) {
    if (/\s/.test(query[i])) { i++; continue; }
    if (query[i] === '"') {
      let end = query.indexOf('"', i + 1);
      if (end < 0) end = query.length - 1;
      tokens.push({ text: query.slice(i + 1, Math.max(i + 1, end)), quoted: true });
      i = end + 1;
      continue;
    }
    const start = i;
    while (i < query.length && !/\s/.test(query[i])) {
      if (query[i] === '"') {
        const end = query.indexOf('"', i + 1);
        i = end < 0 ? query.length : end + 1;
        continue;
      }
      i++;
    }
    tokens.push({ text: query.slice(start, i), quoted: false });
  }
  return tokens;
}
