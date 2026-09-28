"""Expand a handle into public profile URLs and search queries. No network access, no probing."""
from __future__ import annotations

import re
from dataclasses import asdict, dataclass, field
from typing import Any
from urllib.parse import quote as url_quote

from .catalogs import Catalogs, Json
from .errors import InputValidationError
from .generator import DEFAULT_LIMITS, Limits, generate
from .normalizer import is_letter_or_digit, normalize_text, try_normalize_username
from .quoting import quote

NOT_CHECKED = "not-checked"
HANDLE_NOTICE = ("URLs are constructed from templates and are NOT verified. "
                 "A URL that resolves does not prove the account belongs to the person you are researching.")
_HOST_LABEL = re.compile(r"^[A-Za-z0-9-]+$")


def escape_data_string(value: str) -> str:
    """RFC 3986 percent-encoding of everything except unreserved characters (matches .NET Uri.EscapeDataString)."""
    return url_quote(value, safe="")


@dataclass(frozen=True)
class ProfileCandidate:
    platform_id: str
    platform_name: str
    category: str
    url: str | None
    search_query: str
    status: str
    caveat: str | None

    def to_dict(self) -> dict[str, Any]:
        return {"platformId": self.platform_id, "platformName": self.platform_name, "category": self.category, "url": self.url,
                "searchQuery": self.search_query, "status": self.status, "caveat": self.caveat}


@dataclass(frozen=True)
class HandleQuery:
    id: str
    label: str
    query: str
    explanation: str


@dataclass(frozen=True)
class HandleExpandResult:
    username: str
    normalized_username: str
    notice: str
    profiles: list[ProfileCandidate]
    queries: list[HandleQuery]
    warnings: list[str] = field(default_factory=list)
    catalog_version: str = ""

    def to_dict(self) -> dict[str, Any]:
        return {"username": self.username, "normalizedUsername": self.normalized_username, "notice": self.notice,
                "profiles": [p.to_dict() for p in self.profiles], "queries": [asdict(q) for q in self.queries],
                "warnings": self.warnings, "catalogVersion": self.catalog_version}


def _build(p: Json, username: str) -> ProfileCandidate:
    value = username if p.get("caseSensitive") else username.lower()
    caveats: list[str] = []
    if p.get("caveat"):
        caveats.append(p["caveat"])
    template: str = p["profileUrlTemplate"]
    scheme_end = template.find("://") + 3
    host_end = template.find("/", scheme_end)
    in_host = template.find("{username}") < (len(template) if host_end < 0 else host_end)
    url: str | None
    if in_host and not _HOST_LABEL.match(value):
        url = None
        caveats.append("The handle is used as a sub-domain on this platform and contains characters not valid in a host name.")
    else:
        url = template.replace("{username}", value.lower() if in_host else escape_data_string(value))
    rule = p.get("usernameRule")
    if rule and not re.match(rule["pattern"], username):
        caveats.append(rule.get("note") or "The handle does not match this platform's username rules.")
    return ProfileCandidate(p["id"], p["name"], p["category"], url, f"site:{p['searchDomain']} {quote(username)}", NOT_CHECKED,
                            " ".join(caveats) if caveats else None)


def expand_handle(
    username: str, *, categories: list[str] | None = None, platform_ids: list[str] | None = None, max_platforms: int | None = None,
    catalogs: Catalogs | None = None, limits: Limits = DEFAULT_LIMITS,
) -> HandleExpandResult:
    from .catalogs import bundled_catalogs
    catalogs = catalogs or bundled_catalogs()

    normalized = try_normalize_username(username, limits.max_username_length)
    if normalized is None:
        raise InputValidationError(f"A username without whitespace (max {limits.max_username_length} chars) is required.", "username")

    known_categories = {c["id"].lower() for c in catalogs.platforms.get("categories", [])}
    wanted = [c.strip() for c in (categories or []) if c and c.strip()]
    for c in wanted:
        if c.lower() not in known_categories:
            raise InputValidationError(f"Unknown platform category '{c}'. Known: {', '.join(sorted(known_categories))}.", "categories")

    all_platforms: list[Json] = catalogs.platforms.get("platforms", [])
    by_id = {p["id"].lower(): p for p in all_platforms}
    ids = {p.strip().lower() for p in (platform_ids or []) if p and p.strip()}
    for i in ids:
        if i not in by_id:
            raise InputValidationError(f"Unknown platform '{i}'.", "platformIds")

    max_ = limits.default_platforms if max_platforms is None else max_platforms
    if not isinstance(max_, int) or isinstance(max_, bool) or max_ < 1 or max_ > limits.max_platforms:
        raise InputValidationError(f"maxPlatforms must be between 1 and {limits.max_platforms}.", "maxPlatforms")

    wanted_lower = {w.lower() for w in wanted}
    chosen = [p for p in all_platforms if p.get("enabled", True)
              and (not wanted_lower or p["category"].lower() in wanted_lower)
              and (not ids or p["id"].lower() in ids)]
    chosen.sort(key=lambda p: (-p.get("weight", 0), p["id"]))
    profiles = [_build(p, normalized) for p in chosen[:max_]]

    warnings: list[str] = []
    if not profiles:
        warnings.append("No enabled platforms matched the requested filters.")
    if any(not is_letter_or_digit(ch) and ch not in "_.-" for ch in normalized):
        warnings.append("The handle contains characters many platforms do not allow; expect several URLs to be invalid.")

    queries: list[HandleQuery] = []
    try:
        r = generate(normalized, "username", "username-exact", engine="google", options={"maxVariants": 5}, catalogs=catalogs, limits=limits)
        queries = [HandleQuery(v.id, v.label, v.query, v.explanation) for v in r.variants]
    except InputValidationError:
        pass

    return HandleExpandResult(normalize_text(username), normalized, HANDLE_NOTICE, profiles, queries, warnings, catalogs.catalog_version)
