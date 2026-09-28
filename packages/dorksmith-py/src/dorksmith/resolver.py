"""Resolve a template placeholder to operator syntax, or None when the input/options cannot supply it."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from .catalogs import Catalogs, Json
from .normalizer import NormalizedUrl, domain_label, is_letter, is_letter_or_digit
from .quoting import exclusion, operator_value, or_group, quote, safe_term, safe_terms

_WORDY = {"keyword", "person", "organization", "technology"}


@dataclass
class GenerationContext:
    """Fully validated and normalised inputs for one generation run."""

    input_type: str
    input: str
    words: list[str]
    intent: Json
    engine: str
    operators: Json
    operator_by_token: dict[str, Json]
    max_variants: int
    domain: str | None = None
    url: NormalizedUrl | None = None
    username: str | None = None
    email: str | None = None
    email_user: str | None = None
    filename_stem: str | None = None
    filename_ext: str | None = None
    file_types: list[str] = field(default_factory=list)
    exclude_terms: list[str] = field(default_factory=list)
    after: str | None = None
    before: str | None = None
    site: str | None = None
    organization: str | None = None
    location: str | None = None
    role: str | None = None
    display_name: str | None = None

    @property
    def has_context(self) -> bool:
        return any(v is not None for v in (self.organization, self.location, self.role, self.display_name))


def _prefix(op: str, value: str) -> str | None:
    return op + value if value else None


def _file_types(c: GenerationContext, t: Json) -> list[str]:
    if c.file_types:
        return c.file_types
    if t.get("fileTypes") is not None:
        return list(t["fileTypes"])
    return list(c.intent.get("defaultFileTypes") or [])


def platforms_for(t: Json, catalogs: Catalogs) -> list[Json]:
    all_platforms: list[Json] = catalogs.platforms.get("platforms", [])
    ids = t.get("platformIds") or []
    if ids:
        by_id = {p["id"]: p for p in all_platforms}
        return [p for p in (by_id.get(i) for i in ids) if p is not None and p.get("enabled", True)]
    cats = {c.lower() for c in (t.get("platformCategories") or [])}
    chosen = [p for p in all_platforms if p.get("enabled", True) and p["category"].lower() in cats]
    chosen.sort(key=lambda p: (-p.get("weight", 0), p["id"]))
    return chosen[: max(1, int(t.get("platformLimit", 6)))]


def resolve_placeholder(name: str, c: GenerationContext, t: Json, catalogs: Catalogs) -> str | None:
    phrase = " ".join(c.words)
    wordy = c.input_type in _WORDY
    words = c.words

    def is_generatable(token: str) -> bool:
        op = c.operator_by_token.get(token)
        return bool(op and op.get("generate"))

    def kw(k: str) -> str:
        return quote(k) if " " in k else safe_term(k)

    v: str | None
    if name == "terms":
        if c.input_type == "domain":
            v = c.domain
        elif c.input_type == "username":
            v = c.username
        elif c.input_type == "email":
            v = quote(c.email)
        elif c.input_type == "url":
            v = quote(c.url.bare) if c.url else None
        else:
            v = safe_terms(words)
    elif name == "termsAny":
        v = or_group(safe_term(w) for w in words) if wordy and len(words) >= 2 else None
    elif name == "quoted":
        v = quote(c.url.href) if c.input_type == "url" and c.url else quote(phrase)
    elif name == "quotedReversed":
        v = quote(words[-1] + " " + " ".join(words[:-1])) if wordy and len(words) >= 2 else None
    elif name == "quotedInitialLast":
        v = quote(words[0][0].upper() + ". " + words[-1]) if wordy and len(words) >= 2 and is_letter(words[0][0]) else None
    elif name == "phraseWildcard":
        v = quote(words[0] + " * " + words[-1]) if wordy and len(words) >= 2 else None
    elif name == "intitle":
        v = _prefix("intitle:", operator_value(phrase))
    elif name == "intitleAny":
        v = or_group(_prefix("intitle:", operator_value(w)) for w in words) if len(words) >= 2 else None
    elif name == "inurl":
        v = resolve_placeholder("inurlPath", c, t, catalogs) if c.input_type == "url" else _prefix("inurl:", operator_value(phrase))
    elif name == "inurlAny":
        v = or_group(_prefix("inurl:", operator_value(w)) for w in words) if len(words) >= 2 else None
    elif name == "intext":
        if c.input_type == "url" and c.url:
            v = _prefix("intext:", quote(c.url.href))
        elif c.input_type == "email":
            v = _prefix("intext:", quote(c.email))
        else:
            v = _prefix("intext:", operator_value(phrase))
    elif name == "intextAny":
        v = or_group(_prefix("intext:", operator_value(w)) for w in words) if len(words) >= 2 else None

    elif name == "domain":
        v = c.domain
    elif name == "domainQuoted":
        v = quote(c.domain)
    elif name == "domainLabel":
        v = None if c.domain is None else quote(domain_label(c.domain))
    elif name == "domainLabelBare":
        v = None if c.domain is None else domain_label(c.domain)
    elif name == "site":
        v = "site:" + c.domain if c.domain is not None else ("site:" + c.site if c.site is not None else None)
    elif name == "siteOption":
        v = "site:" + c.site if c.site is not None else None
    elif name == "siteWildcard":
        v = None if c.domain is None else "site:*." + c.domain
    elif name == "excludeSite":
        v = None if c.domain is None else "-site:" + c.domain
    elif name == "excludeWww":
        v = None if c.domain is None else "-site:www." + c.domain
    elif name == "atDomainQuoted":
        v = None if c.domain is None else quote("@" + c.domain)

    elif name == "username":
        v = c.username
    elif name == "quotedUsername":
        v = quote(c.username)
    elif name == "atUsername":
        v = None if c.username is None else quote("@" + c.username)
    elif name == "inurlUsername":
        v = _prefix("inurl:", operator_value(c.username))
    elif name == "intitleUsername":
        v = _prefix("intitle:", operator_value(c.username))
    elif name == "hashtagUsername":
        v = "#" + c.username if c.username is not None and all(is_letter_or_digit(ch) or ch == "_" for ch in c.username) else None

    elif name == "emailQuoted":
        v = quote(c.email)
    elif name == "emailUserQuoted":
        v = quote(c.email_user)

    elif name == "url":
        v = c.url.href if c.url else None
    elif name == "urlQuoted":
        v = quote(c.url.href) if c.url else None
    elif name == "urlBareQuoted":
        v = quote(c.url.bare) if c.url else None
    elif name == "inurlPath":
        v = "inurl:" + quote(c.url.path.strip("/")) if c.url is not None and len(c.url.path) > 1 else None

    elif name == "filename":
        v = None if c.filename_stem is None else quote(f"{c.filename_stem}.{c.filename_ext}")
    elif name == "filenameStem":
        v = quote(c.filename_stem)
    elif name == "filetypeFromFilename":
        v = None if c.filename_ext is None else "filetype:" + c.filename_ext

    elif name == "filetypeGroup":
        v = or_group("filetype:" + x for x in _file_types(c, t))
    elif name == "filetypeFirst":
        fts = _file_types(c, t)
        v = "filetype:" + fts[0] if fts else None
    elif name == "exclusions":
        seen: set[str] = set()
        parts: list[str] = []
        for x in [*c.exclude_terms, *(t.get("exclude") or [])]:
            e = exclusion(x, is_generatable)
            if not e or e.lower() in seen:
                continue
            seen.add(e.lower())
            parts.append(e)
        v = " ".join(parts)
    elif name == "dates":
        v = " ".join(x for x in (resolve_placeholder("after", c, t, catalogs), resolve_placeholder("before", c, t, catalogs)) if x is not None)
    elif name == "after":
        v = "after:" + c.after if c.after is not None else None
    elif name == "before":
        v = "before:" + c.before if c.before is not None else None
    elif name == "keywords":
        v = or_group(kw(k) for k in (t.get("keywords") or []))
    elif name == "keywordsAnd":
        v = " ".join(kw(k) for k in (t.get("keywords") or []))
    elif name == "platformSites":
        v = or_group("site:" + p["searchDomain"] for p in platforms_for(t, catalogs))
    elif name == "organization":
        v = quote(c.organization)
    elif name == "location":
        v = quote(c.location)
    elif name == "role":
        v = quote(c.role)
    elif name == "displayName":
        v = quote(c.display_name)
    elif name == "context":
        v = " ".join(quote(x) for x in (c.organization, c.location, c.role, c.display_name) if x is not None)
    else:
        raise ValueError(f"Unknown placeholder {{{name}}}")

    return v if v else None


__all__: list[Any] = ["GenerationContext", "resolve_placeholder", "platforms_for"]
