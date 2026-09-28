# dorksmith (PyPI)

Deterministic search-dork generator and OSINT/SOCMINT query engine as a pure-Python package with no dependencies (Python ≥ 3.10). Same engine and catalogs as the [Dorksmith](https://github.com/aelena/dorksmith) web app and the npm package, conformance-tested against the same golden fixtures.

```bash
pip install dorksmith
```

```python
from dorksmith import generate, expand_handle, validate_query, search_url

result = generate("example.com", "domain", "public-documents",
                  options={"fileTypes": ["pdf", "docx"], "excludeTerms": ["jobs"], "maxVariants": 3})
for v in result.variants:
    print(f"{v.label:<14} {v.query}")
# Balanced       site:example.com (filetype:pdf OR filetype:docx) -jobs
# Precise        site:example.com filetype:pdf -jobs
# Title-focused  site:example.com (filetype:pdf OR filetype:docx) (intitle:report OR intitle:policy OR intitle:presentation OR intitle:"annual report") -jobs

search_url(result.variants[0].query)
# 'https://www.google.com/search?q=site%3Aexample.com%20%28filetype%3Apdf%20OR%20filetype%3Adocx%29%20-jobs'

handle = expand_handle("@alice42", categories=["developer"], max_platforms=3)
handle.profiles[0].url, handle.profiles[0].status
# ('https://github.com/alice42', 'not-checked')

validate_query("cache:example.com site:example.com or filetype:.pdf").warnings
# ["Lower-case 'or' is treated as an ordinary word; ...", "filetype: values take no leading dot ...", "cache: is deprecated: ..."]
```

Results are dataclasses; `.to_dict()` gives the same camelCase shape as the HTTP API (`POST /api/v1/dorks/generate`), minus `requestId` and `rateLimit`.

## API

| Function | Purpose |
|---|---|
| `generate(input, input_type, intent, *, engine="google", options=None, catalogs=None, limits=DEFAULT_LIMITS)` | Ranked variants with explanation, operators, warnings and rank reason. Raises `InputValidationError` (`.field`, `.unprocessable`). |
| `validate_query(query, engine="google", catalogs=None)` | Operators used with support state and warnings. Never rewrites the query. |
| `expand_handle(username, *, categories=None, platform_ids=None, max_platforms=None, catalogs=None)` | Profile URLs (`status` always `not-checked`) and site-scoped queries. |
| `infer_input_type(text, known_extensions=None)` | Deterministic input-type suggestion. |
| `search_url(query, engine="google")` | Search-engine URL, percent-encoded locally. |
| `validate_catalogs(catalogs)` | Structural/cross-reference validation, same rules as the API's readiness check. |
| `bundled_catalogs()`, `load_catalogs(path)`, `catalog_version()` | Embedded or custom catalogs. |
| lower-level: `normalize_text`, `try_normalize_domain`, `quote`, `safe_term`, `analyze_query`, `expand_template`, … | Building blocks for custom pipelines. |

`options` keys match the HTTP API: `fileTypes`, `excludeTerms`, `after`, `before`, `site`, `maxVariants`, `organization`, `location`, `role`, `displayName`.

### Bring your own catalogs

```python
from dorksmith import load_catalogs, validate_catalogs, generate
catalogs = load_catalogs("path/to/data")      # operators.google.json, intents.json, platforms.json, filetypes.json
assert validate_catalogs(catalogs) == []
generate("x", "keyword", "general-discovery", catalogs=catalogs)
```

## CLI

```bash
dorksmith generate example.com --intent exposed-config-files --max 3
dorksmith generate "Alice Smith" --intent person-organization --organization "Example Corp"
dorksmith handle alice42 --categories developer --json
dorksmith validate 'cache:example.com site:example.com'
dorksmith intents
```

Exit codes: `0` ok, `2` invalid input (or a query with syntax errors for `validate`), `3` valid request that cannot be generated.

## Development

```bash
python -m pip install -e ".[test]"
python scripts/sync_catalogs.py      # copies ../../data/*.json into src/dorksmith/data
python -m pytest -q                  # unit tests + the 151 golden fixtures from the monorepo
python -m build                      # sdist + wheel
```

Publishing is done by the `py-package` GitHub workflow when a `py-v*` tag is pushed, using PyPI trusted publishing (see the repository README).
