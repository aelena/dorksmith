#!/usr/bin/env node
// Minimal CLI:  dorksmith generate <input> [--type domain] [--intent public-documents] [--filetypes pdf,docx]
//               dorksmith handle <username> [--categories developer,general-social] [--max 30]
//               dorksmith validate "<query>"
//               dorksmith intents | operators | platforms
import { generate, expandHandle, validateQuery, inferInputType, bundledCatalogs, catalogVersion, searchUrl, InputValidationError } from './index.js';

const args = process.argv.slice(2);
const cmd = args[0];
const flags: Record<string, string | boolean> = {};
const positional: string[] = [];
for (let i = 1; i < args.length; i++) {
  const a = args[i];
  if (a.startsWith('--')) {
    const [k, v] = a.slice(2).split('=', 2);
    if (v !== undefined) flags[k] = v;
    else if (i + 1 < args.length && !args[i + 1].startsWith('--')) flags[k] = args[++i];
    else flags[k] = true;
  } else positional.push(a);
}
const list = (v: string | boolean | undefined) => typeof v === 'string' ? v.split(',').map(s => s.trim()).filter(Boolean) : [];
const str = (v: string | boolean | undefined) => typeof v === 'string' ? v : undefined;
const json = !!flags.json;

function usage(code = 0): never {
  console.log(`dorksmith ${catalogVersion} — deterministic search-dork generator

  dorksmith generate <input> [--type <inputType>] [--intent <id>] [--filetypes a,b] [--exclude a,b]
                     [--after YYYY-MM-DD] [--before YYYY-MM-DD] [--site host] [--max N]
                     [--organization "..."] [--location "..."] [--role "..."] [--display-name "..."] [--json]
  dorksmith handle <username> [--categories a,b] [--platforms a,b] [--max N] [--json]
  dorksmith validate "<query>" [--json]
  dorksmith intents | operators | platforms | filetypes [--json]`);
  process.exit(code);
}

try {
  switch (cmd) {
    case 'generate': {
      const input = positional.join(' ');
      if (!input) usage(1);
      const type = str(flags.type) ?? inferInputType(input, new Set(bundledCatalogs.fileTypes.extensions.map(e => e.ext))) ?? 'keyword';
      const intent = str(flags.intent) ?? bundledCatalogs.intents.intents.find(i => i.compatibleInputTypes.includes(type))!.id;
      const r = generate({
        input, inputType: type, intent, engine: 'google',
        options: {
          fileTypes: list(flags.filetypes), excludeTerms: list(flags.exclude), after: str(flags.after), before: str(flags.before),
          site: str(flags.site), maxVariants: flags.max ? Number(flags.max) : undefined,
          organization: str(flags.organization), location: str(flags.location), role: str(flags.role), displayName: str(flags['display-name']),
        },
      });
      if (json) { console.log(JSON.stringify(r, null, 2)); break; }
      console.log(`# ${r.inputType} "${r.normalizedInput}" · intent ${r.intent} · catalog ${r.catalogVersion}`);
      for (const v of r.variants) {
        console.log(`\n[${v.label}] ${v.id}\n  ${v.query}\n  ↳ ${v.explanation}`);
        for (const w of v.warnings) console.log(`  ⚠ ${w}`);
        if (flags.urls) console.log(`  ${searchUrl(v.query)}`);
      }
      break;
    }
    case 'handle': {
      const username = positional[0];
      if (!username) usage(1);
      const r = expandHandle({ username, categories: list(flags.categories), platformIds: list(flags.platforms), maxPlatforms: flags.max ? Number(flags.max) : undefined });
      if (json) { console.log(JSON.stringify(r, null, 2)); break; }
      console.log(`# ${r.normalizedUsername} · ${r.profiles.length} platforms · ${r.notice}`);
      for (const p of r.profiles) console.log(`${p.platformName.padEnd(28)} ${p.url ?? '(no reliable URL)'}${p.caveat ? `\n${''.padEnd(29)}⚠ ${p.caveat}` : ''}`);
      console.log('\n# queries');
      for (const q of r.queries) console.log(`  ${q.query}`);
      break;
    }
    case 'validate': {
      const r = validateQuery(positional.join(' '));
      if (json) { console.log(JSON.stringify(r, null, 2)); break; }
      for (const o of r.operators) console.log(`${o.token.padEnd(12)} ${o.support.padEnd(11)} ${o.name}`);
      for (const w of r.warnings) console.log(`⚠ ${w}`);
      if (r.hasErrors) process.exitCode = 2;
      break;
    }
    case 'intents':
      if (json) console.log(JSON.stringify(bundledCatalogs.intents.intents.map(i => ({ id: i.id, label: i.label, group: i.group, compatibleInputTypes: i.compatibleInputTypes, safety: i.safety })), null, 2));
      else for (const i of bundledCatalogs.intents.intents) console.log(`${i.id.padEnd(28)} ${i.compatibleInputTypes.join(',').padEnd(52)} ${i.label}`);
      break;
    case 'operators':
      if (json) console.log(JSON.stringify(bundledCatalogs.operators, null, 2));
      else for (const o of Object.values(bundledCatalogs.operators)[0].operators) console.log(`${o.token.padEnd(14)} ${o.support.padEnd(11)} ${o.generate ? 'generates' : '         '}  ${o.name}`);
      break;
    case 'platforms':
      if (json) console.log(JSON.stringify(bundledCatalogs.platforms, null, 2));
      else for (const p of bundledCatalogs.platforms.platforms) console.log(`${p.id.padEnd(18)} ${p.category.padEnd(20)} ${p.enabled ? '' : '(disabled) '}${p.profileUrlTemplate}`);
      break;
    case 'filetypes':
      if (json) console.log(JSON.stringify(bundledCatalogs.fileTypes, null, 2));
      else for (const e of bundledCatalogs.fileTypes.extensions) console.log(`${e.ext.padEnd(12)} ${e.group.padEnd(11)} ${e.label}`);
      break;
    default:
      usage(cmd === undefined || cmd === '--help' || cmd === '-h' ? 0 : 1);
  }
} catch (e) {
  if (e instanceof InputValidationError) {
    console.error(`error: ${e.message}${e.field ? ` (${e.field})` : ''}`);
    process.exit(e.unprocessable ? 3 : 2);
  }
  throw e;
}
