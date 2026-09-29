// Deterministic, non-destructive normalisation of user input. Never lower-cases search terms.
// Port of Dorksmith.Api.Generation.QueryNormalizer.

const SCHEME = /^[a-zA-Z][a-zA-Z0-9+.-]*:\/\//;
const HOSTNAME = /^(?:\*\.)?(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})$/;
const IPV4 = /^(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}$/;
const EMAIL = /^[^\s@"'<>()[\],;:\\]+@((?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63})$/i;
const FILENAME = /^(.+)\.([A-Za-z0-9]{1,12})$/;
const CONTROL = /\p{Cc}/u;

const QUOTE_MAP: Record<string, string> = {
  '“': '"', '”': '"', '„': '"', '‟': '"', '«': '"', '»': '"', '″': '"', '〃': '"',
  '‘': "'", '’': "'", '‚': "'", '‛': "'", '′': "'",
  ' ': ' ', ' ': ' ', ' ': ' ', '　': ' ',
};

/** Trims, collapses whitespace, maps Unicode quotes to ASCII and drops control characters. */
export function normalizeText(input: string | null | undefined): string {
  if (!input) return '';
  let out = '';
  for (const ch of input) {
    const mapped = QUOTE_MAP[ch];
    if (mapped !== undefined) { out += mapped; continue; }
    if (CONTROL.test(ch) && ch !== '\t' && ch !== '\n' && ch !== '\r') continue;
    out += ch;
  }
  return out.replace(/\s+/g, ' ').trim();
}

/**
 * Splits normalised text into terms. A fully quoted input ("a b") is unwrapped; quoted phrases inside the
 * text (alice "antonio elena") are kept as single terms, quotes included, so templates preserve them.
 */
export function words(normalized: string): { words: string[]; wasQuoted: boolean } {
  let s = normalized;
  const quoted = s.length >= 2 && s[0] === '"' && s[s.length - 1] === '"' && s.indexOf('"', 1) === s.length - 1;
  if (quoted) s = s.slice(1, -1).trim();
  const out: string[] = [];
  let i = 0;
  while (i < s.length) {
    if (s[i] === ' ') { i++; continue; }
    if (s[i] === '"') {
      const end = s.indexOf('"', i + 1);
      const inner = (end < 0 ? s.slice(i + 1) : s.slice(i + 1, end)).trim();
      if (inner) out.push(`"${inner}"`);
      i = end < 0 ? s.length : end + 1;
      continue;
    }
    let j = i;
    while (j < s.length && s[j] !== ' ') j++;
    out.push(s.slice(i, j));
    i = j;
  }
  return { words: out, wasQuoted: quoted };
}

/** Accepts host names with optional scheme/path/port; returns the bare lower-case host without a leading www. */
export function tryNormalizeDomain(input: string | null | undefined): string | null {
  let s = normalizeText(input);
  if (!s || s.includes(' ')) return null;
  s = s.replace(SCHEME, '');
  const cut = Math.min(...['/', '?', '#'].map(c => { const i = s.indexOf(c); return i < 0 ? Infinity : i; }));
  if (cut !== Infinity) s = s.slice(0, cut);
  const at = s.lastIndexOf('@');
  if (at >= 0) s = s.slice(at + 1);
  const colon = s.indexOf(':');
  if (colon >= 0) s = s.slice(0, colon);
  s = s.replace(/\.+$/, '').toLowerCase();
  if (s.startsWith('*.')) s = s.slice(2);
  if (s.startsWith('www.') && (s.match(/\./g) || []).length >= 2) s = s.slice(4);
  if (!s || s.length > 253) return null;
  if (!HOSTNAME.test(s) && !IPV4.test(s)) return null;
  return s;
}

/** The organisation-ish label of a domain: example for www.example.co.uk. */
export function domainLabel(domain: string): string {
  const labels = domain.split('.');
  if (labels.length < 2 || IPV4.test(domain)) return domain;
  const last = labels[labels.length - 1];
  const secondLast = labels[labels.length - 2];
  if (labels.length >= 3 && last.length <= 3 && secondLast.length <= 3) return labels[labels.length - 3];
  return secondLast;
}

export interface NormalizedUrl {
  /** Absolute URL without fragment, default port or trailing slash. */
  href: string;
  host: string;
  /** Escaped path, e.g. /docs/api */
  path: string;
  /** host[:port] + path + query, without scheme or trailing slash. */
  bare: string;
}

/** http/https only. A missing scheme is assumed to be https. Never fetches. */
export function tryNormalizeUrl(input: string | null | undefined): NormalizedUrl | null {
  let s = normalizeText(input);
  if (!s || s.includes(' ')) return null;
  if (!SCHEME.test(s)) s = 'https://' + s;
  let url: URL;
  try { url = new URL(s); } catch { return null; }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') return null;
  const host = url.hostname.toLowerCase();
  if (!HOSTNAME.test(host) && !IPV4.test(host)) return null;
  url.hash = '';
  url.hostname = host;
  if (url.pathname.length > 1) url.pathname = url.pathname.replace(/\/+$/, '');
  const hostPort = url.port ? `${host}:${url.port}` : host;
  return {
    href: url.href,
    host,
    path: url.pathname,
    bare: (hostPort + url.pathname + url.search).replace(/\/+$/, ''),
  };
}

/** Strips leading @ characters; rejects whitespace, control characters and quotes. */
export function tryNormalizeUsername(input: string | null | undefined, maxLength: number): string | null {
  const s = normalizeText(input).replace(/^@+/, '').trim();
  if (!s || s.length > maxLength) return null;
  for (const ch of s) if (/\s/.test(ch) || CONTROL.test(ch) || ch === '"') return null;
  return s;
}

export function tryNormalizeEmail(input: string | null | undefined): { email: string; localPart: string; domain: string } | null {
  const s = normalizeText(input).replace(/^[<>"']+|[<>"']+$/g, '');
  if (!EMAIL.test(s) || s.length > 254) return null;
  const at = s.lastIndexOf('@');
  const localPart = s.slice(0, at);
  const domain = s.slice(at + 1).toLowerCase();
  return { email: `${localPart}@${domain}`, localPart, domain };
}

export function tryParseFilename(input: string | null | undefined): { stem: string; extension: string } | null {
  const s = normalizeText(input).replace(/^"+|"+$/g, '');
  const m = FILENAME.exec(s);
  if (!m) return null;
  const stem = m[1].trim();
  if (!stem || stem.includes('"')) return null;
  return { stem, extension: m[2].toLowerCase() };
}

/** Strict ISO YYYY-MM-DD; returns the same string when valid. */
export function tryParseIsoDate(input: string | null | undefined): string | null {
  const s = normalizeText(input);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return null;
  const [y, m, d] = s.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d ? s : null;
}

/** Canonical form used for duplicate detection: collapsed whitespace, lower-cased operator prefixes, upper-cased OR/AND. */
export function canonicalize(query: string): string {
  const s = query.replace(/\s+/g, ' ').trim();
  const out: string[] = [];
  let inQuote = false;
  let start = 0;
  for (let i = 0; i <= s.length; i++) {
    const end = i === s.length;
    const c = end ? ' ' : s[i];
    if (!end && c === '"') inQuote = !inQuote;
    if ((c === ' ' && !inQuote) || end) {
      out.push(canonicalToken(s.slice(start, i)));
      start = i + 1;
    }
  }
  return out.join(' ').trim();
}

const LETTERS = /^\p{L}+$/u;
function canonicalToken(token: string): string {
  if (token.toLowerCase() === 'or') return 'OR';
  if (token.toLowerCase() === 'and') return 'AND';
  const body = token.replace(/^[(-]+/, '');
  const colon = body.indexOf(':');
  if (colon > 0 && LETTERS.test(body.slice(0, colon))) {
    const prefixLen = token.length - body.length;
    return token.slice(0, prefixLen) + body.slice(0, colon).toLowerCase() + body.slice(colon);
  }
  return token;
}

export const isLetter = (ch: string): boolean => /^\p{L}$/u.test(ch);
export const isLetterOrDigit = (ch: string): boolean => /^[\p{L}\p{Nd}]$/u.test(ch);
