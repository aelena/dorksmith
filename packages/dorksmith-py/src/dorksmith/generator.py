"""raw input -> normalise -> classify/validate -> templates -> placeholders -> canonicalise -> de-duplicate -> rank."""
from __future__ import annotations

import re
from dataclasses import asdict, dataclass, field
from typing import Any

from .analyzer import analyze_query
from .catalogs import Catalogs, Json
from .errors import InputValidationError
from .normalizer import (
    canonicalize, normalize_text, try_normalize_domain, try_normalize_email, try_normalize_url,
    try_normalize_username, try_parse_filename, try_parse_iso_date, words as split_words,
)
from .placeholders import OPTIONAL_BY_DEFAULT
from .ranker import Candidate, rank
from .resolver import GenerationContext, resolve_placeholder

INPUT_TYPES: tuple[str, ...] = ("keyword", "person", "organization", "domain", "url", "username", "email", "filename", "technology")

_MAX_CONTEXT_LENGTH = 200
_MAX_EXCLUDE_TERM_LENGTH = 100
_EMPTY_PARENS = re.compile(r"\(\s*\)")
_WS = re.compile(r"\s+")


@dataclass(frozen=True)
class Limits:
    """Limits the engine enforces; defaults match the HTTP API's appsettings."""

    max_query_length: int = 500
    max_variants: int = 12
    default_variants: int = 6
    max_exclude_terms: int = 20
    max_file_types: int = 10
    engines: tuple[str, ...] = ("google",)
    max_username_length: int = 100
    max_platforms: int = 100
    default_platforms: int = 30


DEFAULT_LIMITS = Limits()


@dataclass(frozen=True)
class Variant:
    id: str
    label: str
    family: str
    query: str
    explanation: str
    operators: list[str]
    warnings: list[str]
    rank_reason: str

    def to_dict(self) -> dict[str, Any]:
        d = asdict(self)
        d["rankReason"] = d.pop("rank_reason")
        return d


@dataclass(frozen=True)
class GenerateResult:
    catalog_version: str
    engine: str
    intent: str
    input_type: str
    normalized_input: str
    variants: list[Variant] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return {
            "catalogVersion": self.catalog_version, "engine": self.engine, "intent": self.intent,
            "inputType": self.input_type, "normalizedInput": self.normalized_input, "variants": [v.to_dict() for v in self.variants],
        }


@dataclass(frozen=True)
class OperatorUse:
    token: str
    name: str
    support: str
    generate: bool


@dataclass(frozen=True)
class ValidateQueryResult:
    query: str
    engine: str
    operators: list[OperatorUse]
    warnings: list[str]
    has_errors: bool

    def to_dict(self) -> dict[str, Any]:
        return {"query": self.query, "engine": self.engine, "operators": [asdict(o) for o in self.operators], "warnings": self.warnings, "hasErrors": self.has_errors}


def _context_value(value: str | None, field_name: str) -> str | None:
    s = normalize_text(value)
    if not s:
        return None
    if len(s) > _MAX_CONTEXT_LENGTH:
        raise InputValidationError(f"{field_name} must be at most {_MAX_CONTEXT_LENGTH} characters.", field_name)
    return s


