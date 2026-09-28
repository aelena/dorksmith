// raw input → normalise → classify/validate → load templates → expand placeholders → drop invalid
// → canonicalise → de-duplicate → score/rank → top N with explanations. Fully deterministic.
// Port of Dorksmith.Api.Generation.RequestValidator + DorkGenerator.
import {
  DEFAULT_LIMITS, INPUT_TYPES, InputValidationError,
  type Catalogs, type DorkVariant, type EngineLimits, type GenerateRequest, type GenerateResult, type InputType,
  type OperatorDefinition, type QueryTemplate, type ValidateQueryResult,
} from './types.js';
import {
  canonicalize, normalizeText, tryNormalizeDomain, tryNormalizeEmail, tryNormalizeUrl, tryNormalizeUsername,
  tryParseFilename, tryParseIsoDate, words as splitWords,
} from './normalizer.js';
import { analyzeQuery, operatorIndex } from './analyzer.js';
import { OPTIONAL_BY_DEFAULT } from './placeholders.js';
import { resolvePlaceholder, type GenerationContext } from './resolver.js';
import { rank, type Candidate } from './ranker.js';

const MAX_CONTEXT_LENGTH = 200;
const MAX_EXCLUDE_TERM_LENGTH = 100;

export function catalogVersion(catalogs: Catalogs): string {
  return catalogs.intents.catalogVersion;
}

