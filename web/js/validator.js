// Client-side syntax warnings for the target box. Mirrors the server analyser for instant feedback;
// the server remains the source of truth for generated variants.
import { allOperators } from './operators-ui.js';

const OPERATOR_PREFIX = /^-?([a-zA-Z]{1,15}):(.*)$/;

/** @returns {string[]} plain-language warnings for a raw input string */
export function analyzeInput(raw) {
  const warnings = [];
  const s = (raw || '').trim();
  if (!s) return warnings;
  const ops = allOperators();
  const byToken = new Map(ops.map(o => [o.token, o]));

  const quoteCount = (s.match(/"/g) || []).length;
  if (quoteCount % 2 === 1) warnings.push('Unbalanced double quotes.');

  let depth = 0;
  for (const ch of s) { if (ch === '(') depth++; if (ch === ')') depth--; if (depth < 0) break; }
  if (depth !== 0) warnings.push('Unbalanced parentheses.');

  // Tokens outside quotes only.
  const outside = s.replace(/"[^"]*"/g, ' ');
  for (const token of outside.split(/\s+/).filter(Boolean)) {
    if (token === 'or' || token === 'and') {
      warnings.push(`Lower-case '${token}' is treated as a word; use ${token.toUpperCase()} as an operator.`);
      continue;
    }
    const m = OPERATOR_PREFIX.exec(token.replace(/^\(+/, '').replace(/\)+$/, ''));
    if (!m) continue;
    const prefix = m[1].toLowerCase() + ':';
    const value = m[2];
    const op = byToken.get(prefix);
    if (!op) {
      if (!token.includes('://')) warnings.push(`'${prefix}' is not a known operator; it will be searched as plain text.`);
      continue;
    }
    if (op.support === 'deprecated') warnings.push(`${prefix} is deprecated and no longer works.`);
    else if (op.support === 'unreliable') warnings.push(`${prefix} is unreliable: ${op.caveats?.[0] || 'results are inconsistent.'}`);
    if (prefix === 'filetype:' && value.startsWith('.')) warnings.push('filetype: takes no leading dot (filetype:pdf).');
    if (prefix === 'site:' && value.includes('://')) warnings.push('site: takes no scheme (site:example.com).');
    if ((prefix === 'before:' || prefix === 'after:') && !/^\d{4}(-\d{2}-\d{2})?$/.test(value)) warnings.push(`${prefix} expects YYYY-MM-DD or YYYY.`);
  }

  const words = s.split(/\s+/).length;
  if (words > 32) warnings.push(`Query has ${words} terms; Google ignores everything after the first 32.`);
  if (ops.length && /(^|\s)[a-zA-Z]+:\S/.test(outside)) {
    warnings.push('Operators typed here are kept as literal text in generated variants; use the options to add scoping, or the Operator Guide to learn syntax.');
  }
  return [...new Set(warnings)];
}
