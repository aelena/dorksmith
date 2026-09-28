// dorksmith — deterministic search-dork generator and OSINT query workbench engine.
//
//   import { generate, expandHandle, validateQuery, bundledCatalogs } from 'dorksmith';
//   const { variants } = generate({ input: 'example.com', inputType: 'domain', intent: 'public-documents' });
//
// The engine is pure: same request + same catalogs = same output. Pass your own catalogs
// (loaded from the repository's data/ directory or edited copies) to change behaviour without code.

export * from './types.js';
export { normalizeText, tryNormalizeDomain, tryNormalizeUrl, tryNormalizeEmail, tryNormalizeUsername, tryParseFilename, tryParseIsoDate, domainLabel, canonicalize } from './normalizer.js';
export { quote, unquote, safeTerm, safeTerms, operatorValue, orGroup, exclusion, looksLikeSyntax } from './quoting.js';
export { ALL_PLACEHOLDERS, OPTIONAL_BY_DEFAULT, placeholdersIn } from './placeholders.js';
export { analyzeQuery, operatorIndex, GOOGLE_WORD_LIMIT } from './analyzer.js';
export type { QueryAnalysis } from './analyzer.js';
export { expandTemplate, buildContext } from './generator.js';
export type { GenerationContext } from './resolver.js';
export { inferInputType } from './infer.js';
export { validateCatalogs } from './catalog-validator.js';
export { HANDLE_NOTICE, NOT_CHECKED, escapeDataString } from './handles.js';

import { DEFAULT_LIMITS, type Catalogs, type EngineLimits, type GenerateRequest, type GenerateResult, type HandleExpandRequest, type HandleExpandResult, type ValidateQueryResult } from './types.js';
import { generate as generateWith, validateQuery as validateQueryWith } from './generator.js';
import { expandHandle as expandHandleWith } from './handles.js';
import { bundledCatalogs as bundled, intents, platforms, filetypes, operatorsGoogle } from './catalogs.generated.js';

/** The catalogs embedded in this package, assembled into the shape the engine consumes. */
export const bundledCatalogs: Catalogs = {
  operators: { [operatorsGoogle.engine]: operatorsGoogle },
  intents,
  platforms,
  fileTypes: filetypes,
};

/** Raw embedded catalog files keyed by file name (operators.google.json, intents.json, ...). */
export const bundledCatalogFiles = bundled;

/** Version string of the embedded catalogs (from intents.json). */
export const catalogVersion: string = intents.catalogVersion;

/** Generate ranked query variants. Uses the embedded catalogs unless others are supplied. */
export function generate(request: GenerateRequest, catalogs: Catalogs = bundledCatalogs, limits: EngineLimits = DEFAULT_LIMITS): GenerateResult {
  return generateWith(request, catalogs, limits);
}

/** Analyse a hand-written query: operators used, support states and warnings. Never rewrites it. */
export function validateQuery(query: string, engine = 'google', catalogs: Catalogs = bundledCatalogs): ValidateQueryResult {
  return validateQueryWith(query, catalogs, engine);
}

/** Expand a handle into unverified profile URLs and search queries. */
export function expandHandle(request: HandleExpandRequest, catalogs: Catalogs = bundledCatalogs, limits: EngineLimits = DEFAULT_LIMITS): HandleExpandResult {
  return expandHandleWith(request, catalogs, limits);
}

/** Search-engine URL for a query, built client-side; never accept redirect URLs from elsewhere. */
export function searchUrl(query: string, engine = 'google', catalogs: Catalogs = bundledCatalogs): string {
  const template = catalogs.operators[engine]?.searchUrlTemplate ?? 'https://www.google.com/search?q={query}';
  return template.replace('{query}', encodeURIComponent(query));
}

/** Convenience: bind catalogs and limits once. */
export function createEngine(catalogs: Catalogs = bundledCatalogs, limits: EngineLimits = DEFAULT_LIMITS) {
  return {
    catalogs,
    limits,
    catalogVersion: catalogs.intents.catalogVersion,
    generate: (request: GenerateRequest) => generateWith(request, catalogs, limits),
    validateQuery: (query: string, engine = 'google') => validateQueryWith(query, catalogs, engine),
    expandHandle: (request: HandleExpandRequest) => expandHandleWith(request, catalogs, limits),
    searchUrl: (query: string, engine = 'google') => searchUrl(query, engine, catalogs),
  };
}
