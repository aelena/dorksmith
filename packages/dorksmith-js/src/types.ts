// Catalog and contract types. They mirror the JSON catalogs in the repository's data/ directory
// and the request/response contracts of the Dorksmith HTTP API, so output is interchangeable.

export type OperatorSupport = 'official' | 'working' | 'unreliable' | 'deprecated' | 'unknown';

export interface OperatorDefinition {
  token: string;
  name: string;
  syntax: string;
  description: string;
  example?: string;
  support: OperatorSupport;
  category: string;
  takesValue: boolean;
  allowMultiple: boolean;
  generate: boolean;
  caveats?: string[];
  sourceUrls?: string[];
}

export interface OperatorCatalog {
  schemaVersion: number;
  catalogVersion: string;
  engine: string;
  engineName: string;
  searchUrlTemplate: string;
  notes?: string[];
  operators: OperatorDefinition[];
}

export interface FileTypeGroup { id: string; label: string; extensions: string[] }
export interface FileTypeDefinition { ext: string; label: string; group: string; weight: number; defensive?: boolean }
export interface FileTypeCatalog {
  schemaVersion: number;
  catalogVersion: string;
  groups: FileTypeGroup[];
  extensions: FileTypeDefinition[];
}

export interface PlatformCategory { id: string; label: string }
export interface UsernameRule { pattern: string; note: string }
export interface Platform {
  id: string;
  name: string;
  category: string;
  profileUrlTemplate: string;
  searchDomain: string;
  enabled: boolean;
  caseSensitive: boolean;
  weight: number;
  caveat?: string;
  usernameRule?: UsernameRule;
}
export interface PlatformCatalog {
  schemaVersion: number;
  catalogVersion: string;
  notes?: string[];
  categories: PlatformCategory[];
  platforms: Platform[];
}

export interface VariantFamily { id: string; label: string }
export interface IntentGroup { id: string; label: string }
export interface QueryTemplate {
  id: string;
  family: string;
  when: string[];
  pattern: string;
  weight: number;
  explanation: string;
  tags?: string[];
  exclude?: string[];
  keywords?: string[];
  fileTypes?: string[];
  platformIds?: string[];
  platformCategories?: string[];
  platformLimit?: number;
  requires?: string[];
  optional?: string[];
  warnings?: string[];
}
export interface Intent {
  id: string;
  label: string;
  group: string;
  description: string;
  safety: 'standard' | 'defensive-exposure';
  compatibleInputTypes: string[];
  tags?: string[];
  requiresOptions?: string[];
  requiresOptionsForInputTypes?: Record<string, string[]>;
  defaultFileTypes?: string[];
  templates: QueryTemplate[];
}
export interface IntentCatalog {
  schemaVersion: number;
  catalogVersion: string;
  notes?: string[];
  families: VariantFamily[];
  groups: IntentGroup[];
  intents: Intent[];
}

/** The four catalogs the engine needs. `operators` is keyed by engine id. */
export interface Catalogs {
  operators: Record<string, OperatorCatalog>;
  intents: IntentCatalog;
  platforms: PlatformCatalog;
  fileTypes: FileTypeCatalog;
}

// ---------- contracts ----------

export type InputType = 'keyword' | 'person' | 'organization' | 'domain' | 'url' | 'username' | 'email' | 'filename' | 'technology';
export const INPUT_TYPES: readonly InputType[] = ['keyword', 'person', 'organization', 'domain', 'url', 'username', 'email', 'filename', 'technology'];

export interface GenerateOptions {
  fileTypes?: string[] | null;
  excludeTerms?: string[] | null;
  after?: string | null;
  before?: string | null;
  site?: string | null;
  maxVariants?: number | null;
  organization?: string | null;
  location?: string | null;
  role?: string | null;
  displayName?: string | null;
}

export interface GenerateRequest {
  input: string;
  inputType: InputType | string;
  intent: string;
  engine?: string | null;
  options?: GenerateOptions | null;
}

export interface DorkVariant {
  id: string;
  label: string;
  family: string;
  query: string;
  explanation: string;
  operators: string[];
  warnings: string[];
  rankReason: string;
}

export interface GenerateResult {
  catalogVersion: string;
  engine: string;
  intent: string;
  inputType: InputType;
  normalizedInput: string;
  variants: DorkVariant[];
}

export interface OperatorUse { token: string; name: string; support: OperatorSupport; generate: boolean }
export interface ValidateQueryResult {
  query: string;
  engine: string;
  operators: OperatorUse[];
  warnings: string[];
  hasErrors: boolean;
}

export interface HandleExpandRequest {
  username: string;
  categories?: string[] | null;
  platformIds?: string[] | null;
  maxPlatforms?: number | null;
}
export interface ProfileCandidate {
  platformId: string;
  platformName: string;
  category: string;
  url: string | null;
  searchQuery: string;
  status: 'not-checked';
  caveat: string | null;
}
export interface HandleQuery { id: string; label: string; query: string; explanation: string }
export interface HandleExpandResult {
  username: string;
  normalizedUsername: string;
  notice: string;
  profiles: ProfileCandidate[];
  queries: HandleQuery[];
  warnings: string[];
  catalogVersion: string;
}

/** Limits the engine enforces; defaults match the HTTP API's appsettings. */
export interface EngineLimits {
  maxQueryLength: number;
  maxVariants: number;
  defaultVariants: number;
  maxExcludeTerms: number;
  maxFileTypes: number;
  engines: string[];
  maxUsernameLength: number;
  maxPlatforms: number;
  defaultPlatforms: number;
}

export const DEFAULT_LIMITS: EngineLimits = {
  maxQueryLength: 500,
  maxVariants: 12,
  defaultVariants: 6,
  maxExcludeTerms: 20,
  maxFileTypes: 10,
  engines: ['google'],
  maxUsernameLength: 100,
  maxPlatforms: 100,
  defaultPlatforms: 30,
};

/** Thrown for invalid requests. `unprocessable` distinguishes 422-style (valid shape, cannot generate) from 400-style errors. */
export class InputValidationError extends Error {
  constructor(message: string, public readonly field: string | null = null, public readonly unprocessable = false) {
    super(message);
    this.name = 'InputValidationError';
  }
}
