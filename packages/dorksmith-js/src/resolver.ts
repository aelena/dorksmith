// Resolves a template placeholder to operator syntax, or null when the input/options cannot supply it.
// Port of Dorksmith.Api.Generation.PlaceholderResolver.
import type { Catalogs, Intent, InputType, OperatorCatalog, Platform, QueryTemplate } from './types.js';
import { domainLabel, isLetterOrDigit } from './normalizer.js';
import type { NormalizedUrl } from './normalizer.js';
import { exclusion, operatorValue, orGroup, quote, safeTerm, safeTerms } from './quoting.js';

/** Fully validated and normalised inputs for one generation run. */
export interface GenerationContext {
  inputType: InputType;
  input: string;
  words: string[];
  intent: Intent;
  operators: OperatorCatalog;
  operatorByToken: Map<string, OperatorCatalog['operators'][number]>;
  maxVariants: number;
  domain: string | null;
  url: NormalizedUrl | null;
  username: string | null;
  email: string | null;
  emailUser: string | null;
  filenameStem: string | null;
  filenameExt: string | null;
  fileTypes: string[];
  excludeTerms: string[];
  after: string | null;
  before: string | null;
  site: string | null;
  organization: string | null;
  location: string | null;
  role: string | null;
  displayName: string | null;
}

const WORDY: ReadonlySet<string> = new Set(['keyword', 'person', 'organization', 'technology']);
const prefix = (op: string, value: string): string | null => (value ? op + value : null);
const nonEmpty = (v: string | null | undefined): string | null => (v && v.length > 0 ? v : null);

