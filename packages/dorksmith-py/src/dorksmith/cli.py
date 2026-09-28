"""Command line: dorksmith generate|handle|validate|intents|operators|platforms|filetypes."""
from __future__ import annotations

import argparse
import json
import sys

from . import __version__, bundled_catalogs, catalog_version, expand_handle, generate, infer_input_type, search_url, validate_query
from .errors import InputValidationError


def _csv(value: str | None) -> list[str]:
    return [v.strip() for v in value.split(",") if v.strip()] if value else []


def _parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="dorksmith", description=f"Deterministic search-dork generator (catalog {catalog_version()}, package {__version__}).")
    sub = p.add_subparsers(dest="command", required=True)

    g = sub.add_parser("generate", help="generate ranked query variants")
    g.add_argument("input", nargs="+", help="target text (domain, name, handle, email, phrase, ...)")
    g.add_argument("--type", dest="input_type", help="input type; inferred when omitted")
    g.add_argument("--intent", help="intent id; first compatible intent when omitted")
    g.add_argument("--filetypes", help="comma-separated extensions")
    g.add_argument("--exclude", help="comma-separated excluded terms")
    g.add_argument("--after"); g.add_argument("--before"); g.add_argument("--site")
    g.add_argument("--max", type=int, dest="max_variants")
    g.add_argument("--organization"); g.add_argument("--location"); g.add_argument("--role"); g.add_argument("--display-name", dest="display_name")
    g.add_argument("--urls", action="store_true", help="print a search URL under each variant")
    g.add_argument("--json", action="store_true")

    h = sub.add_parser("handle", help="expand a handle into profile URLs and queries")
    h.add_argument("username")
    h.add_argument("--categories"); h.add_argument("--platforms"); h.add_argument("--max", type=int, dest="max_platforms")
    h.add_argument("--json", action="store_true")

    v = sub.add_parser("validate", help="analyse a hand-written query")
    v.add_argument("query", nargs="+")
    v.add_argument("--json", action="store_true")

    for name in ("intents", "operators", "platforms", "filetypes"):
        sub.add_parser(name, help=f"list the {name} catalog").add_argument("--json", action="store_true")
    return p


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    catalogs = bundled_catalogs()
    try:
        if args.command == "generate":
            text = " ".join(args.input)
            known = {e["ext"] for e in catalogs.file_types["extensions"]}
            input_type = args.input_type or infer_input_type(text, known) or "keyword"
            intent = args.intent or next(i["id"] for i in catalogs.intents["intents"] if input_type in i["compatibleInputTypes"])
            r = generate(text, input_type, intent, options={
                "fileTypes": _csv(args.filetypes), "excludeTerms": _csv(args.exclude), "after": args.after, "before": args.before, "site": args.site,
                "maxVariants": args.max_variants, "organization": args.organization, "location": args.location, "role": args.role, "displayName": args.display_name,
            })
            if args.json:
                print(json.dumps(r.to_dict(), indent=2, ensure_ascii=False))
                return 0
            print(f"# {r.input_type} \"{r.normalized_input}\" · intent {r.intent} · catalog {r.catalog_version}")
            for var in r.variants:
                print(f"\n[{var.label}] {var.id}\n  {var.query}\n  ↳ {var.explanation}")
                for w in var.warnings:
                    print(f"  ⚠ {w}")
                if args.urls:
                    print(f"  {search_url(var.query)}")
        elif args.command == "handle":
            r = expand_handle(args.username, categories=_csv(args.categories), platform_ids=_csv(args.platforms), max_platforms=args.max_platforms)
            if args.json:
                print(json.dumps(r.to_dict(), indent=2, ensure_ascii=False))
                return 0
            print(f"# {r.normalized_username} · {len(r.profiles)} platforms · {r.notice}")
            for p in r.profiles:
                print(f"{p.platform_name:<28} {p.url or '(no reliable URL)'}")
                if p.caveat:
                    print(f"{'':<29}⚠ {p.caveat}")
            print("\n# queries")
            for q in r.queries:
                print(f"  {q.query}")
        elif args.command == "validate":
            r = validate_query(" ".join(args.query))
            if args.json:
                print(json.dumps(r.to_dict(), indent=2, ensure_ascii=False))
                return 0
            for o in r.operators:
                print(f"{o.token:<12} {o.support:<11} {o.name}")
            for w in r.warnings:
                print(f"⚠ {w}")
            return 2 if r.has_errors else 0
        elif args.command == "intents":
            rows = catalogs.intents["intents"]
            if args.json:
                print(json.dumps([{k: i[k] for k in ("id", "label", "group", "compatibleInputTypes", "safety")} for i in rows], indent=2))
            else:
                for i in rows:
                    print(f"{i['id']:<28} {','.join(i['compatibleInputTypes']):<52} {i['label']}")
        elif args.command == "operators":
            if args.json:
                print(json.dumps(catalogs.operators, indent=2))
            else:
                for o in next(iter(catalogs.operators.values()))["operators"]:
                    print(f"{o['token']:<14} {o['support']:<11} {'generates' if o.get('generate') else '         '}  {o['name']}")
        elif args.command == "platforms":
            if args.json:
                print(json.dumps(catalogs.platforms, indent=2))
            else:
                for p in catalogs.platforms["platforms"]:
                    print(f"{p['id']:<18} {p['category']:<20} {'' if p.get('enabled', True) else '(disabled) '}{p['profileUrlTemplate']}")
        elif args.command == "filetypes":
            if args.json:
                print(json.dumps(catalogs.file_types, indent=2))
            else:
                for e in catalogs.file_types["extensions"]:
                    print(f"{e['ext']:<12} {e['group']:<11} {e['label']}")
        return 0
    except InputValidationError as e:
        print(f"error: {e.message}" + (f" ({e.field})" if e.field else ""), file=sys.stderr)
        return 3 if e.unprocessable else 2


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