def build_context(
    input: str, input_type: str, intent: str, *, engine: str | None = "google", options: dict[str, Any] | None = None,
    catalogs: Catalogs | None = None, limits: Limits = DEFAULT_LIMITS,
) -> GenerationContext:
    from .catalogs import bundled_catalogs
    catalogs = catalogs or bundled_catalogs()
    o = options or {}

    engine_id = engine.strip().lower() if engine and engine.strip() else "google"
    engines = [e.lower() for e in limits.engines]
    operators = catalogs.operators.get(engine_id)
    if engine_id not in engines or operators is None:
        raise InputValidationError(f"Engine '{engine_id}' is not supported. Supported: {', '.join(engines)}.", "engine")

    type_id = (input_type or "").lower()
    if type_id not in INPUT_TYPES:
        raise InputValidationError(f"inputType must be one of: {', '.join(INPUT_TYPES)}.", "inputType")

    intent_obj = catalogs.intent((intent or "").strip())
    if intent_obj is None:
        raise InputValidationError("Unknown intent. See the intent catalog.", "intent")
    compatible = intent_obj.get("compatibleInputTypes", [])
    if not any(t.lower() == type_id for t in compatible):
        raise InputValidationError(f"Intent '{intent_obj['id']}' does not support inputType '{type_id}'. Compatible: {', '.join(compatible)}.", "intent", True)

    raw = input or ""
    if len(raw) > limits.max_query_length:
        raise InputValidationError(f"input exceeds {limits.max_query_length} characters.", "input")
    text = normalize_text(raw)
    if not text:
        raise InputValidationError("input is required.", "input")

    ctx = GenerationContext(input_type=type_id, input=text, words=[], intent=intent_obj, engine=engine_id, operators=operators,
                            operator_by_token=catalogs.operator_index(engine_id), max_variants=0)

    if type_id == "domain":
        d = try_normalize_domain(text)
        if d is None:
            raise InputValidationError("A valid domain is required for inputType=domain.", "input")
        ctx.domain, ctx.input, ctx.words = d, d, [d]
    elif type_id == "url":
        u = try_normalize_url(text)
        if u is None:
            raise InputValidationError("A valid http(s) URL is required for inputType=url.", "input")
        ctx.url, ctx.domain, ctx.input, ctx.words = u, u.host, u.href, [u.href]
    elif type_id == "email":
        e = try_normalize_email(text)
        if e is None:
            raise InputValidationError("A valid email address is required for inputType=email.", "input")
        ctx.email, ctx.email_user, ctx.domain = e
        ctx.input, ctx.words = e[0], [e[0]]
    elif type_id == "username":
        un = try_normalize_username(text, limits.max_username_length)
        if un is None:
            raise InputValidationError(f"A username without whitespace (max {limits.max_username_length} chars) is required for inputType=username.", "input")
        ctx.username, ctx.words = un, [un]
    elif type_id == "filename":
        f = try_parse_filename(text)
        if f is None:
            raise InputValidationError("A file name with an extension (report.pdf) is required for inputType=filename.", "input")
        ctx.filename_stem, ctx.filename_ext = f
        ctx.words = [f"{f[0]}.{f[1]}"]
    else:
        w, _ = split_words(text)
        if not w:
            raise InputValidationError("input must contain at least one word.", "input")
        ctx.words = w

    known_ext = {e["ext"] for e in catalogs.file_types.get("extensions", [])}
    for ft in o.get("fileTypes") or []:
        x = (ft or "").strip().lstrip(".").lower()
        if not x:
            continue
        if x not in known_ext:
            raise InputValidationError(f"Unknown file type '{x}'. See the file-type catalog.", "options.fileTypes")
        if x not in ctx.file_types:
            ctx.file_types.append(x)
    if len(ctx.file_types) > limits.max_file_types:
        raise InputValidationError(f"At most {limits.max_file_types} file types are allowed.", "options.fileTypes")

    for term in o.get("excludeTerms") or []:
        x = normalize_text(term).lstrip("-").strip()
        if not x:
            continue
        if len(x) > _MAX_EXCLUDE_TERM_LENGTH:
            raise InputValidationError(f"Excluded terms must be at most {_MAX_EXCLUDE_TERM_LENGTH} characters.", "options.excludeTerms")
        if x.lower() not in (e.lower() for e in ctx.exclude_terms):
            ctx.exclude_terms.append(x)
    if len(ctx.exclude_terms) > limits.max_exclude_terms:
        raise InputValidationError(f"At most {limits.max_exclude_terms} excluded terms are allowed.", "options.excludeTerms")

    if (o.get("after") or "").strip():
        ctx.after = try_parse_iso_date(o["after"])
        if ctx.after is None:
            raise InputValidationError("after must be an ISO date (YYYY-MM-DD).", "options.after")
    if (o.get("before") or "").strip():
        ctx.before = try_parse_iso_date(o["before"])
        if ctx.before is None:
            raise InputValidationError("before must be an ISO date (YYYY-MM-DD).", "options.before")
    if ctx.after and ctx.before and ctx.after > ctx.before:
        raise InputValidationError("after must be on or before the before date.", "options.after")

    if (o.get("site") or "").strip():
        ctx.site = try_normalize_domain(o["site"])
        if ctx.site is None:
            raise InputValidationError("site must be a valid domain.", "options.site")

    max_variants = o.get("maxVariants")
    if max_variants is None:
        max_variants = limits.default_variants
    if not isinstance(max_variants, int) or isinstance(max_variants, bool) or max_variants < 1 or max_variants > limits.max_variants:
        raise InputValidationError(f"maxVariants must be between 1 and {limits.max_variants}.", "options.maxVariants")
    ctx.max_variants = max_variants

    ctx.organization = _context_value(o.get("organization"), "options.organization")
    ctx.location = _context_value(o.get("location"), "options.location")
    ctx.role = _context_value(o.get("role"), "options.role")
    ctx.display_name = _context_value(o.get("displayName"), "options.displayName")

    required = {r.lower() for r in [*(intent_obj.get("requiresOptions") or []), *((intent_obj.get("requiresOptionsForInputTypes") or {}).get(type_id) or [])]}
    checks = {
        "site": ctx.site is not None or ctx.domain is not None,
        "dates": ctx.after is not None or ctx.before is not None,
        "filetypes": bool(ctx.file_types),
        "excludeterms": bool(ctx.exclude_terms),
        "organization": ctx.organization is not None,
        "location": ctx.location is not None,
        "role": ctx.role is not None,
        "displayname": ctx.display_name is not None,
        "context": ctx.has_context,
    }
    for name in sorted(required):
        if not checks.get(name, True):
            raise InputValidationError(f"Intent '{intent_obj['id']}' requires options.{name} for inputType '{type_id}'.", f"options.{name}", True)
    return ctx


