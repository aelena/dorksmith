// Structural and cross-reference validation of the catalogs. Port of Dorksmith.Api.Catalogs.CatalogValidator.
import { INPUT_TYPES, type Catalogs } from './types.js';
import { ALL_PLACEHOLDERS, placeholdersIn } from './placeholders.js';

const EXTENSION = /^[a-z0-9]{1,12}$/;
const ID = /^[a-z0-9][a-z0-9-]*$/;
const SUPPORT = new Set(['official', 'working', 'unreliable', 'deprecated', 'unknown']);

/** Returns a list of human-readable problems; empty means the catalogs are consistent. */
export function validateCatalogs(catalogs: Catalogs): string[] {
  const errors: string[] = [];
  const { intents, platforms, fileTypes } = catalogs;
  const operatorCatalogs = Object.values(catalogs.operators);

  for (const c of operatorCatalogs) {
    const p = `operators.${c.engine}`;
    if (c.schemaVersion !== 1) errors.push(`${p}: unsupported schemaVersion ${c.schemaVersion}`);
    if (!c.engine?.trim()) errors.push(`${p}: engine is required`);
    if (!c.catalogVersion?.trim()) errors.push(`${p}: catalogVersion is required`);
    if (!c.searchUrlTemplate?.includes('{query}')) errors.push(`${p}: searchUrlTemplate must contain {query}`);
    const seen = new Set<string>();
    for (const op of c.operators) {
      const q = `${p}[${op.token}]`;
      if (!op.token) { errors.push(`${p}: operator with empty token`); continue; }
      if (seen.has(op.token)) errors.push(`${q}: duplicate token`); seen.add(op.token);
      if (!op.name?.trim()) errors.push(`${q}: name is required`);
      if (!op.description?.trim()) errors.push(`${q}: description is required`);
      if (!SUPPORT.has(op.support)) errors.push(`${q}: unknown support '${op.support}'`);
      if (op.support === 'deprecated' && op.generate) errors.push(`${q}: deprecated operators must have generate=false`);
      if (op.generate && op.support !== 'official' && op.support !== 'working') errors.push(`${q}: generate=true requires support official or working`);
      for (const u of op.sourceUrls ?? []) if (!/^https?:\/\//.test(u)) errors.push(`${q}: invalid sourceUrl '${u}'`);
    }
  }
  if (operatorCatalogs.length === 0) errors.push('operators: no operator catalog found');

  if (fileTypes.schemaVersion !== 1) errors.push(`filetypes: unsupported schemaVersion ${fileTypes.schemaVersion}`);
  const exts = new Set<string>();
  for (const e of fileTypes.extensions) {
    if (!EXTENSION.test(e.ext)) errors.push(`filetypes: invalid extension '${e.ext}' (lowercase alphanumerics only)`);
    if (exts.has(e.ext)) errors.push(`filetypes: duplicate extension '${e.ext}'`); exts.add(e.ext);
    if (!fileTypes.groups.some(g => g.id === e.group)) errors.push(`filetypes[${e.ext}]: unknown group '${e.group}'`);
  }
  for (const g of fileTypes.groups) for (const x of g.extensions) if (!exts.has(x)) errors.push(`filetypes.groups[${g.id}]: extension '${x}' not defined`);

  if (platforms.schemaVersion !== 1) errors.push(`platforms: unsupported schemaVersion ${platforms.schemaVersion}`);
  const categories = new Set(platforms.categories.map(c => c.id));
  const platformIds = new Set<string>();
  for (const pl of platforms.platforms) {
    const q = `platforms[${pl.id}]`;
    if (!ID.test(pl.id)) errors.push(`${q}: invalid id`);
    if (platformIds.has(pl.id)) errors.push(`${q}: duplicate id`); platformIds.add(pl.id);
    if (!pl.name?.trim()) errors.push(`${q}: name is required`);
    if (!categories.has(pl.category)) errors.push(`${q}: unknown category '${pl.category}'`);
    if (!pl.profileUrlTemplate?.startsWith('https://') || !pl.profileUrlTemplate.includes('{username}')) errors.push(`${q}: profileUrlTemplate must be https and contain {username}`);
    if (!pl.searchDomain?.trim() || pl.searchDomain.includes('://')) errors.push(`${q}: searchDomain must be a bare host name`);
    if (pl.usernameRule) { try { new RegExp(pl.usernameRule.pattern); } catch { errors.push(`${q}: usernameRule.pattern is not a valid regex`); } }
  }

  if (intents.schemaVersion !== 1) errors.push(`intents: unsupported schemaVersion ${intents.schemaVersion}`);
  if (!intents.catalogVersion?.trim()) errors.push('intents: catalogVersion is required');
  const families = new Set(intents.families.map(f => f.id));
  const groups = new Set(intents.groups.map(g => g.id));
  const intentIds = new Set<string>();
  const templateIds = new Set<string>();
  const blocked = new Set(operatorCatalogs.flatMap(o => o.operators.filter(op => !op.generate && (op.token.endsWith(':') || op.token.endsWith('('))).map(op => op.token.toLowerCase())));

  for (const intent of intents.intents) {
    const q = `intents[${intent.id}]`;
    if (!ID.test(intent.id)) errors.push(`${q}: invalid id`);
    if (intentIds.has(intent.id)) errors.push(`${q}: duplicate id`); intentIds.add(intent.id);
    if (!intent.label?.trim()) errors.push(`${q}: label is required`);
    if (!groups.has(intent.group)) errors.push(`${q}: unknown group '${intent.group}'`);
    if (intent.safety !== 'standard' && intent.safety !== 'defensive-exposure') errors.push(`${q}: safety must be 'standard' or 'defensive-exposure'`);
    if (!intent.compatibleInputTypes?.length) errors.push(`${q}: compatibleInputTypes is required`);
    for (const t of intent.compatibleInputTypes ?? []) if (!INPUT_TYPES.includes(t as never)) errors.push(`${q}: unknown input type '${t}'`);
    for (const x of intent.defaultFileTypes ?? []) if (!exts.has(x)) errors.push(`${q}: defaultFileTypes references unknown extension '${x}'`);
    for (const type of Object.keys(intent.requiresOptionsForInputTypes ?? {})) if (!intent.compatibleInputTypes.includes(type)) errors.push(`${q}: requiresOptionsForInputTypes references incompatible type '${type}'`);
    if (!intent.templates?.length) errors.push(`${q}: at least one template is required`);

    for (const t of intent.templates ?? []) {
      const r = `${q}.templates[${t.id}]`;
      if (!ID.test(t.id)) errors.push(`${r}: invalid id`);
      if (templateIds.has(t.id)) errors.push(`${r}: duplicate template id (ids are global)`); templateIds.add(t.id);
      if (!families.has(t.family)) errors.push(`${r}: unknown family '${t.family}'`);
      if (!t.pattern?.trim()) errors.push(`${r}: pattern is required`);
      if (!t.explanation?.trim()) errors.push(`${r}: explanation is required`);
      if (t.weight < 0 || t.weight > 1000) errors.push(`${r}: weight must be 0..1000`);
      for (const w of t.when ?? []) if (w !== '*' && !intent.compatibleInputTypes.some(c => c.toLowerCase() === w.toLowerCase())) errors.push(`${r}: 'when' contains '${w}' which is not in compatibleInputTypes`);
      const names = placeholdersIn(t.pattern ?? '');
      if (names.length === 0) errors.push(`${r}: pattern has no placeholders`);
      for (const n of [...names, ...(t.requires ?? []), ...(t.optional ?? [])]) if (!ALL_PLACEHOLDERS.has(n)) errors.push(`${r}: unknown placeholder {${n}}`);
      if ((names.includes('keywords') || names.includes('keywordsAnd')) && !(t.keywords?.length)) errors.push(`${r}: {keywords} used without a keywords list`);
      if (names.includes('platformSites') && !(t.platformIds?.length) && !(t.platformCategories?.length)) errors.push(`${r}: {platformSites} used without platformIds or platformCategories`);
      for (const id of t.platformIds ?? []) if (!platformIds.has(id)) errors.push(`${r}: unknown platformId '${id}'`);
      for (const cat of t.platformCategories ?? []) if (!categories.has(cat)) errors.push(`${r}: unknown platform category '${cat}'`);
      for (const x of t.fileTypes ?? []) if (!exts.has(x)) errors.push(`${r}: fileTypes references unknown extension '${x}'`);
      for (const token of (t.pattern ?? '').split(' ').filter(Boolean)) {
        const bare = token.replace(/^[(-]+/, '').toLowerCase();
        const colon = bare.indexOf(':');
        if (colon > 0 && blocked.has(bare.slice(0, colon + 1))) errors.push(`${r}: pattern emits non-generatable operator '${bare.slice(0, colon + 1)}'`);
      }
    }
  }
  return errors;
}