export function resolvePlaceholder(name: string, c: GenerationContext, t: QueryTemplate, catalogs: Catalogs): string | null {
  const phrase = c.words.join(' ');
  const wordy = WORDY.has(c.inputType);
  const isGeneratable = (token: string) => { const op = c.operatorByToken.get(token); return !!op && op.generate; };
  const kw = (k: string) => (k.includes(' ') ? quote(k) : safeTerm(k));

  let v: string | null;
  switch (name) {
    // ----- words -----
    case 'terms':
      v = c.inputType === 'domain' ? c.domain
        : c.inputType === 'username' ? c.username
        : c.inputType === 'email' ? quote(c.email)
        : c.inputType === 'url' ? quote(c.url!.bare)
        : safeTerms(c.words);
      break;
    case 'termsAny': v = wordy && c.words.length >= 2 ? orGroup(c.words.map(safeTerm)) : null; break;
    case 'quoted': v = c.inputType === 'url' ? quote(c.url!.href) : quote(phrase); break;
    case 'quotedReversed': v = wordy && c.words.length >= 2 ? quote(c.words[c.words.length - 1] + ' ' + c.words.slice(0, -1).join(' ')) : null; break;
    case 'quotedInitialLast': v = wordy && c.words.length >= 2 && /^\p{L}/u.test(c.words[0]) ? quote(c.words[0][0].toUpperCase() + '. ' + c.words[c.words.length - 1]) : null; break;
    case 'phraseWildcard': v = wordy && c.words.length >= 2 ? quote(c.words[0] + ' * ' + c.words[c.words.length - 1]) : null; break;
    case 'intitle': v = prefix('intitle:', operatorValue(phrase)); break;
    case 'intitleAny': v = c.words.length >= 2 ? orGroup(c.words.map(w => prefix('intitle:', operatorValue(w)))) : null; break;
    case 'inurl': v = c.inputType === 'url' ? resolvePlaceholder('inurlPath', c, t, catalogs) : prefix('inurl:', operatorValue(phrase)); break;
    case 'inurlAny': v = c.words.length >= 2 ? orGroup(c.words.map(w => prefix('inurl:', operatorValue(w)))) : null; break;
    case 'intext': v = prefix('intext:', c.inputType === 'url' ? quote(c.url!.href) : c.inputType === 'email' ? quote(c.email) : operatorValue(phrase)); break;
    case 'intextAny': v = c.words.length >= 2 ? orGroup(c.words.map(w => prefix('intext:', operatorValue(w)))) : null; break;

    // ----- domain -----
    case 'domain': v = c.domain; break;
    case 'domainQuoted': v = quote(c.domain); break;
    case 'domainLabel': v = c.domain === null ? null : quote(domainLabel(c.domain)); break;
    case 'domainLabelBare': v = c.domain === null ? null : domainLabel(c.domain); break;
    case 'site': v = c.domain !== null ? 'site:' + c.domain : c.site !== null ? 'site:' + c.site : null; break;
    case 'siteOption': v = c.site !== null ? 'site:' + c.site : null; break;
    case 'siteWildcard': v = c.domain === null ? null : 'site:*.' + c.domain; break;
    case 'excludeSite': v = c.domain === null ? null : '-site:' + c.domain; break;
    case 'excludeWww': v = c.domain === null ? null : '-site:www.' + c.domain; break;
    case 'atDomainQuoted': v = c.domain === null ? null : quote('@' + c.domain); break;

    // ----- username -----
    case 'username': v = c.username; break;
    case 'quotedUsername': v = quote(c.username); break;
    case 'atUsername': v = c.username === null ? null : quote('@' + c.username); break;
    case 'inurlUsername': v = prefix('inurl:', operatorValue(c.username)); break;
    case 'intitleUsername': v = prefix('intitle:', operatorValue(c.username)); break;
    case 'hashtagUsername': v = c.username !== null && [...c.username].every(ch => isLetterOrDigit(ch) || ch === '_') ? '#' + c.username : null; break;

    // ----- email -----
    case 'emailQuoted': v = quote(c.email); break;
    case 'emailUserQuoted': v = quote(c.emailUser); break;

    // ----- url -----
    case 'url': v = c.url?.href ?? null; break;
    case 'urlQuoted': v = c.url === null ? null : quote(c.url.href); break;
    case 'urlBareQuoted': v = c.url === null ? null : quote(c.url.bare); break;
    case 'inurlPath': v = c.url !== null && c.url.path.length > 1 ? 'inurl:' + quote(c.url.path.replace(/^\/+|\/+$/g, '')) : null; break;

    // ----- filename -----
    case 'filename': v = c.filenameStem === null ? null : quote(c.filenameStem + '.' + c.filenameExt); break;
    case 'filenameStem': v = quote(c.filenameStem); break;
    case 'filetypeFromFilename': v = c.filenameExt === null ? null : 'filetype:' + c.filenameExt; break;

    // ----- options & template data -----
    case 'filetypeGroup': v = orGroup(fileTypes(c, t).map(x => 'filetype:' + x)); break;
    case 'filetypeFirst': { const f = fileTypes(c, t)[0]; v = f ? 'filetype:' + f : null; break; }
    case 'exclusions': {
      const seen = new Set<string>();
      const parts: string[] = [];
      for (const x of [...c.excludeTerms, ...(t.exclude ?? [])]) {
        const e = exclusion(x, isGeneratable);
        if (!e) continue;
        const key = e.toLowerCase();
        if (seen.has(key)) continue;
        seen.add(key);
        parts.push(e);
      }
      v = parts.join(' ');
      break;
    }
    case 'dates': v = [resolvePlaceholder('after', c, t, catalogs), resolvePlaceholder('before', c, t, catalogs)].filter(x => x !== null).join(' '); break;
    case 'after': v = c.after !== null ? 'after:' + c.after : null; break;
    case 'before': v = c.before !== null ? 'before:' + c.before : null; break;
    case 'keywords': v = orGroup((t.keywords ?? []).map(kw)); break;
    case 'keywordsAnd': v = (t.keywords ?? []).map(kw).join(' '); break;
    case 'platformSites': v = orGroup(platformsFor(t, catalogs).map(p => 'site:' + p.searchDomain)); break;
    case 'organization': v = quote(c.organization); break;
    case 'location': v = quote(c.location); break;
    case 'role': v = quote(c.role); break;
    case 'displayName': v = quote(c.displayName); break;
    case 'context': v = [c.organization, c.location, c.role, c.displayName].filter(x => x !== null).map(quote).join(' '); break;

    default: throw new Error(`Unknown placeholder {${name}}`);
  }
  return nonEmpty(v);
}

function fileTypes(c: GenerationContext, t: QueryTemplate): string[] {
  return c.fileTypes.length > 0 ? c.fileTypes : t.fileTypes ?? c.intent.defaultFileTypes ?? [];
}

export const ordinal = (a: string, b: string): number => (a < b ? -1 : a > b ? 1 : 0);

export function platformsFor(t: QueryTemplate, catalogs: Catalogs): Platform[] {
  const all = catalogs.platforms;
  if (t.platformIds && t.platformIds.length > 0) {
    const byId = new Map(all.platforms.map(p => [p.id, p]));
    return t.platformIds.map(id => byId.get(id)).filter((p): p is Platform => !!p && p.enabled);
  }
  const cats = new Set((t.platformCategories ?? []).map(c => c.toLowerCase()));
  return all.platforms
    .filter(p => p.enabled && cats.has(p.category.toLowerCase()))
    .sort((a, b) => b.weight - a.weight || ordinal(a.id, b.id))
    .slice(0, Math.max(1, t.platformLimit ?? 6));
}
