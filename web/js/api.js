// In-browser "API": the same call shapes the SPA used against the HTTP service, now answered locally by the
// dorksmith engine (built from packages/dorksmith-js and copied to js/engine/ by `npm run build`).
// Nothing here performs a network request.
import * as engine from './engine/index.js';

const { bundledCatalogs, catalogVersion, DEFAULT_LIMITS, InputValidationError } = engine;

export class ApiError extends Error {
  constructor(status, body) {
    super(body?.message || `Request failed (${status})`);
    this.status = status;
    this.error = body?.error || 'request_failed';
    this.field = body?.field || null;
    this.retryAfterSeconds = body?.retryAfterSeconds ?? null;
  }
}

function run(fn) {
  try {
    return fn();
  } catch (e) {
    if (e instanceof InputValidationError) {
      throw new ApiError(e.unprocessable ? 422 : 400, { error: e.unprocessable ? 'cannot_generate' : 'invalid_input', message: e.message, field: e.field });
    }
    throw new ApiError(500, { error: 'internal_error', message: String(e?.message || e) });
  }
}

const newId = () => Date.now().toString(36).toUpperCase() + Math.random().toString(36).slice(2, 8).toUpperCase();

const config = Object.freeze({
  maxVariants: DEFAULT_LIMITS.maxVariants,
  defaultVariants: DEFAULT_LIMITS.defaultVariants,
  maxQueryLength: DEFAULT_LIMITS.maxQueryLength,
  maxExcludeTerms: DEFAULT_LIMITS.maxExcludeTerms,
  maxFileTypes: DEFAULT_LIMITS.maxFileTypes,
  maxUsernameLength: DEFAULT_LIMITS.maxUsernameLength,
  maxPlatforms: DEFAULT_LIMITS.maxPlatforms,
  supportedEngines: Object.keys(bundledCatalogs.operators),
  usernameSearchEnabled: true,
  runsLocally: true,
  catalogVersion,
});

const publicIntents = () => ({
  catalogVersion,
  families: bundledCatalogs.intents.families,
  groups: bundledCatalogs.intents.groups,
  intents: bundledCatalogs.intents.intents.map(i => ({
    id: i.id, label: i.label, group: i.group, description: i.description, safety: i.safety,
    compatibleInputTypes: i.compatibleInputTypes, tags: i.tags ?? [],
    requiresOptions: i.requiresOptions ?? [], requiresOptionsForInputTypes: i.requiresOptionsForInputTypes ?? {},
    defaultFileTypes: i.defaultFileTypes ?? [], templateCount: i.templates.length,
    families: [...new Set(i.templates.map(t => t.family))],
  })),
});

export const api = {
  config: async () => config,
  operators: async (name = 'google') => {
    const c = bundledCatalogs.operators[name];
    if (!c) throw new ApiError(404, { error: 'not_found', message: `No operator catalog for engine '${name}'.` });
    return c;
  },
  intents: async () => publicIntents(),
  fileTypes: async () => bundledCatalogs.fileTypes,
  platforms: async () => ({
    catalogVersion: bundledCatalogs.platforms.catalogVersion,
    categories: bundledCatalogs.platforms.categories,
    platforms: bundledCatalogs.platforms.platforms.filter(p => p.enabled),
  }),
  generate: async (payload) => run(() => ({ requestId: newId(), ...engine.generate(payload), rateLimit: null })),
  validate: async ({ query, engine: name = 'google' }) => run(() => engine.validateQuery(query, name)),
  expandHandle: async (payload) => run(() => ({ ...engine.expandHandle(payload), rateLimit: null })),
};

/** Search URL built client-side with encodeURIComponent; the only outbound navigation, and user-initiated. */
export function googleSearchUrl(query) {
  return engine.searchUrl(query, 'google');
}