export function buildContext(request: GenerateRequest, catalogs: Catalogs, limits: EngineLimits = DEFAULT_LIMITS): GenerationContext {
  const o = request.options ?? {};
  const engine = (request.engine ?? '').trim() ? request.engine!.trim().toLowerCase() : 'google';
  const engines = limits.engines.map(e => e.toLowerCase());
  const operators = catalogs.operators[engine];
  if (!engines.includes(engine) || !operators)
    throw new InputValidationError(`Engine '${engine}' is not supported. Supported: ${engines.join(', ')}.`, 'engine');

  const typeId = (request.inputType ?? '').toLowerCase();
  if (!INPUT_TYPES.includes(typeId as InputType))
    throw new InputValidationError(`inputType must be one of: ${INPUT_TYPES.join(', ')}.`, 'inputType');
  const inputType = typeId as InputType;

  const intent = catalogs.intents.intents.find(i => i.id === (request.intent ?? '').trim());
  if (!intent) throw new InputValidationError('Unknown intent. See the intent catalog.', 'intent');
  if (!intent.compatibleInputTypes.some(t => t.toLowerCase() === typeId))
    throw new InputValidationError(`Intent '${intent.id}' does not support inputType '${typeId}'. Compatible: ${intent.compatibleInputTypes.join(', ')}.`, 'intent', true);

  const raw = request.input ?? '';
  if (raw.length > limits.maxQueryLength) throw new InputValidationError(`input exceeds ${limits.maxQueryLength} characters.`, 'input');
  let input = normalizeText(raw);
  if (!input) throw new InputValidationError('input is required.', 'input');

  let domain: string | null = null, username: string | null = null, email: string | null = null, emailUser: string | null = null;
  let stem: string | null = null, ext: string | null = null, url: ReturnType<typeof tryNormalizeUrl> = null;
  let wordList: string[];

  switch (inputType) {
    case 'domain': {
      const d = tryNormalizeDomain(input);
      if (!d) throw new InputValidationError('A valid domain is required for inputType=domain.', 'input');
      domain = d; input = d; wordList = [d];
      break;
    }
    case 'url': {
      const u = tryNormalizeUrl(input);
      if (!u) throw new InputValidationError('A valid http(s) URL is required for inputType=url.', 'input');
      url = u; domain = u.host; input = u.href; wordList = [input];
      break;
    }
    case 'email': {
      const e = tryNormalizeEmail(input);
      if (!e) throw new InputValidationError('A valid email address is required for inputType=email.', 'input');
      email = e.email; emailUser = e.localPart; domain = e.domain; input = e.email; wordList = [e.email];
      break;
    }
    case 'username': {
      const un = tryNormalizeUsername(input, limits.maxUsernameLength);
      if (!un) throw new InputValidationError(`A username without whitespace (max ${limits.maxUsernameLength} chars) is required for inputType=username.`, 'input');
      username = un; wordList = [un];
      break;
    }
    case 'filename': {
      const f = tryParseFilename(input);
      if (!f) throw new InputValidationError('A file name with an extension (report.pdf) is required for inputType=filename.', 'input');
      stem = f.stem; ext = f.extension; wordList = [stem + '.' + ext];
      break;
    }
    default: {
      const w = splitWords(input).words;
      if (w.length === 0) throw new InputValidationError('input must contain at least one word.', 'input');
      wordList = w;
    }
  }

  const knownExt = new Set(catalogs.fileTypes.extensions.map(e => e.ext));
  const fileTypes: string[] = [];
  for (const ft of o.fileTypes ?? []) {
    const x = (ft ?? '').trim().replace(/^\.+/, '').toLowerCase();
    if (!x) continue;
    if (!knownExt.has(x)) throw new InputValidationError(`Unknown file type '${x}'. See the file-type catalog.`, 'options.fileTypes');
    if (!fileTypes.includes(x)) fileTypes.push(x);
  }
  if (fileTypes.length > limits.maxFileTypes) throw new InputValidationError(`At most ${limits.maxFileTypes} file types are allowed.`, 'options.fileTypes');

  const excludes: string[] = [];
  for (const term of o.excludeTerms ?? []) {
    const x = normalizeText(term).replace(/^-+/, '').trim();
    if (!x) continue;
    if (x.length > MAX_EXCLUDE_TERM_LENGTH) throw new InputValidationError(`Excluded terms must be at most ${MAX_EXCLUDE_TERM_LENGTH} characters.`, 'options.excludeTerms');
    if (!excludes.some(e => e.toLowerCase() === x.toLowerCase())) excludes.push(x);
  }
  if (excludes.length > limits.maxExcludeTerms) throw new InputValidationError(`At most ${limits.maxExcludeTerms} excluded terms are allowed.`, 'options.excludeTerms');

  let after: string | null = null, before: string | null = null;
  if (o.after && o.after.trim()) {
    after = tryParseIsoDate(o.after);
    if (!after) throw new InputValidationError('after must be an ISO date (YYYY-MM-DD).', 'options.after');
  }
  if (o.before && o.before.trim()) {
    before = tryParseIsoDate(o.before);
    if (!before) throw new InputValidationError('before must be an ISO date (YYYY-MM-DD).', 'options.before');
  }
  if (after && before && after > before) throw new InputValidationError('after must be on or before the before date.', 'options.after');

  let site: string | null = null;
  if (o.site && o.site.trim()) {
    site = tryNormalizeDomain(o.site);
    if (!site) throw new InputValidationError('site must be a valid domain.', 'options.site');
  }

  const maxVariants = o.maxVariants ?? limits.defaultVariants;
  if (!Number.isInteger(maxVariants) || maxVariants < 1 || maxVariants > limits.maxVariants)
    throw new InputValidationError(`maxVariants must be between 1 and ${limits.maxVariants}.`, 'options.maxVariants');

  const context = (value: string | null | undefined, field: string): string | null => {
    const s = normalizeText(value);
    if (!s) return null;
    if (s.length > MAX_CONTEXT_LENGTH) throw new InputValidationError(`${field} must be at most ${MAX_CONTEXT_LENGTH} characters.`, field);
    return s;
  };

  const ctx: GenerationContext = {
    inputType, input, words: wordList, intent, operators, operatorByToken: operatorIndex(operators), maxVariants,
    domain, url, username, email, emailUser, filenameStem: stem, filenameExt: ext,
    fileTypes, excludeTerms: excludes, after, before, site,
    organization: context(o.organization, 'options.organization'),
    location: context(o.location, 'options.location'),
    role: context(o.role, 'options.role'),
    displayName: context(o.displayName, 'options.displayName'),
  };

  const hasContext = ctx.organization !== null || ctx.location !== null || ctx.role !== null || ctx.displayName !== null;
  const required = new Set([...(intent.requiresOptions ?? []), ...((intent.requiresOptionsForInputTypes ?? {})[typeId] ?? [])].map(r => r.toLowerCase()));
  for (const name of required) {
    const satisfied = ({
      site: ctx.site !== null || ctx.domain !== null,
      dates: ctx.after !== null || ctx.before !== null,
      filetypes: ctx.fileTypes.length > 0,
      excludeterms: ctx.excludeTerms.length > 0,
      organization: ctx.organization !== null,
      location: ctx.location !== null,
      role: ctx.role !== null,
      displayname: ctx.displayName !== null,
      context: hasContext,
    } as Record<string, boolean>)[name] ?? true;
    if (!satisfied) throw new InputValidationError(`Intent '${intent.id}' requires options.${name} for inputType '${typeId}'.`, `options.${name}`, true);
  }
  return ctx;
}

