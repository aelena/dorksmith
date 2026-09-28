"""Deterministic, local input-type suggestion. The caller can always override the result."""
from __future__ import annotations

import re
from collections.abc import Collection

_EMAIL = re.compile(r"^[^\s@\"'<>()\[\],;:\\]+@(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", re.IGNORECASE)
_DOMAIN = re.compile(r"^(?:\*\.)?(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})\.?$", re.IGNORECASE)
_URL = re.compile(r"^(?:https?://)?(?:[a-z0-9-]+\.)+[a-z]{2,63}(?::\d+)?/\S*$", re.IGNORECASE)
_FILENAME = re.compile(r"^[^\s/\\]+\.([a-z0-9]{1,12})$", re.IGNORECASE)


def _capitalised_word(w: str) -> bool:
    return len(w) >= 2 and w[0].isupper() and all(ch.isalpha() or ch in "'’.-" for ch in w[1:])


def infer_input_type(raw: str, known_extensions: Collection[str] | None = None) -> str | None:
    """Suggest an input type for raw text: username, email, url, filename, domain, person or keyword."""
    s = (raw or "").strip()
    if len(s) >= 2 and s[0] == '"' and s[-1] == '"':
        s = s[1:-1]
    if not s:
        return None
    if re.match(r"^@[^\s@]+$", s):
        return "username"
    if _EMAIL.match(s):
        return "email"
    if re.match(r"^https?://", s, re.IGNORECASE) or _URL.match(s):
        return "url"
    if _DOMAIN.match(s):
        m = _FILENAME.match(s)
        if m and known_extensions and m.group(1).lower() in known_extensions and len(s.split(".")) == 2:
            return "filename"
        return "domain"
    fm = _FILENAME.match(s)
    if fm and known_extensions and fm.group(1).lower() in known_extensions:
        return "filename"
    parts = s.split()
    if 2 <= len(parts) <= 3 and all(_capitalised_word(w) for w in parts):
        return "person"
    return "keyword"
