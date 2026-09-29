// Conformance: the repository's golden fixtures (input + type + intent + options → ordered variants)
// must produce byte-identical queries from this port. Skipped when the fixtures are not present
// (i.e. when the package is tested outside the monorepo).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { generate, bundledCatalogs, validateCatalogs } from '../dist/index.js';

const here = dirname(fileURLToPath(import.meta.url));
const fixtureDir = resolve(here, '..', '..', '..', 'tests', 'golden');
const available = existsSync(fixtureDir);
// DORKSMITH_UPDATE_GOLDEN=1 rewrites each fixture's `expected` from the current engine output (review the diff!).
const update = process.env.DORKSMITH_UPDATE_GOLDEN === '1';

test('embedded catalogs validate', () => {
  assert.deepEqual(validateCatalogs(bundledCatalogs), []);
});

test('golden fixtures directory is present in the monorepo', { skip: !available && 'fixtures not found' }, () => {
  assert.ok(readdirSync(fixtureDir).filter(f => f.endsWith('.json')).length > 100);
});

if (available) {
  const files = readdirSync(fixtureDir).filter(f => f.endsWith('.json')).sort();
  const covered = new Set();
  for (const f of files) {
    const fx = JSON.parse(readFileSync(resolve(fixtureDir, f), 'utf8'));
    test(`golden: ${fx.name}`, () => {
      const r = generate({ input: fx.input, inputType: fx.inputType, intent: fx.intent, engine: 'google', options: fx.options ?? null });
      const actual = r.variants.map(v => ({ id: v.id, query: v.query }));
      if (update) {
        fx.expected = actual;
        writeFileSync(resolve(fixtureDir, f), JSON.stringify(fx, null, 2) + '\n');
      }
      for (const needle of fx.expectedContains ?? []) assert.ok(actual.some(v => v.query.includes(needle)), `missing ${needle}`);
      assert.deepEqual(actual.map(v => v.id), fx.expected.map(e => e.id));
      assert.deepEqual(actual.map(v => v.query), fx.expected.map(e => e.query));
      for (const v of r.variants) covered.add(v.id);
    });
  }
  test('every template is covered by a golden fixture', () => {
    const all = bundledCatalogs.intents.intents.flatMap(i => i.templates.map(t => t.id));
    const missing = all.filter(id => !covered.has(id));
    assert.deepEqual(missing, []);
  });
}
