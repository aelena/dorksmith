// Copies the built engine (packages/dorksmith-js/dist) into web/js/engine so the SPA can import it as a
// same-origin ES module. web/js/engine is generated and git-ignored; run `npm run build` at the repo root.
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const dist = resolve(root, 'packages', 'dorksmith-js', 'dist');
const target = resolve(root, 'web', 'js', 'engine');
if (!existsSync(resolve(dist, 'index.js'))) {
  console.error('engine not built: run `npm --prefix packages/dorksmith-js run build` first');
  process.exit(1);
}
rmSync(target, { recursive: true, force: true });
mkdirSync(target, { recursive: true });
let n = 0;
for (const f of readdirSync(dist)) {
  if (!f.endsWith('.js') || f === 'cli.js') continue;
  cpSync(resolve(dist, f), resolve(target, f));
  n++;
}
const pkg = JSON.parse(readFileSync(resolve(root, 'packages', 'dorksmith-js', 'package.json'), 'utf8'));
writeFileSync(resolve(target, 'VERSION'), `${pkg.name} ${pkg.version}\n`);
console.log(`copied ${n} engine module(s) (dorksmith ${pkg.version}) to web/js/engine`);