def expand_template(template: Json, ctx: GenerationContext, catalogs: Catalogs) -> str | None:
    """Substitute placeholders; None when a required placeholder cannot be resolved."""
    optional = set(OPTIONAL_BY_DEFAULT)
    optional.update(template.get("optional") or [])
    optional.difference_update(template.get("requires") or [])
    for r in template.get("requires") or []:
        if resolve_placeholder(r, ctx, template, catalogs) is None:
            return None

    pattern: str = template["pattern"]
    out: list[str] = []
    i = 0
    while i < len(pattern):
        open_ = pattern.find("{", i)
        if open_ < 0:
            out.append(pattern[i:])
            break
        close = pattern.find("}", open_ + 1)
        if close < 0:
            out.append(pattern[i:])
            break
        out.append(pattern[i:open_])
        name = pattern[open_ + 1:close]
        value = resolve_placeholder(name, ctx, template, catalogs)
        if value is None and name not in optional:
            return None
        out.append(value or "")
        i = close + 1
    query = _WS.sub(" ", _EMPTY_PARENS.sub(" ", "".join(out))).strip()
    return query or None


def generate(
    input: str, input_type: str, intent: str, *, engine: str | None = "google", options: dict[str, Any] | None = None,
    catalogs: Catalogs | None = None, limits: Limits = DEFAULT_LIMITS,
) -> GenerateResult:
    """Generate ranked query variants. Same input + same catalogs = same output."""
    from .catalogs import bundled_catalogs
    catalogs = catalogs or bundled_catalogs()
    ctx = build_context(input, input_type, intent, engine=engine, options=options, catalogs=catalogs, limits=limits)
    by_token = ctx.operator_by_token

    candidates: list[Candidate] = []
    order = 0
    for template in ctx.intent.get("templates", []):
        order += 1
        when = template.get("when", ["*"])
        if not any(w == "*" or w.lower() == ctx.input_type for w in when):
            continue
        query = expand_template(template, ctx, catalogs)
        if query is None:
            continue
        analysis = analyze_query(query, ctx.operators, by_token)
        if any((op := by_token.get(t)) and not op.get("generate") and op.get("support") == "deprecated" for t in analysis.operators):
            continue
        candidates.append(Candidate(template, order, query, canonicalize(query), analysis))

    ranked = rank(candidates, ctx.input_type, by_token, ctx.max_variants)
    if not ranked:
        raise InputValidationError(f"No variants could be generated for intent '{ctx.intent['id']}' with inputType '{ctx.input_type}' and the supplied options.", "intent", True)

    family_label = {f["id"]: f["label"] for f in catalogs.intents.get("families", [])}
    variants = [
        Variant(
            id=c.template["id"], label=family_label.get(c.template.get("family", ""), c.template.get("family", "")), family=c.template.get("family", ""),
            query=c.query, explanation=c.template.get("explanation", ""), operators=list(c.analysis.operators),
            warnings=list(dict.fromkeys([*c.analysis.warnings, *(c.template.get("warnings") or [])])), rank_reason=c.base_reason,
        )
        for c in ranked
    ]
    return GenerateResult(catalogs.catalog_version, ctx.engine, ctx.intent["id"], ctx.input_type, ctx.input, variants)


def validate_query(query: str, engine: str = "google", catalogs: Catalogs | None = None) -> ValidateQueryResult:
    """Expert-mode analysis of a hand-written query. Never rewrites the query."""
    from .catalogs import bundled_catalogs
    catalogs = catalogs or bundled_catalogs()
    engine_id = engine.strip().lower() if engine and engine.strip() else "google"
    ops = catalogs.operators.get(engine_id)
    if ops is None:
        raise InputValidationError(f"Engine '{engine_id}' is not supported.", "engine")
    normalized = normalize_text(query)
    if not normalized:
        raise InputValidationError("query is required.", "query")
    by_token = catalogs.operator_index(engine_id)
    analysis = analyze_query(normalized, ops, by_token)
    uses = [
        OperatorUse(t, op["name"], op["support"], bool(op.get("generate"))) if (op := by_token.get(t)) else OperatorUse(t, "Unknown", "unknown", False)
        for t in analysis.operators
    ]
    return ValidateQueryResult(normalized, engine_id, uses, analysis.warnings, analysis.has_errors)