/** Substitutes placeholders; returns null when a required placeholder cannot be resolved. */
export function expandTemplate(template: QueryTemplate, ctx: GenerationContext, catalogs: Catalogs): string | null {
  const optional = new Set(OPTIONAL_BY_DEFAULT);
  for (const o of template.optional ?? []) optional.add(o);
  for (const r of template.requires ?? []) optional.delete(r);
  for (const r of template.requires ?? []) if (resolvePlaceholder(r, ctx, template, catalogs) === null) return null;

  let out = '';
  const pattern = template.pattern;
  let i = 0;
  while (i < pattern.length) {
    const open = pattern.indexOf('{', i);
    if (open < 0) { out += pattern.slice(i); break; }
    const close = pattern.indexOf('}', open + 1);
    if (close < 0) { out += pattern.slice(i); break; }
    out += pattern.slice(i, open);
    const name = pattern.slice(open + 1, close);
    const value = resolvePlaceholder(name, ctx, template, catalogs);
    if (value === null && !optional.has(name)) return null;
    out += value ?? '';
    i = close + 1;
  }
  const query = out.replace(/\(\s*\)/g, ' ').replace(/\s+/g, ' ').trim();
  return query || null;
}

export function generate(request: GenerateRequest, catalogs: Catalogs, limits: EngineLimits = DEFAULT_LIMITS): GenerateResult {
  const ctx = buildContext(request, catalogs, limits);
  const byToken = ctx.operatorByToken;
  const candidates: Candidate[] = [];
  let order = 0;
  for (const template of ctx.intent.templates) {
    order++;
    if (!template.when.some(w => w === '*' || w.toLowerCase() === ctx.inputType)) continue;
    const query = expandTemplate(template, ctx, catalogs);
    if (query === null) continue;
    const analysis = analyzeQuery(query, ctx.operators, byToken);
    if (analysis.operators.some(t => { const op = byToken.get(t); return !!op && !op.generate && op.support === 'deprecated'; })) continue;
    candidates.push({ template, catalogOrder: order, query, canonical: canonicalize(query), analysis, baseScore: 0, baseReason: '' });
  }

  const ranked = rank(candidates, ctx.inputType, byToken, ctx.maxVariants);
  if (ranked.length === 0)
    throw new InputValidationError(`No variants could be generated for intent '${ctx.intent.id}' with inputType '${ctx.inputType}' and the supplied options.`, 'intent', true);

  const familyLabel = new Map(catalogs.intents.families.map(f => [f.id, f.label]));
  const variants: DorkVariant[] = ranked.map(c => ({
    id: c.template.id,
    label: familyLabel.get(c.template.family) ?? c.template.family,
    family: c.template.family,
    query: c.query,
    explanation: c.template.explanation,
    operators: c.analysis.operators,
    warnings: [...new Set([...c.analysis.warnings, ...(c.template.warnings ?? [])])],
    rankReason: c.baseReason,
  }));

  return { catalogVersion: catalogVersion(catalogs), engine: ctx.operators.engine, intent: ctx.intent.id, inputType: ctx.inputType, normalizedInput: ctx.input, variants };
}

/** Expert-mode analysis of a hand-written query. Never rewrites the query. */
export function validateQuery(query: string, catalogs: Catalogs, engine = 'google'): ValidateQueryResult {
  const id = (engine ?? '').trim() ? engine.trim().toLowerCase() : 'google';
  const ops = catalogs.operators[id];
  if (!ops) throw new InputValidationError(`Engine '${id}' is not supported.`, 'engine');
  const normalized = normalizeText(query);
  if (!normalized) throw new InputValidationError('query is required.', 'query');
  const byToken = operatorIndex(ops);
  const analysis = analyzeQuery(normalized, ops, byToken);
  const uses = analysis.operators.map(t => {
    const op: OperatorDefinition | undefined = byToken.get(t);
    return op ? { token: t, name: op.name, support: op.support, generate: op.generate } : { token: t, name: 'Unknown', support: 'unknown' as const, generate: false };
  });
  return { query: normalized, engine: id, operators: uses, warnings: analysis.warnings, hasErrors: analysis.hasErrors };
}
