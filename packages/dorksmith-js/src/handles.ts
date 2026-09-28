// Expands a handle into public profile URLs and search queries from the platform catalog.
// No network access, no probing: every profile is reported as "not-checked". Port of Dorksmith.Api.Handles.HandleExpander.
import { DEFAULT_LIMITS, InputValidationError, type Catalogs, type EngineLimits, type HandleExpandRequest, type HandleExpandResult, type Platform, type ProfileCandidate } from './types.js';
import { normalizeText, tryNormalizeUsername, isLetterOrDigit } from './normalizer.js';
import { quote } from './quoting.js';
import { ordinal } from './resolver.js';
import { generate } from './generator.js';

export const NOT_CHECKED = 'not-checked' as const;
export const HANDLE_NOTICE = 'URLs are constructed from templates and are NOT verified. A URL that resolves does not prove the account belongs to the person you are researching.';

const HOST_LABEL = /^[A-Za-z0-9-]+$/;

/** RFC 3986 percent-encoding of everything except unreserved characters (matches .NET Uri.EscapeDataString). */
export function escapeDataString(value: string): string {
  return encodeURIComponent(value).replace(/[!'()*]/g, ch => '%' + ch.charCodeAt(0).toString(16).toUpperCase());
}

export function expandHandle(request: HandleExpandRequest, catalogs: Catalogs, limits: EngineLimits = DEFAULT_LIMITS): HandleExpandResult {
  const username = tryNormalizeUsername(request.username, limits.maxUsernameLength);
  if (!username) throw new InputValidationError(`A username without whitespace (max ${limits.maxUsernameLength} chars) is required.`, 'username');

  const categories = new Set(catalogs.platforms.categories.map(c => c.id.toLowerCase()));
  const wanted = (request.categories ?? []).map(c => (c ?? '').trim()).filter(Boolean);
  for (const c of wanted)
    if (!categories.has(c.toLowerCase())) throw new InputValidationError(`Unknown platform category '${c}'. Known: ${[...categories].sort().join(', ')}.`, 'categories');

  const byId = new Map(catalogs.platforms.platforms.map(p => [p.id.toLowerCase(), p]));
  const ids = new Set((request.platformIds ?? []).map(p => (p ?? '').trim()).filter(Boolean).map(p => p.toLowerCase()));
  for (const id of ids) if (!byId.has(id)) throw new InputValidationError(`Unknown platform '${id}'.`, 'platformIds');

  const max = request.maxPlatforms ?? limits.defaultPlatforms;
  if (!Number.isInteger(max) || max < 1 || max > limits.maxPlatforms)
    throw new InputValidationError(`maxPlatforms must be between 1 and ${limits.maxPlatforms}.`, 'maxPlatforms');

  const wantedLower = new Set(wanted.map(w => w.toLowerCase()));
  const platforms = catalogs.platforms.platforms
    .filter(p => p.enabled)
    .filter(p => wantedLower.size === 0 || wantedLower.has(p.category.toLowerCase()))
    .filter(p => ids.size === 0 || ids.has(p.id.toLowerCase()))
    .sort((a, b) => b.weight - a.weight || ordinal(a.id, b.id))
    .slice(0, max);

  const profiles = platforms.map(p => build(p, username));

  const warnings: string[] = [];
  if (profiles.length === 0) warnings.push('No enabled platforms matched the requested filters.');
  if ([...username].some(ch => !isLetterOrDigit(ch) && ch !== '_' && ch !== '.' && ch !== '-'))
    warnings.push('The handle contains characters many platforms do not allow; expect several URLs to be invalid.');

  let queries: HandleExpandResult['queries'] = [];
  try {
    const r = generate({ input: username, inputType: 'username', intent: 'username-exact', engine: 'google', options: { maxVariants: 5 } }, catalogs, limits);
    queries = r.variants.map(v => ({ id: v.id, label: v.label, query: v.query, explanation: v.explanation }));
  } catch (e) {
    if (!(e instanceof InputValidationError)) throw e;
  }

  return {
    username: normalizeText(request.username),
    normalizedUsername: username,
    notice: HANDLE_NOTICE,
    profiles,
    queries,
    warnings,
    catalogVersion: catalogs.intents.catalogVersion,
  };
}

function build(p: Platform, username: string): ProfileCandidate {
  const value = p.caseSensitive ? username : username.toLowerCase();
  const caveats: string[] = [];
  if (p.caveat) caveats.push(p.caveat);

  const schemeEnd = p.profileUrlTemplate.indexOf('://') + 3;
  const hostEnd = p.profileUrlTemplate.indexOf('/', schemeEnd);
  const inHost = p.profileUrlTemplate.indexOf('{username}') < (hostEnd < 0 ? p.profileUrlTemplate.length : hostEnd);
  let url: string | null;
  if (inHost && !HOST_LABEL.test(value)) {
    url = null;
    caveats.push('The handle is used as a sub-domain on this platform and contains characters not valid in a host name.');
  } else {
    url = p.profileUrlTemplate.replace(/\{username\}/g, inHost ? value.toLowerCase() : escapeDataString(value));
  }

  if (p.usernameRule && !new RegExp(p.usernameRule.pattern).test(username))
    caveats.push(p.usernameRule.note || "The handle does not match this platform's username rules.");

  return {
    platformId: p.id,
    platformName: p.name,
    category: p.category,
    url,
    searchQuery: `site:${p.searchDomain} ${quote(username)}`,
    status: NOT_CHECKED,
    caveat: caveats.length === 0 ? null : caveats.join(' '),
  };
}
