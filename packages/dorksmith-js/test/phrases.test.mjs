// Quoted phrases and social-style tokens inside free-text input.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { generate, safeTerm, analyzeQuery, bundledCatalogs } from '../dist/index.js';
import { words } from '../dist/normalizer.js';

test('words keeps user-quoted phrases as single terms', () => {
  assert.deepEqual(words('aelena "antonio elena"').words, ['aelena', '"antonio elena"']);
  assert.deepEqual(words('"antonio elena" aelena x').words, ['"antonio elena"', 'aelena', 'x']);
  assert.deepEqual(words('a "" b').words, ['a', 'b']);
  assert.deepEqual(words('a "unterminated phrase').words, ['a', '"unterminated phrase"']);
  assert.deepEqual(words('"whole input quoted"'), { words: ['whole', 'input', 'quoted'], wasQuoted: true });
});

test('safeTerm keeps quoted phrases and neutralises @ and #', () => {
  assert.equal(safeTerm('"antonio elena"'), '"antonio elena"');
  assert.equal(safeTerm('"nested "quotes" inside"'), '"nested quotes inside"');
  assert.equal(safeTerm('""'), '');
  assert.equal(safeTerm('@aelena'), '"@aelena"');
  assert.equal(safeTerm('#osint'), '"#osint"');
  assert.equal(safeTerm('@'), '@');
  assert.equal(safeTerm('a@b'), 'a@b');
});

test('quoted phrase in keyword input survives every variant', () => {
  const r = generate({ input: 'aelena "antonio elena"', inputType: 'keyword', intent: 'general-discovery', options: { maxVariants: 12 } });
  const q = r.variants.map(v => v.query);
  assert.ok(q.includes('aelena "antonio elena"'), 'balanced keeps the phrase');
  assert.ok(q.includes('(aelena OR "antonio elena")'), 'broad treats the phrase as one alternative');
  assert.ok(q.includes('"aelena antonio elena"'), 'precise quotes the whole input');
  for (const v of r.variants) assert.ok(!v.query.includes('""'), v.query);
});

test('a leading @ in keyword input is quoted and raises no operator warning', () => {
  const r = generate({ input: '@aelena antonio elena', inputType: 'keyword', intent: 'general-discovery', options: { maxVariants: 12 } });
  for (const v of r.variants) {
    assert.ok(!v.operators.includes('@'), `${v.id}: ${v.query}`);
    assert.deepEqual(v.warnings, [], `${v.id}: ${v.query}`);
  }
  assert.ok(r.variants.some(v => v.query === '"@aelena" antonio elena'));
  const a = analyzeQuery('"@aelena" antonio elena', bundledCatalogs.operators.google);
  assert.deepEqual(a.operators, ['"']);
});
