// Unit tests mirroring the C# suites for the pieces the golden fixtures do not exercise directly.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  normalizeText, tryNormalizeDomain, domainLabel, tryNormalizeUrl, tryNormalizeUsername, tryNormalizeEmail, tryParseFilename, tryParseIsoDate, canonicalize,
  quote, safeTerm, operatorValue, orGroup, exclusion,
  generate, validateQuery, expandHandle, inferInputType, analyzeQuery, bundledCatalogs, InputValidationError, escapeDataString, searchUrl,
} from '../dist/index.js';

const google = bundledCatalogs.operators.google;
const isGen = token => { const op = google.operators.find(o => o.token === token); return !!op && op.generate; };

test('normalizeText collapses whitespace, maps quotes, keeps case', () => {
  assert.equal(normalizeText('  hello   world  '), 'hello world');
  assert.equal(normalizeText('“quarterly roadmap”'), '"quarterly roadmap"');
  assert.equal(normalizeText('it’s'), "it's");
  assert.equal(normalizeText('ctrl\u0001char'), 'ctrlchar');
  assert.equal(normalizeText('Alice Smith'), 'Alice Smith');
});

test('domain normalisation', () => {
  for (const [i, e] of [['example.com', 'example.com'], ['https://www.example.com/path?q=1', 'example.com'], ['EXAMPLE.COM.', 'example.com'],
    ['sub.example.co.uk:8443', 'sub.example.co.uk'], ['*.example.com', 'example.com'], ['www.example.com', 'example.com'], ['192.168.1.10', '192.168.1.10']])
    assert.equal(tryNormalizeDomain(i), e, i);
  for (const bad of ['', 'not a domain', 'localhost', 'example', '-bad.example.com', 'example.c']) assert.equal(tryNormalizeDomain(bad), null, bad);
  assert.equal(domainLabel('www.example.co.uk'), 'example');
  assert.equal(domainLabel('docs.example.org'), 'example');
});

test('url normalisation', () => {
  assert.equal(tryNormalizeUrl('https://Example.com/Path/').href, 'https://example.com/Path');
  assert.equal(tryNormalizeUrl('example.com/docs/api').href, 'https://example.com/docs/api');
  assert.equal(tryNormalizeUrl('http://example.com:80/x#frag').href, 'http://example.com/x');
  assert.equal(tryNormalizeUrl('https://example.com/a/b/').bare, 'example.com/a/b');
  for (const bad of ['ftp://example.com/file', 'javascript:alert(1)', 'not a url']) assert.equal(tryNormalizeUrl(bad), null, bad);
});

test('username, email, filename, date', () => {
  assert.equal(tryNormalizeUsername('@@alice', 100), 'alice');
  assert.equal(tryNormalizeUsername('has space', 100), null);
  assert.equal(tryNormalizeUsername('a'.repeat(101), 100), null);
  assert.deepEqual(tryNormalizeEmail('Alice.Smith@Example.COM'), { email: 'Alice.Smith@example.com', localPart: 'Alice.Smith', domain: 'example.com' });
  assert.equal(tryNormalizeEmail('alice@localhost'), null);
  assert.deepEqual(tryParseFilename('Annual Report 2024.PDF'), { stem: 'Annual Report 2024', extension: 'pdf' });
  assert.equal(tryParseIsoDate('2024-01-31'), '2024-01-31');
  assert.equal(tryParseIsoDate('2024-13-01'), null);
  assert.equal(tryParseIsoDate('2024-1-1'), null);
});

test('canonicalize', () => {
  assert.equal(canonicalize('SITE:example.com   FileType:pdf  or  x'), 'site:example.com filetype:pdf OR x');
  assert.equal(canonicalize('(intitle:"Index Of" OR intext:x)'), '(intitle:"Index Of" OR intext:x)');
});

