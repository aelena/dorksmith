// Names of every placeholder a template pattern may use, and which are optional by default.
// Port of Dorksmith.Api.Generation.Placeholders.

export const ALL_PLACEHOLDERS: ReadonlySet<string> = new Set([
  'terms', 'termsAny', 'quoted', 'quotedReversed', 'quotedInitialLast', 'phraseWildcard',
  'intitle', 'intitleAny', 'inurl', 'inurlAny', 'intext', 'intextAny',
  'domain', 'domainQuoted', 'domainLabel', 'domainLabelBare', 'site', 'siteOption', 'siteWildcard',
  'excludeSite', 'excludeWww', 'atDomainQuoted',
  'username', 'quotedUsername', 'atUsername', 'inurlUsername', 'intitleUsername', 'hashtagUsername',
  'emailQuoted', 'emailUserQuoted',
  'url', 'urlQuoted', 'urlBareQuoted', 'inurlPath',
  'filename', 'filenameStem', 'filetypeFromFilename',
  'filetypeGroup', 'filetypeFirst', 'exclusions', 'dates', 'after', 'before',
  'keywords', 'keywordsAnd', 'platformSites',
  'organization', 'location', 'role', 'displayName', 'context',
]);

/** Placeholders that may resolve to nothing without disqualifying the template. */
export const OPTIONAL_BY_DEFAULT: ReadonlySet<string> = new Set([
  'exclusions', 'dates', 'after', 'before', 'site', 'siteOption', 'context',
]);

/** Extracts {placeholder} names from a pattern in order of appearance. */
export function placeholdersIn(pattern: string): string[] {
  const names: string[] = [];
  let i = 0;
  for (;;) {
    const open = pattern.indexOf('{', i);
    if (open < 0) break;
    const close = pattern.indexOf('}', open + 1);
    if (close < 0) break;
    names.push(pattern.slice(open + 1, close));
    i = close + 1;
  }
  return names;
}
