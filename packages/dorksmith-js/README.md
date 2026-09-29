# dorksmith (npm)

Deterministic search-dork generator and OSINT/SOCMINT query engine. Zero dependencies, ESM, runs in browsers and Node ≥ 20. The same engine that powers the [Dorksmith](https://github.com/aelena/dorksmith) web app, conformance-tested against the same golden fixtures, with the operator, intent, file-type and platform catalogs embedded.

```bash
npm install dorksmith
```

```js
import { generate, expandHandle, validateQuery, searchUrl } from 'dorksmith';

const { variants } = generate({
  input: 'example.com',
  inputType: 'domain',
  intent: 'public-documents',
  options: { fileTypes: ['pdf', 'docx'], excludeTerms: ['jobs'], maxVariants: 3 },
});
for (const v of variants) console.log(v.label, '→', v.query);
// Balanced → site:example.com (filetype:pdf OR filetype:docx) -jobs
// Precise  → site:example.com filetype:pdf -jobs
// Title-focused → site:example.com (filetype:pdf OR filetype:docx) (intitle:report OR intitle:policy OR intitle:presentation OR intitle:"annual report") -jobs

searchUrl(variants[0].query);
// https://www.google.com/search?q=site%3Aexample.com%20(filetype%3Apdf%20OR%20filetype%3Adocx)%20-jobs

const handle = expandHandle({ username: '@alice42', categories: ['developer'], maxPlatforms: 3 });
handle.profiles[0];
// { platformId: 'github', url: 'https://github.com/alice42', searchQuery: 'site:github.com "alice42"', status: 'not-checked', ... }

validateQuery('cache:example.com site:example.com or filetype:.pdf').warnings;
// [ "Lower-case 'or' is treated as an ordinary word; ...", "filetype: values take no leading dot ...", "cache: is deprecated: ..." ]
```

In the browser it works straight from a CDN, which is how a static (GitHub Pages) build of the app can run entirely client-side:

```html
<script type="module">
  import { generate } from 'https://esm.sh/dorksmith';
  console.log(generate({ input: 'Alice Smith', inputType: 'person', intent: 'person-name-variants' }).variants.map(v => v.query));
</script>
```

## API

| Export | Purpose |
|---|---|
| `generate(request, catalogs?, limits?)` | Ranked variants with explanation, operators, warnings and rank reason. Throws `InputValidationError` (`field`, `unprocessable`). |
| `validateQuery(query, engine?, catalogs?)` | Operators used with support state and plain-language warnings. Never rewrites the query. |
| `expandHandle(request, catalogs?, limits?)` | Profile URLs (`status` always `not-checked`) and site-scoped queries from the platform catalog. |
| `inferInputType(text, knownExtensions?)` | Deterministic input-type suggestion (domain, email, url, username, filename, person, keyword). |
| `searchUrl(query, engine?)` | Search-engine URL built with `encodeURIComponent`. |
| `validateCatalogs(catalogs)` | Structural/cross-reference validation, same rules as the API's readiness check. |
| `createEngine(catalogs?, limits?)` | Binds catalogs and limits once. |
| `bundledCatalogs`, `bundledCatalogFiles`, `catalogVersion` | The embedded catalogs. |
| lower-level: `normalizeText`, `tryNormalizeDomain`, `quote`, `safeTerm`, `analyzeQuery`, `expandTemplate`, … | The building blocks, for custom pipelines. |

Request/response shapes are identical to the HTTP API (`POST /api/v1/dorks/generate` etc.), minus `requestId` and `rateLimit`.

### Bring your own catalogs

```js
import { generate, validateCatalogs } from 'dorksmith';
const catalogs = { operators: { google: myOperators }, intents: myIntents, platforms: myPlatforms, fileTypes: myFileTypes };
if (validateCatalogs(catalogs).length) throw new Error('catalog problem');
generate({ input: 'x', inputType: 'keyword', intent: 'general-discovery' }, catalogs);
```

## CLI

```bash
npx dorksmith generate example.com --intent exposed-config-files --max 3
npx dorksmith generate "Alice Smith" --intent person-organization --organization "Example Corp"
npx dorksmith handle alice42 --categories developer --json
npx dorksmith validate 'cache:example.com site:example.com'
npx dorksmith intents
```

## Development

```bash
npm install
npm run sync-catalogs   # copies ../../data/*.json in and regenerates src/catalogs.generated.ts
npm test                # builds, then runs unit tests + the 153 golden fixtures from the monorepo
```

Publishing is done by the `js-package` GitHub workflow when a `js-v*` tag is pushed (see the repository README).