test('quoting utilities', () => {
  assert.equal(quote('nested "inner" quotes'), '"nested inner quotes"');
  assert.equal(quote('"already"'), '"already"');
  assert.equal(safeTerm('site:evil.com'), '"site:evil.com"');
  assert.equal(safeTerm('OR'), '"OR"');
  assert.equal(safeTerm('(group'), '"group"');
  assert.equal(safeTerm('AROUND(3)'), '"AROUND(3)"');
  assert.equal(safeTerm('10:30'), '10:30');
  assert.equal(operatorValue('quarterly roadmap'), '"quarterly roadmap"');
  assert.equal(orGroup(['filetype:pdf', 'filetype:docx', 'filetype:pdf']), '(filetype:pdf OR filetype:docx)');
  assert.equal(exclusion('SITE:pinterest.com', isGen), '-site:pinterest.com');
  assert.equal(exclusion('cache:example.com', isGen), '-"cache:example.com"');
  assert.equal(exclusion('privacy policy', isGen), '-"privacy policy"');
});

test('generation is deterministic and safe against operator injection', () => {
  const req = { input: '-secret site:evil.com OR', inputType: 'keyword', intent: 'general-discovery', options: { maxVariants: 12 } };
  const a = generate(req), b = generate(req);
  assert.deepEqual(a, b);
  for (const v of a.variants) {
    const outside = v.query.replace(/"[^"]*"/g, '""');
    assert.ok(!outside.includes('site:evil.com'));
    assert.ok(!outside.includes('-secret'));
  }
});

test('validation errors carry field and 422 flag', () => {
  assert.throws(() => generate({ input: 'not a domain', inputType: 'domain', intent: 'public-documents' }), e => e instanceof InputValidationError && e.field === 'input' && !e.unprocessable);
  assert.throws(() => generate({ input: 'Alice Smith', inputType: 'person', intent: 'person-organization' }), e => e.field === 'options.organization' && e.unprocessable);
  assert.throws(() => generate({ input: 'x', inputType: 'keyword', intent: 'general-discovery', options: { maxVariants: 13 } }), e => e.field === 'options.maxVariants');
});

test('validateQuery reports operators and warnings without rewriting', () => {
  const v = validateQuery('cache:example.com site:example.com or filetype:.pdf bogus:x');
  assert.equal(v.query, 'cache:example.com site:example.com or filetype:.pdf bogus:x');
  assert.deepEqual(v.operators.map(o => o.token), ['cache:', 'site:', 'filetype:']);
  assert.ok(v.warnings.some(w => w.includes('deprecated')));
  assert.ok(v.warnings.some(w => w.includes("'bogus:'")));
  const a = analyzeQuery('(site:example.com "open', google);
  assert.ok(a.hasErrors);
});

test('handle expansion never claims existence', () => {
  const r = expandHandle({ username: '@alice42', categories: ['developer'], maxPlatforms: 3 });
  assert.equal(r.normalizedUsername, 'alice42');
  assert.equal(r.profiles.length, 3);
  assert.ok(r.profiles.every(p => p.status === 'not-checked'));
  assert.equal(r.profiles.find(p => p.platformId === 'github').url, 'https://github.com/alice42');
  const sub = expandHandle({ username: 'alice.b_c', platformIds: ['tumblr', 'github'] });
  assert.equal(sub.profiles.find(p => p.platformId === 'tumblr').url, null);
  assert.equal(expandHandle({ username: 'a&b=c', platformIds: ['github'] }).profiles[0].url, 'https://github.com/a%26b%3Dc');
  assert.equal(expandHandle({ username: 'AliceX', platformIds: ['hackernews'] }).profiles[0].url, 'https://news.ycombinator.com/user?id=AliceX');
  assert.throws(() => expandHandle({ username: 'has space' }), e => e.field === 'username');
});

test('escapeDataString matches RFC 3986 unreserved set', () => {
  assert.equal(escapeDataString("a b!*'()~-_."), "a%20b%21%2A%27%28%29~-_.");
});

test('inferInputType and searchUrl', () => {
  const ext = new Set(bundledCatalogs.fileTypes.extensions.map(e => e.ext));
  assert.equal(inferInputType('example.com'), 'domain');
  assert.equal(inferInputType('@alice'), 'username');
  assert.equal(inferInputType('alice@example.com'), 'email');
  assert.equal(inferInputType('https://example.com/x'), 'url');
  assert.equal(inferInputType('report.pdf', ext), 'filename');
  assert.equal(inferInputType('Alice Smith'), 'person');
  assert.equal(inferInputType('quarterly roadmap'), 'keyword');
  assert.equal(searchUrl('site:example.com "a b"'), 'https://www.google.com/search?q=site%3Aexample.com%20%22a%20b%22');
});
