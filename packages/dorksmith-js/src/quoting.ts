// Safe search-string quoting. Port of Dorksmith.Api.Generation.QueryQuoting.
import { isLetter } from './normalizer.js';

const RESERVED = new Set(['OR', 'AND', '|']);

function stripOuterQuotes(s: string): string {
  return s.length >= 2 && s[0] === '"' && s[s.length - 1] === '"' ? s.slice(1, -1) : s;
}

/** Wraps a phrase in ASCII double quotes. Existing surrounding quotes are reused, inner quotes removed. */
export function quote(phrase: string | null | undefined): string {
  let s = (phrase ?? '').trim();
  s = stripOuterQuotes(s).replace(/"/g, '').trim();
  return s ? `"${s}"` : '';
}

export function unquote(phrase: string | null | undefined): string {
  return stripOuterQuotes((phrase ?? '').trim()).replace(/"/g, '').trim();
}

export function looksLikeSyntax(s: string): boolean {
  if (RESERVED.has(s)) return true;
  if ('-+~()|'.includes(s[0])) return true;
  const last = s[s.length - 1];
  if (last === '(' || last === ')') return true;
  const colon = s.indexOf(':');
  if (colon > 0 && colon < s.length - 1 && [...s.slice(0, colon)].every(isLetter)) return true;
  if (s.startsWith('AROUND(')) return true;
  return false;
}

/** A single word emitted unquoted; words that would parse as syntax are quoted so they stay literal. */
export function safeTerm(word: string | null | undefined): string {
  const s = (word ?? '').trim().replace(/"/g, '');
  if (!s) return '';
  if (looksLikeSyntax(s)) return `"${s.startsWith('AROUND(') ? s : s.replace(/^[()]+|[()]+$/g, '')}"`;
  return s;
}

export function safeTerms(items: readonly string[]): string {
  return items.map(safeTerm).filter(Boolean).join(' ');
}

/** Value for a prefix operator: bare for a single safe word, quoted otherwise. */
export function operatorValue(phrase: string | null | undefined): string {
  const s = unquote(phrase);
  if (!s) return '';
  return s.includes(' ') || looksLikeSyntax(s) ? `"${s}"` : s;
}

/** (a OR b OR c); a single item is returned bare; none returns empty. */
export function orGroup(items: readonly (string | null | undefined)[]): string {
  const list: string[] = [];
  for (const i of items) if (i && i.trim() && !list.includes(i)) list.push(i);
  if (list.length === 0) return '';
  if (list.length === 1) return list[0];
  return `(${list.join(' OR ')})`;
}

/** -term for exclusions. Known generatable operators (site:x) pass through; phrases get quoted. */
export function exclusion(term: string | null | undefined, isGeneratableOperator: (token: string) => boolean): string {
  const s = (term ?? '').trim().replace(/^-+/, '').replace(/"/g, '').trim();
  if (!s) return '';
  const colon = s.indexOf(':');
  if (colon > 0 && colon < s.length - 1 && !s.includes(' ') && isGeneratableOperator(s.slice(0, colon + 1).toLowerCase()))
    return '-' + s.slice(0, colon).toLowerCase() + s.slice(colon);
  return s.includes(' ') || looksLikeSyntax(s) ? `-"${s}"` : `-${s}`;
}
