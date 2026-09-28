// Deterministic, local input-type suggestion. The caller can always override the result.
import type { InputType } from './types.js';

const EMAIL_RE = /^[^\s@"'<>()[\],;:\\]+@(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;
const DOMAIN_RE = /^(?:\*\.)?(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})\.?$/i;
const URL_RE = /^(?:https?:\/\/)?(?:[a-z0-9-]+\.)+[a-z]{2,63}(?::\d+)?\/\S*$/i;
const FILENAME_RE = /^[^\s/\\]+\.([a-z0-9]{1,12})$/i;

/**
 * Suggests an input type for raw text. `knownExtensions` (e.g. from the file-type catalog) lets
 * `report.pdf` be classified as a filename rather than a domain.
 */
export function inferInputType(raw: string, knownExtensions: ReadonlySet<string> | null = null): InputType | null {
  const s = (raw || '').trim().replace(/^"(.*)"$/, '$1');
  if (!s) return null;
  if (/^@[^\s@]+$/.test(s)) return 'username';
  if (EMAIL_RE.test(s)) return 'email';
  if (/^https?:\/\//i.test(s) || URL_RE.test(s)) return 'url';
  if (DOMAIN_RE.test(s)) {
    const m = FILENAME_RE.exec(s);
    if (m && knownExtensions && knownExtensions.has(m[1].toLowerCase()) && s.split('.').length === 2) return 'filename';
    return 'domain';
  }
  const fm = FILENAME_RE.exec(s);
  if (fm && knownExtensions && knownExtensions.has(fm[1].toLowerCase())) return 'filename';
  const parts = s.split(/\s+/);
  if (parts.length >= 2 && parts.length <= 3 && parts.every(w => /^\p{Lu}[\p{L}'’.-]+$/u.test(w))) return 'person';
  return 'keyword';
}
