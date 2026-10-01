# Dorksmith — search query workbench

<!-- badges-start -->
[![Pages](https://img.shields.io/github/actions/workflow/status/aelena/dorksmith/pages.yml?style=flat&logo=github&label=pages)](https://github.com/aelena/dorksmith/actions/workflows/pages.yml) [![npm package CI](https://img.shields.io/github/actions/workflow/status/aelena/dorksmith/js-package.yml?style=flat&logo=github&label=js-package)](https://github.com/aelena/dorksmith/actions/workflows/js-package.yml) [![Python package CI](https://img.shields.io/github/actions/workflow/status/aelena/dorksmith/py-package.yml?style=flat&logo=github&label=py-package)](https://github.com/aelena/dorksmith/actions/workflows/py-package.yml) [![Last commit](https://img.shields.io/github/last-commit/aelena/dorksmith?style=flat)](https://github.com/aelena/dorksmith/commits/main)

[![npm](https://img.shields.io/npm/v/dorksmith?style=flat&logo=npm&label=npm)](https://www.npmjs.com/package/dorksmith) [![npm downloads](https://img.shields.io/npm/dm/dorksmith?style=flat)](https://www.npmjs.com/package/dorksmith) [![PyPI](https://img.shields.io/pypi/v/dorksmith?style=flat&logo=pypi&logoColor=white&label=PyPI)](https://pypi.org/project/dorksmith/) [![PyPI downloads](https://img.shields.io/pypi/dm/dorksmith?style=flat)](https://pypistats.org/packages/dorksmith) [![Python versions](https://img.shields.io/pypi/pyversions/dorksmith?style=flat)](https://pypi.org/project/dorksmith/)

[![Runs in the browser](https://img.shields.io/badge/runs-100%25%20client--side-0f6e6a?style=flat)](#architecture) [![Hosted on GitHub Pages](https://img.shields.io/badge/hosted%20on-GitHub%20Pages-222?style=flat&logo=github)](https://aelena.github.io/dorksmith/) [![TypeScript](https://img.shields.io/badge/engine-TypeScript-3178C6?style=flat&logo=typescript&logoColor=white)](packages/dorksmith-js) [![Python](https://img.shields.io/badge/engine-Python%203.10%2B-3776AB?style=flat&logo=python&logoColor=white)](packages/dorksmith-py) [![Frontend](https://img.shields.io/badge/frontend-vanilla%20JS%2C%20no%20bundler-F7DF1E?style=flat&logo=javascript&logoColor=black)](web)

[![Golden fixtures](https://img.shields.io/badge/golden%20fixtures-153%20shared%20by%20both%20engines-brightgreen?style=flat)](tests/golden) [![E2E: Playwright](https://img.shields.io/badge/e2e-Playwright-45ba4b?style=flat&logo=playwright&logoColor=white)](web/tests) [![No LLM](https://img.shields.io/badge/LLM-none%2C%20deterministic-blue?style=flat)](#main-features) [![License: MIT](https://img.shields.io/github/license/aelena/dorksmith?style=flat)](LICENSE) [![PRs welcome](https://img.shields.io/badge/PRs-welcome-brightgreen?style=flat)](https://github.com/aelena/dorksmith/pulls)
<!-- badges-end -->

```plaintext

██████╗  ██████╗ ██████╗ ██╗  ██╗███████╗███╗   ███╗██╗████████╗██╗  ██╗
██╔══██╗██╔═══██╗██╔══██╗██║ ██╔╝██╔════╝████╗ ████║██║╚══██╔══╝██║  ██║
██║  ██║██║   ██║██████╔╝█████╔╝ ███████╗██╔████╔██║██║   ██║   ███████║
██║  ██║██║   ██║██╔══██╗██╔═██╗ ╚════██║██║╚██╔╝██║██║   ██║   ██╔══██║
██████╔╝╚██████╔╝██║  ██║██║  ██╗███████║██║ ╚═╝ ██║██║   ██║   ██║  ██║
╚═════╝  ╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝╚══════╝╚═╝     ╚═╝╚═╝   ╚═╝   ╚═╝  ╚═╝
                                                                        
```

Most "dork generators" are either a static list of copy-pasted strings from 2012 (half of which still recommend `cache:` and `link:`), or a novelty wrapper around a language model that invents operators. Dorksmith is built like a small query compiler instead.

Dorksmith is a **no-login web app and a pair of libraries that compile advanced search queries ("dorks")** for legitimate research: defensive exposure reviews, OSINT/SOCMINT, journalism, troubleshooting and plain power-searching.

Give it a target, an input type and an intent. It returns several ranked query variants, explains why each exists and which operators it uses, and warns when an operator is unreliable or no longer works. Everything is generated **deterministically from versioned JSON catalogs**, and the web app runs **entirely in your browser**: no server, no LLM, no result scraping, nothing logged.

**Use it:** <https://aelena.github.io/dorksmith/>

```
target  example.com   ·   type  domain   ·   intent  public-documents   ·   file types  pdf, docx, xlsx   ·   exclude  jobs

→ site:example.com (filetype:pdf OR filetype:docx OR filetype:xlsx) -jobs          Balanced
→ site:example.com filetype:pdf -jobs                                               Precise
→ site:example.com (filetype:pdf OR filetype:docx OR filetype:xlsx)
     (intitle:report OR intitle:policy OR intitle:presentation OR intitle:"annual report") -jobs   Title-focused
→ site:example.com (filetype:xlsx OR filetype:xls OR filetype:csv) -jobs            Document-focused
→ "example.com" (filetype:pdf OR filetype:docx OR filetype:xlsx) -site:example.com -jobs   Broad
→ site:example.com (filetype:pptx OR filetype:ppt) -jobs                            Document-focused
```

Same input + same catalog version = same output, every time, in the browser, in Node and in Python.

![Generator screen: target, intent, options and ranked query variants with operator chips](docs/screenshot-generator.png)

---

## Contents

- [Main features](#main-features)
- [Try it or run it locally](#try-it-or-run-it-locally)
- [Using the app](#using-the-app)
- [Examples](#examples)
- [Packages: npm and PyPI](#packages-npm-and-pypi)
- [Catalog maintenance](#catalog-maintenance)
- [Architecture](#architecture)
- [Development and testing](#development-and-testing)
- [Deploying to GitHub Pages](#deploying-to-github-pages)
- [Privacy](#privacy)
- [Safety boundaries](#safety-boundaries)
- [Roadmap](#roadmap)

---

## Main features

| | Dorksmith |
|---|---|
| **Several variants, not one** | Balanced, Precise, Broad, Title-, URL- and Document-focused, Recent, Noise-reduced |
| **Explains itself** | Every variant carries an explanation, the operators it uses and a rank reason |
| **Operator registry is data** | `data/operators.google.json` records `official` / `working` / `unreliable` / `deprecated` per operator, with sources. Deprecated operators are never emitted |
| **Operator-aware autocomplete** | `si` → `site:`, `filetype:p` → `pdf`, a domain suggests domain intents |
| **OSINT/SOCMINT workflows** | 36 intents including username discovery across 85 platforms, without probing anything |
| **Runs anywhere** | In the browser as a static page, in Node or the browser as an npm package, in Python as a PyPI package, and on the command line |
| **Nothing to host, nothing logged** | The web app is static files; generation happens on your device |
| **No account, ever** | No registration, no cookies, no profiles |

---

## Try it or run it locally

The app is published at **<https://aelena.github.io/dorksmith/>** from the `main` branch by the `pages` workflow.

To run it locally you need Node ≥ 20 (used only to build the engine and serve files; the app itself has no bundler):

```bash
git clone https://github.com/aelena/dorksmith.git
cd dorksmith
npm run build      # builds packages/dorksmith-js and copies the engine into web/js/engine
npm run serve      # http://localhost:5080
```

Any static file server works instead of `npm run serve` (e.g. `python -m http.server 5080 -d web`), as long as the engine has been copied in by `npm run build`.

Prefer the command line? `npx dorksmith generate example.com --intent public-documents` or `pip install dorksmith && dorksmith generate example.com --intent public-documents`.

---

## Using the app

Four sections, switchable with the tabs or <kbd>Alt</kbd>+<kbd>G</kbd> / <kbd>U</kbd> / <kbd>O</kbd> / <kbd>A</kbd> (the underlined initial of each tab):

1. **Generator** — target, input type (auto-detected, overridable), intent, options, results.
2. **Username Search** — expands a handle into profile URLs and site-scoped queries by platform category.
3. **Operator Guide** — searchable operator cards with syntax, example, support badge, caveats and source links.
4. **About** — usage notice, keyboard reference, opt-in local history, catalog and engine versions.

Every result card has **Copy** and **Open in Google** (the URL is built with `encodeURIComponent`; opening it is the only outbound action, and you take it). **Copy all** copies every variant, one per line. **More precise** / **Broader** adjust the structured controls and regenerate.

Keyboard: <kbd>/</kbd> focuses the target box, <kbd>Ctrl</kbd>/<kbd>⌘</kbd>+<kbd>↵</kbd> generates, <kbd>↑</kbd> <kbd>↓</kbd> <kbd>↵</kbd> drive suggestions, <kbd>Esc</kbd> closes them.

### Input types

| Type | Detected from | Notes |
|---|---|---|
| `keyword` | anything else | multi-word phrases get quoted/wildcard variants |
| `person` | 2–3 capitalised words (`Alice Smith`) | reversed, initial and `*` middle-name variants |
| `organization` | choose manually | organisation name as an exact phrase |
| `domain` | `example.com`, `https://www.example.com/x` | scheme, path, port and `www.` are stripped |
| `url` | anything with a path | scheme-less citations, `inurl:` of the path |
| `username` | `@alice42` | `@` removed for URLs, kept as a quoted `"@alice42"` variant |
| `email` | `alice@example.com` | local part and domain are used separately |
| `filename` | `report.pdf` | stem + `filetype:` from the extension |
| `technology` | choose manually | "powered by", job postings, StackShare |

### Intents (36)

| Group | Intents |
|---|---|
| General discovery | broad web discovery · exact phrase · domain/site scoped · title-focused · URL-focused · page-text focused · date-bounded · exclude noise · documents by file type · URL references · troubleshooting |
| Organisation / domain OSINT | public documents · sub-domain references · contact pages · staff/profile references · technology mentions · developer references · cloud/storage references · support/status pages · presentations/reports |
| **Defensive exposure checks** | exposed configuration files · backups/archives · logs/diagnostics · directory listings · API documentation · error/debug pages · source maps · repository references |
| Person / identity OSINT | name variants · name + organisation · name + location · name + role · documents mentioning the person · social-profile-focused · email references |
| SOCMINT / username | platform-scoped profile search · exact username · username + display name |

Defensive intents are labelled in the UI and only accept `domain` input. They stop at query generation: nothing is fetched, verified or downloaded.

![Username Search: profile URLs grouped by platform category, all marked not checked](docs/screenshot-handles.png)

---

## Examples

The examples use the CLI (`npx dorksmith …` or `dorksmith …` after `pip install dorksmith`); the web app produces the same variants from the same inputs. `--json` prints the full payload with `label`, `explanation`, `operators`, `warnings` and `rankReason` per variant.

### 1. Documents about a topic, recent only

```bash
dorksmith generate "quarterly roadmap" --intent documents --filetypes pdf,pptx --after 2025-01-01 --max 4
```
```
[Document-focused]  "quarterly roadmap" (filetype:pdf OR filetype:pptx) after:2025-01-01
[Precise]           "quarterly roadmap" filetype:pdf
[Title-focused]     intitle:"quarterly roadmap" (filetype:pdf OR filetype:pptx)
[Broad]             quarterly roadmap (filetype:pdf OR filetype:pptx)
```

### 2. Defensive exposure check on your own domain

```bash
dorksmith generate example.com --intent exposed-config-files
```
```
site:example.com (filetype:env OR filetype:ini OR filetype:cfg OR filetype:conf OR filetype:yml OR filetype:yaml OR filetype:toml OR filetype:properties)
site:example.com (inurl:".env" OR inurl:"config.php" OR inurl:"settings.py" OR inurl:"appsettings.json" OR inurl:"web.config" OR inurl:"wp-config")
site:example.com (intitle:"index of" OR intext:"parent directory") (".env" OR config OR settings)
site:example.com (inurl:docker-compose OR inurl:Dockerfile OR inurl:".htaccess" OR inurl:"nginx.conf" OR inurl:"httpd.conf")
site:example.com (inurl:".git" OR inurl:".svn" OR inurl:".hg" OR inurl:".DS_Store")
site:*.example.com -inurl:www (filetype:env OR filetype:ini OR filetype:cfg OR filetype:conf OR filetype:yml OR filetype:yaml OR filetype:toml OR filetype:properties)
```

Override the default extensions with `--filetypes yml,yaml` to focus on CI and Kubernetes manifests.

### 3. Sub-domains, without touching DNS

```bash
dorksmith generate example.com --intent subdomain-references --max 3
```
```
site:*.example.com -inurl:www
site:*.example.com -site:www.example.com -site:example.com
site:*.example.com (inurl:dev OR inurl:staging OR inurl:test OR inurl:uat OR inurl:api OR inurl:admin OR inurl:portal) -inurl:www
```

### 4. Person + organisation, with proximity

```bash
dorksmith generate "Alice Smith" --type person --intent person-organization --organization "Example Corp" --max 3
```
```
"Alice Smith" "Example Corp"
   ↳ Both the name and the organisation as exact phrases.
"Alice Smith" AROUND(5) "Example Corp"
   ↳ AROUND(5) requires the phrases to be within five words, which filters incidental co-mentions.
"Alice Smith" "Example Corp" (filetype:pdf OR filetype:docx OR filetype:pptx)
   ↳ Documents mentioning both.
```

`person-organization`, `person-location`, `person-role` and `username-display-name` require the matching option; the engine raises an `InputValidationError` with `field: "options.organization"` (exit code 3 on the CLI) if it is missing.

### 5. Name variants

```bash
dorksmith generate "Alice Smith" --type person --intent person-name-variants --max 5
```
```
"Alice Smith"
"Alice * Smith"        ← wildcard catches middle names and initials
"Smith Alice"          ← directory / citation ordering
intitle:"Alice Smith"
"Alice Smith" -site:pinterest.com -site:amazon.com -site:ebay.com
```

Raise `--max` to see the remaining variants, e.g. `"A. Smith"` (first initial + surname) and the date-bounded one when `--after`/`--before` are supplied.

### 6. Username across platforms

```bash
dorksmith generate @alice42 --intent username-profiles --max 3
```
```
"alice42" (site:x.com OR site:instagram.com OR site:facebook.com OR site:tiktok.com OR site:threads.net OR site:bsky.app)
"alice42" (site:github.com OR site:stackoverflow.com OR site:gitlab.com OR site:bitbucket.org OR site:news.ycombinator.com)
"alice42" (site:linkedin.com OR site:xing.com OR site:wellfound.com OR site:about.me)
```

Platform lists come from `data/platforms.json`, ordered by weight, so editing the catalog changes the queries without a code change.

### 7. Expand a handle into profile URLs

```bash
dorksmith handle alice42 --categories developer --max 3 --json
```
```json
{
  "username": "alice42",
  "normalizedUsername": "alice42",
  "notice": "URLs are constructed from templates and are NOT verified. A URL that resolves does not prove the account belongs to the person you are researching.",
  "profiles": [
    { "platformId": "github", "platformName": "GitHub", "category": "developer",
      "url": "https://github.com/alice42", "searchQuery": "site:github.com \"alice42\"", "status": "not-checked", "caveat": null },
    { "platformId": "stackoverflow", "platformName": "Stack Overflow", "category": "developer",
      "url": "https://stackoverflow.com/users/alice42", "searchQuery": "site:stackoverflow.com \"alice42\"",
      "status": "not-checked", "caveat": "Stack Overflow user URLs need a numeric id; use the search query instead." },
    { "platformId": "gitlab", "platformName": "GitLab", "category": "developer",
      "url": "https://gitlab.com/alice42", "searchQuery": "site:gitlab.com \"alice42\"", "status": "not-checked", "caveat": null }
  ],
  "queries": [ { "id": "ue-quoted", "label": "Balanced", "query": "\"alice42\"", "explanation": "The handle as an exact phrase." }, "…" ],
  "warnings": [],
  "catalogVersion": "2026-09-24"
}
```

`status` is always `not-checked`. Handles that are used as a sub-domain (`{username}.tumblr.com`) but contain characters invalid in a host name get `url: null` plus a caveat instead of a broken link.

### 8. Email address references

```bash
dorksmith generate alice@example.com --intent email-mentions
```
```
"alice@example.com"
"alice@example.com" -site:example.com           ← published elsewhere
"alice" site:example.com                        ← the local part on its own domain
"alice@example.com" (filetype:pdf OR filetype:docx OR filetype:xlsx OR filetype:txt)
"alice@example.com" (site:github.com OR site:linkedin.com OR site:x.com OR site:facebook.com)
intext:"alice@example.com"
```

### 9. Troubleshooting an error message

```bash
dorksmith generate "ECONNRESET socket hang up" --type technology --intent troubleshooting --max 3
```
```
"ECONNRESET socket hang up" (site:stackoverflow.com OR site:github.com OR site:serverfault.com OR site:superuser.com)
"ECONNRESET socket hang up" site:github.com inurl:issues
"ECONNRESET socket hang up" (inurl:docs OR inurl:documentation OR intitle:documentation)
```

### 10. Validate a hand-written query (expert mode)

```bash
dorksmith validate 'cache:example.com site:example.com or filetype:.pdf' --json
```
```json
{
  "query": "cache:example.com site:example.com or filetype:.pdf",
  "engine": "google",
  "operators": [
    { "token": "cache:", "name": "Cached copy", "support": "deprecated", "generate": false },
    { "token": "site:", "name": "Site restriction", "support": "official", "generate": true },
    { "token": "filetype:", "name": "File type", "support": "official", "generate": true }
  ],
  "warnings": [
    "Lower-case 'or' is treated as an ordinary word; use upper-case OR for a boolean operator.",
    "filetype: values take no leading dot (filetype:pdf).",
    "cache: is deprecated: No longer works. Use the Wayback Machine (web.archive.org) instead."
  ],
  "hasErrors": false
}
```

The query is never rewritten — validation only. The web app runs the same analysis live under the target box.

### 11. User text never becomes syntax

```bash
dorksmith generate '-secret site:evil.com OR' --max 2
```
```
"-secret" "site:evil.com" "OR"
"-secret site:evil.com OR"
```

Tokens that look like operators are quoted, so a pasted string cannot smuggle `-site:` exclusions or `OR` logic into a generated query. Excluded terms do allow known, generatable operators (`site:pinterest.com` → `-site:pinterest.com`), but deprecated ones are neutralised (`cache:x` → `-"cache:x"`).

### 12. Quoted phrases and handles inside free text are kept

```bash
dorksmith generate 'aelena "antonio elena"' --max 3
dorksmith generate '@aelena antonio elena' --max 2
```
```
aelena "antonio elena"                   ← Balanced: your phrase stays a phrase
"aelena antonio elena"                   ← Precise: the whole input as one phrase
intitle:"aelena antonio elena"

"@aelena" antonio elena                  ← a leading @ is quoted so Google does not read it as the social operator
"@aelena antonio elena"
```

The Broad variant treats the phrase as one alternative: `(aelena OR "antonio elena")`.

### 13. As a library

```js
import { generate, expandHandle, validateQuery, searchUrl } from 'dorksmith';   // Node or browser
const { variants } = generate({ input: 'example.com', inputType: 'domain', intent: 'public-documents', options: { fileTypes: ['pdf'] } });
```

```python
from dorksmith import generate, expand_handle, validate_query
result = generate("example.com", "domain", "public-documents", options={"fileTypes": ["pdf"]})
```

Both throw/raise `InputValidationError` with `field` and `unprocessable` (true when the request is well-formed but cannot be generated, e.g. an intent that needs an option).

---

## Packages: npm and PyPI

| Package | Folder | Install | Runtime | Tests |
|---|---|---|---|---|
| [`dorksmith` on npm](https://www.npmjs.com/package/dorksmith) ([source](packages/dorksmith-js)) | `packages/dorksmith-js` | `npm install dorksmith` | ESM, zero dependencies, browsers and Node ≥ 20, TypeScript types, CLI | 172 (unit + golden) |
| [`dorksmith` on PyPI](https://pypi.org/project/dorksmith/) ([source](packages/dorksmith-py)) | `packages/dorksmith-py` | `pip install dorksmith` | pure Python ≥ 3.10, zero dependencies, typed, CLI | 183 (unit + golden) |

Both packages are conformance-tested against the same 153 golden fixtures, so they produce identical queries for identical input. The web app uses the npm package's build directly. Each package README documents its full API.

### Releasing

Each package has its own workflow (`.github/workflows/js-package.yml`, `py-package.yml`) that tests on every change under its folder, `data/` or `tests/golden/`, and publishes when a version tag is pushed. Both registries use trusted publishing (OIDC), so no credentials are stored anywhere. Published so far: npm 0.2.0, PyPI 0.2.0 (versions are kept aligned).

```bash
# npm: bump packages/dorksmith-js/package.json, then
git tag js-v0.2.1 && git push origin js-v0.2.1

# PyPI: bump packages/dorksmith-py/pyproject.toml (and __version__), then
git tag py-v0.2.1 && git push origin py-v0.2.1
```

The workflows refuse to publish when the tag does not match the version in the manifest. A tag can be re-pushed to retry a failed publish (`git push --delete origin js-v0.2.1 && git push origin js-v0.2.1`) as long as that version is not already on the registry.

One-time setup (already done for this repository; documented for forks):

- **npm** — trusted publishing is configured on the *package's* settings page, so the package must exist first. Publish `0.1.0` once from a workstation (`cd packages/dorksmith-js && npm login && npm publish --access public`; npm requires 2FA on the account), then on npmjs.com open the package → Settings → Trusted Publisher → GitHub Actions: user `aelena`, repository `dorksmith`, workflow filename `js-package.yml`, environment `npm`, allowed action `npm publish`. Create the matching `npm` environment in the GitHub repository settings.
- **PyPI** — no bootstrap needed. On pypi.org add a *pending* trusted publisher for project `dorksmith`: owner `aelena`, repository `dorksmith`, workflow `py-package.yml`, environment `pypi`. Create the matching `pypi` environment in the GitHub repository settings.

Once on npm, the ESM package is also served by CDNs such as jsDelivr, unpkg and esm.sh (`import { generate } from 'https://esm.sh/dorksmith'`).

---

## Catalog maintenance

All knowledge lives in `data/` and is validated by both engines (`validateCatalogs` / `validate_catalogs`) and by the package tests. `data/` is the single source of truth; the packages embed copies that a sync script refreshes (`npm run sync-catalogs` in `packages/dorksmith-js`, `python scripts/sync_catalogs.py` in `packages/dorksmith-py`) and CI fails on drift.

| File | Contents |
|---|---|
| `operators.google.json` | 40 operators: token, syntax, description, example, `support`, `generate`, caveats, source URLs |
| `intents.json` | 8 variant families, 5 groups, 36 intents, ~230 templates |
| `filetypes.json` | 55 extensions in 6 groups with autocomplete weights and a `defensive` flag |
| `platforms.json` | 85 platforms in 8 categories with URL templates, search domains and optional username rules |

### Adding or changing an operator

```json
{
  "token": "related:",
  "name": "Related sites",
  "support": "deprecated",
  "generate": false,
  "caveats": ["Removed; still appears in many third-party lists."],
  "sourceUrls": ["https://developers.google.com/search/updates"]
}
```

Rules enforced by the validators: `deprecated` ⇒ `generate:false`; `generate:true` ⇒ `official` or `working`; no template pattern may contain a non-generatable operator. Bump `catalogVersion` when you change behaviour.

### Adding a template

Templates are patterns with placeholders. Composite placeholders always resolve to complete syntax or to nothing, so a template never leaves a dangling `site:` behind:

```json
{
  "id": "pd-domain-sheets",
  "family": "document-focused",
  "when": ["domain"],
  "pattern": "{site} (filetype:xlsx OR filetype:xls OR filetype:csv) {exclusions}",
  "weight": 75,
  "explanation": "Spreadsheets and data exports on the domain."
}
```

Frequently used placeholders:

| Placeholder | Resolves to |
|---|---|
| `{terms}` / `{quoted}` / `{termsAny}` | `quarterly roadmap` / `"quarterly roadmap"` / `(quarterly OR roadmap)` |
| `{intitle}` `{inurl}` `{intext}` (+`Any`) | `intitle:"quarterly roadmap"`, `(inurl:quarterly OR inurl:roadmap)` |
| `{phraseWildcard}` `{quotedReversed}` `{quotedInitialLast}` | `"Alice * Smith"`, `"Smith Alice"`, `"A. Smith"` |
| `{site}` `{siteWildcard}` `{excludeSite}` `{domainQuoted}` `{domainLabel}` `{atDomainQuoted}` | `site:example.com`, `site:*.example.com`, `-site:example.com`, `"example.com"`, `"example"`, `"@example.com"` |
| `{quotedUsername}` `{atUsername}` `{inurlUsername}` `{hashtagUsername}` | `"alice42"`, `"@alice42"`, `inurl:alice42`, `#alice42` |
| `{emailQuoted}` `{emailUserQuoted}` · `{urlQuoted}` `{urlBareQuoted}` `{inurlPath}` · `{filenameStem}` `{filetypeFromFilename}` | per input type |
| `{filetypeGroup}` `{filetypeFirst}` | from the request's file types, else the template's `fileTypes`, else the intent's `defaultFileTypes` |
| `{exclusions}` `{dates}` `{after}` `{before}` `{siteOption}` `{context}` | optional by default — empty when not supplied |
| `{keywords}` | OR-group of the template's `keywords` list |
| `{platformSites}` | `(site:a OR site:b …)` from `platformIds` or `platformCategories` + `platformLimit` |
| `{organization}` `{location}` `{role}` `{displayName}` | quoted option values |

A placeholder that resolves to nothing disqualifies the template unless it is optional (`exclusions`, `dates`, `after`, `before`, `site`, `siteOption`, `context`) — override per template with `requires` / `optional`. The full list is in `packages/dorksmith-js/src/placeholders.ts` (mirrored in `packages/dorksmith-py/src/dorksmith/placeholders.py`).

**Every template must be covered by a golden fixture.** Fixtures live in `tests/golden/*.json` (input + type + intent + options → expected ordered variants) and are the contract between the JS and Python engines. After adding or changing templates:

```bash
cd packages/dorksmith-js && npm run sync-catalogs
DORKSMITH_UPDATE_GOLDEN=1 npm test        # rewrites the expected output of every fixture from the JS engine
git diff ../../tests/golden               # review the change
npm test                                  # coverage test fails if a template has no fixture
cd ../dorksmith-py && python scripts/sync_catalogs.py && python -m pytest -q   # Python must agree
```

To cover a new template, add a fixture file with `"expected": []` and the request that reaches it, then run the update step.

### Adding a platform

```json
{ "id": "codeberg", "name": "Codeberg", "category": "developer",
  "profileUrlTemplate": "https://codeberg.org/{username}", "searchDomain": "codeberg.org",
  "enabled": true, "caseSensitive": false, "weight": 50,
  "caveat": "optional text shown to the user",
  "usernameRule": { "pattern": "^[A-Za-z0-9-]+$", "note": "shown when the handle does not match" } }
```

Templates must be `https://` and contain `{username}`. Set `enabled:false` to keep a record without emitting it.

Sources used for the operator catalog: Google Search Help (*Refine Google searches*), Google Search Central updates, Google Guide's operator reference and Ahrefs' operator list. First-party documentation wins on conflicts; a Google removal notice always overrides third-party lists.

---

## Architecture

```
raw input → normalise → classify/validate → load intent templates → resolve placeholders
          → drop invalid → canonicalise → de-duplicate → score & rank → top N + explanations
```

The pipeline is a pure function of (request, catalogs). It is implemented twice, in TypeScript and in Python, and the 153 golden fixtures keep the two byte-for-byte identical.

```
data/                        versioned catalogs — the single source of truth
tests/golden/                153 golden fixtures shared by both engines
packages/dorksmith-js/       TypeScript engine → npm "dorksmith" (also the engine of the web app)
  src/normalizer.ts · quoting.ts · resolver.ts · analyzer.ts · ranker.ts · generator.ts · handles.ts
  src/catalog-validator.ts · infer.ts · cli.ts · catalogs.generated.ts (embedded data/)
packages/dorksmith-py/       Python engine → PyPI "dorksmith", same module layout
web/                         the app: index.html + css/app.css + ES modules, no bundler
  js/api.js                  in-page "API": same call shapes the old HTTP API had, answered by js/engine/
  js/engine/                 generated copy of the npm package build (git-ignored, `npm run build`)
  js/app.js · generator-ui.js · autocomplete.js · validator.js · operators-ui.js · handles-ui.js
  tests/                     Playwright browser tests (test-only Node dependency)
scripts/                     copy-engine.mjs (build step), serve.mjs (dependency-free static server)
.github/workflows/           pages.yml (build + test + deploy the app), js-package.yml, py-package.yml
```

Ranking (`ranker`):

```
score = template_weight + intent_match (+10) + input_type_match (+10)
      + operator_reliability (official +4, working +2, unreliable −10, unknown −15)
      − deprecated_penalty (−30) − complexity (−3 per operator beyond 4) − long_query (−5)
      + diversity_bonus (+15 for the first variant of each family)
      − similarity_penalty (up to −20 when Jaccard token overlap with a selected variant > 0.7)
```

Ties break on catalog order, so output is stable. Duplicates are removed after canonicalisation (collapsed whitespace, lower-cased operator prefixes, upper-cased `OR`).

Web security without a server: the page declares its own Content-Security-Policy (`default-src 'self'`, no inline script or style, `connect-src 'self'`), never uses `innerHTML` with data, and the only outbound navigation is the user-initiated "Open in Google" link with `rel="noopener noreferrer"`.

---

## Development and testing

```bash
npm run build                                   # engine → web/js/engine
npm run serve                                   # http://localhost:5080
npm test                                        # Playwright suite (needs `npx playwright install chromium` once)

cd packages/dorksmith-js && npm test            # 172 tests: unit + golden fixtures
cd packages/dorksmith-py && python -m pip install -e ".[test]" && python -m pytest -q   # 183 tests
```

The browser suite covers the generate flow, copy, autocomplete keyboard navigation, operator guide search and filters, username expansion, 400/422 rendering, keyboard shortcuts, and asserts that no request leaves the page after load.

Workflows:

| Workflow | Trigger | Does |
|---|---|---|
| `pages.yml` | push to `main`, manual | build engine → engine tests → Playwright against the static site → upload `web/` → deploy to GitHub Pages |
| `js-package.yml` | changes under `packages/dorksmith-js`, `data/`, `tests/golden/`; tags `js-v*` | test matrix (Node 20/22), catalog-drift check, publish to npm on tag |
| `py-package.yml` | changes under `packages/dorksmith-py`, `data/`, `tests/golden/`; tags `py-v*` | test matrix (Python 3.10–3.13), catalog-drift check, build + publish to PyPI on tag |

---

## Deploying to GitHub Pages

The `pages` workflow already builds and deploys `web/` on every push to `main`. What the repository owner has to do once:

1. Repository → **Settings → Pages**.
2. Under **Build and deployment → Source**, choose **GitHub Actions** (not "Deploy from a branch").
3. Push to `main` or run the `pages` workflow manually (Actions → pages → Run workflow). The first run creates the `github-pages` environment automatically.
4. The site is served at `https://<owner>.github.io/<repository>/`, i.e. <https://aelena.github.io/dorksmith/>. The app uses relative asset paths and hash routing, so it works under that sub-path without configuration.

No organisation is required: a project site is published from a personal account. If you later want a custom domain, add it under Settings → Pages → Custom domain, create the DNS `CNAME` (or `A`/`AAAA` records for an apex) it shows, and commit a `web/CNAME` file containing the domain so deployments keep it.

Forks work the same way; the only thing to change is the "Use it" link in this README.

---

## Privacy

- The app is static. Generation, validation and handle expansion run in your browser from embedded catalogs; the page performs no network requests after loading its own files (the browser test suite asserts this).
- Nothing is logged, there is no server-side component and no analytics.
- No cookies. Theme preference, optional target history and suggestion frequencies live in `localStorage`, only if you opt in, and can be cleared from the About tab.
- "Open in Google" is a normal link you click; what happens after that is between you and the search engine.

---

## Safety boundaries

Dorksmith generates public-search queries and public profile URLs. It does not, and will not:

- fetch, scrape or parse search-engine result pages;
- probe platforms to determine whether an account exists (Sherlock/Maigret style);
- validate, use or store discovered secrets, passwords or tokens;
- download exposed datasets or enumerate targets at scale;
- offer proxy rotation, credential stuffing, exploitation or intrusion functionality.

Defensive exposure checks are for systems you own or are authorised to assess. Users are responsible for lawful use; the notice is shown in the app.

---

## Roadmap

- Bing / DuckDuckGo operator catalogs (the engine is already keyed by `engine`).
- Permalinks encoded entirely in the URL fragment.
- Export variants as TXT/JSON/CSV; local favourites and reusable recipes.
- Side-by-side variant comparison and organisation research packs.
- Signed, versioned catalog releases.

---

Licence: MIT. Working name "Dorksmith"; rename freely.
