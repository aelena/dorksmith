"""Structural and cross-reference validation of the catalogs (same rules as the API's readiness check)."""
from __future__ import annotations

import re

from .catalogs import Catalogs
from .generator import INPUT_TYPES
from .placeholders import ALL_PLACEHOLDERS, placeholders_in

_EXTENSION = re.compile(r"^[a-z0-9]{1,12}$")
_ID = re.compile(r"^[a-z0-9][a-z0-9-]*$")
_SUPPORT = {"official", "working", "unreliable", "deprecated", "unknown"}


def validate_catalogs(catalogs: Catalogs) -> list[str]:
    """Return human-readable problems; an empty list means the catalogs are consistent."""
    errors: list[str] = []
    intents, platforms, file_types = catalogs.intents, catalogs.platforms, catalogs.file_types
    operator_catalogs = list(catalogs.operators.values())

    for c in operator_catalogs:
        p = f"operators.{c.get('engine', '')}"
        if c.get("schemaVersion") != 1:
            errors.append(f"{p}: unsupported schemaVersion {c.get('schemaVersion')}")
        if not str(c.get("engine", "")).strip():
            errors.append(f"{p}: engine is required")
        if not str(c.get("catalogVersion", "")).strip():
            errors.append(f"{p}: catalogVersion is required")
        if "{query}" not in str(c.get("searchUrlTemplate", "")):
            errors.append(f"{p}: searchUrlTemplate must contain {{query}}")
        seen: set[str] = set()
        for op in c.get("operators", []):
            token = op.get("token", "")
            q = f"{p}[{token}]"
            if not token:
                errors.append(f"{p}: operator with empty token")
                continue
            if token in seen:
                errors.append(f"{q}: duplicate token")
            seen.add(token)
            if not str(op.get("name", "")).strip():
                errors.append(f"{q}: name is required")
            if not str(op.get("description", "")).strip():
                errors.append(f"{q}: description is required")
            if op.get("support") not in _SUPPORT:
                errors.append(f"{q}: unknown support '{op.get('support')}'")
            if op.get("support") == "deprecated" and op.get("generate"):
                errors.append(f"{q}: deprecated operators must have generate=false")
            if op.get("generate") and op.get("support") not in ("official", "working"):
                errors.append(f"{q}: generate=true requires support official or working")
            for u in op.get("sourceUrls") or []:
                if not re.match(r"^https?://", u):
                    errors.append(f"{q}: invalid sourceUrl '{u}'")
    if not operator_catalogs:
        errors.append("operators: no operator catalog found")

    if file_types.get("schemaVersion") != 1:
        errors.append(f"filetypes: unsupported schemaVersion {file_types.get('schemaVersion')}")
    exts: set[str] = set()
    groups = {g["id"] for g in file_types.get("groups", [])}
    for e in file_types.get("extensions", []):
        if not _EXTENSION.match(e.get("ext", "")):
            errors.append(f"filetypes: invalid extension '{e.get('ext')}' (lowercase alphanumerics only)")
        if e.get("ext") in exts:
            errors.append(f"filetypes: duplicate extension '{e.get('ext')}'")
        exts.add(e.get("ext", ""))
        if e.get("group") not in groups:
            errors.append(f"filetypes[{e.get('ext')}]: unknown group '{e.get('group')}'")
    for g in file_types.get("groups", []):
        for x in g.get("extensions", []):
            if x not in exts:
                errors.append(f"filetypes.groups[{g['id']}]: extension '{x}' not defined")

    if platforms.get("schemaVersion") != 1:
        errors.append(f"platforms: unsupported schemaVersion {platforms.get('schemaVersion')}")
    categories = {c["id"] for c in platforms.get("categories", [])}
    platform_ids: set[str] = set()
    for pl in platforms.get("platforms", []):
        q = f"platforms[{pl.get('id')}]"
        if not _ID.match(pl.get("id", "")):
            errors.append(f"{q}: invalid id")
        if pl.get("id") in platform_ids:
            errors.append(f"{q}: duplicate id")
        platform_ids.add(pl.get("id", ""))
        if not str(pl.get("name", "")).strip():
            errors.append(f"{q}: name is required")
        if pl.get("category") not in categories:
            errors.append(f"{q}: unknown category '{pl.get('category')}'")
        tpl = str(pl.get("profileUrlTemplate", ""))
        if not tpl.startswith("https://") or "{username}" not in tpl:
            errors.append(f"{q}: profileUrlTemplate must be https and contain {{username}}")
        sd = str(pl.get("searchDomain", ""))
        if not sd.strip() or "://" in sd:
            errors.append(f"{q}: searchDomain must be a bare host name")
        rule = pl.get("usernameRule")
        if rule:
            try:
                re.compile(rule.get("pattern", ""))
            except re.error:
                errors.append(f"{q}: usernameRule.pattern is not a valid regex")

    if intents.get("schemaVersion") != 1:
        errors.append(f"intents: unsupported schemaVersion {intents.get('schemaVersion')}")
    if not str(intents.get("catalogVersion", "")).strip():
        errors.append("intents: catalogVersion is required")
    families = {f["id"] for f in intents.get("families", [])}
    intent_groups = {g["id"] for g in intents.get("groups", [])}
    intent_ids: set[str] = set()
    template_ids: set[str] = set()
    blocked = {op["token"].lower() for c in operator_catalogs for op in c.get("operators", [])
               if not op.get("generate") and (op["token"].endswith(":") or op["token"].endswith("("))}

    for intent in intents.get("intents", []):
        q = f"intents[{intent.get('id')}]"
        if not _ID.match(intent.get("id", "")):
            errors.append(f"{q}: invalid id")
        if intent.get("id") in intent_ids:
            errors.append(f"{q}: duplicate id")
        intent_ids.add(intent.get("id", ""))
        if not str(intent.get("label", "")).strip():
            errors.append(f"{q}: label is required")
        if intent.get("group") not in intent_groups:
            errors.append(f"{q}: unknown group '{intent.get('group')}'")
        if intent.get("safety", "standard") not in ("standard", "defensive-exposure"):
            errors.append(f"{q}: safety must be 'standard' or 'defensive-exposure'")
        compatible = intent.get("compatibleInputTypes") or []
        if not compatible:
            errors.append(f"{q}: compatibleInputTypes is required")
        for t in compatible:
            if t not in INPUT_TYPES:
                errors.append(f"{q}: unknown input type '{t}'")
        for x in intent.get("defaultFileTypes") or []:
            if x not in exts:
                errors.append(f"{q}: defaultFileTypes references unknown extension '{x}'")
        for t in intent.get("requiresOptionsForInputTypes") or {}:
            if t not in compatible:
                errors.append(f"{q}: requiresOptionsForInputTypes references incompatible type '{t}'")
        templates = intent.get("templates") or []
        if not templates:
            errors.append(f"{q}: at least one template is required")
        for t in templates:
            r = f"{q}.templates[{t.get('id')}]"
            if not _ID.match(t.get("id", "")):
                errors.append(f"{r}: invalid id")
            if t.get("id") in template_ids:
                errors.append(f"{r}: duplicate template id (ids are global)")
            template_ids.add(t.get("id", ""))
            if t.get("family", "balanced") not in families:
                errors.append(f"{r}: unknown family '{t.get('family')}'")
            pattern = str(t.get("pattern", ""))
            if not pattern.strip():
                errors.append(f"{r}: pattern is required")
            if not str(t.get("explanation", "")).strip():
                errors.append(f"{r}: explanation is required")
            if not 0 <= int(t.get("weight", 50)) <= 1000:
                errors.append(f"{r}: weight must be 0..1000")
            for w in t.get("when", ["*"]):
                if w != "*" and not any(c.lower() == w.lower() for c in compatible):
                    errors.append(f"{r}: 'when' contains '{w}' which is not in compatibleInputTypes")
            names = placeholders_in(pattern)
            if not names:
                errors.append(f"{r}: pattern has no placeholders")
            for n in [*names, *(t.get("requires") or []), *(t.get("optional") or [])]:
                if n not in ALL_PLACEHOLDERS:
                    errors.append(f"{r}: unknown placeholder {{{n}}}")
            if ("keywords" in names or "keywordsAnd" in names) and not t.get("keywords"):
                errors.append(f"{r}: {{keywords}} used without a keywords list")
            if "platformSites" in names and not t.get("platformIds") and not t.get("platformCategories"):
                errors.append(f"{r}: {{platformSites}} used without platformIds or platformCategories")
            for pid in t.get("platformIds") or []:
                if pid not in platform_ids:
                    errors.append(f"{r}: unknown platformId '{pid}'")
            for cat in t.get("platformCategories") or []:
                if cat not in categories:
                    errors.append(f"{r}: unknown platform category '{cat}'")
            for x in t.get("fileTypes") or []:
                if x not in exts:
                    errors.append(f"{r}: fileTypes references unknown extension '{x}'")
            for token in pattern.split(" "):
                bare = token.lstrip("(-").lower()
                colon = bare.find(":")
                if colon > 0 and bare[: colon + 1] in blocked:
                    errors.append(f"{r}: pattern emits non-generatable operator '{bare[: colon + 1]}'")
    return errors
