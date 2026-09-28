"""Deterministic, non-destructive normalisation of user input. Never lower-cases search terms."""
from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass
from datetime import date
from urllib.parse import urlsplit

_SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*://")
_HOSTNAME = re.compile(r"^(?:\*\.)?(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})$")
_IPV4 = re.compile(r"^(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}$")
_EMAIL = re.compile(r"^[^\s@\"'<>()\[\],;:\\]+@((?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63})$", re.IGNORECASE)
_FILENAME = re.compile(r"^(?P<stem>.+)\.(?P<ext>[A-Za-z0-9]{1,12})$")
_WS = re.compile(r"\s+")

_QUOTE_MAP = {
    "“": '"', "”": '"', "„": '"', "‟": '"', "«": '"', "»": '"', "″": '"', "〃": '"',
    "‘": "'", "’": "'", "‚": "'", "‛": "'", "′": "'",
    " ": " ", " ": " ", " ": " ", "　": " ",
}


def is_control(ch: str) -> bool:
    return unicodedata.category(ch) == "Cc"


def is_letter(ch: str) -> bool:
    return unicodedata.category(ch).startswith("L")


def is_letter_or_digit(ch: str) -> bool:
    cat = unicodedata.category(ch)
    return cat.startswith("L") or cat == "Nd"


def normalize_text(value: str | None) -> str:
    """Trim, collapse whitespace, map Unicode quotes to ASCII and drop control characters."""
    if not value:
        return ""
    out: list[str] = []
    for ch in value:
        mapped = _QUOTE_MAP.get(ch)
        if mapped is not None:
            out.append(mapped)
            continue
        if is_control(ch) and ch not in "\t\n\r":
            continue
        out.append(ch)
    return _WS.sub(" ", "".join(out)).strip()


def words(normalized: str) -> tuple[list[str], bool]:
    """Split normalised text into words, unwrapping a fully quoted phrase ("a b" -> a b)."""
    s = normalized
    quoted = len(s) >= 2 and s[0] == '"' and s[-1] == '"' and s.find('"', 1) == len(s) - 1
    if quoted:
        s = s[1:-1].strip()
    return [w for w in (p.strip() for p in s.split(" ")) if w], quoted


def try_normalize_domain(value: str | None) -> str | None:
    """Accept host names with optional scheme/path/port; return the bare lower-case host without a leading www."""
    s = normalize_text(value)
    if not s or " " in s:
        return None
    s = _SCHEME.sub("", s, count=1)
    cuts = [i for i in (s.find(c) for c in "/?#") if i >= 0]
    if cuts:
        s = s[: min(cuts)]
    at = s.rfind("@")
    if at >= 0:
        s = s[at + 1:]
    colon = s.find(":")
    if colon >= 0:
        s = s[:colon]
    s = s.rstrip(".").lower()
    if s.startswith("*."):
        s = s[2:]
    if s.startswith("www.") and s.count(".") >= 2:
        s = s[4:]
    if not s or len(s) > 253:
        return None
    if not _HOSTNAME.match(s) and not _IPV4.match(s):
        return None
    return s


def domain_label(domain: str) -> str:
    """The organisation-ish label of a domain: example for www.example.co.uk."""
    labels = domain.split(".")
    if len(labels) < 2 or _IPV4.match(domain):
        return domain
    last, second_last = labels[-1], labels[-2]
    if len(labels) >= 3 and len(last) <= 3 and len(second_last) <= 3:
        return labels[-3]
    return second_last


@dataclass(frozen=True)
class NormalizedUrl:
    href: str
    host: str
    path: str
    bare: str


_DEFAULT_PORTS = {"http": 80, "https": 443}


def try_normalize_url(value: str | None) -> NormalizedUrl | None:
    """http/https only. A missing scheme is assumed to be https. Never fetches."""
    s = normalize_text(value)
    if not s or " " in s:
        return None
    if not _SCHEME.match(s):
        s = "https://" + s
    try:
        parts = urlsplit(s)
        host = (parts.hostname or "").lower()
        port = parts.port
    except ValueError:
        return None
    scheme = parts.scheme.lower()
    if scheme not in _DEFAULT_PORTS or not host:
        return None
    if not _HOSTNAME.match(host) and not _IPV4.match(host):
        return None
    path = parts.path or "/"
    if len(path) > 1:
        path = path.rstrip("/") or "/"
    host_port = host if port is None or port == _DEFAULT_PORTS[scheme] else f"{host}:{port}"
    query = f"?{parts.query}" if parts.query else ""
    href = f"{scheme}://{host_port}{path}{query}"
    bare = (host_port + path + query).rstrip("/")
    return NormalizedUrl(href=href, host=host, path=path, bare=bare)


def try_normalize_username(value: str | None, max_length: int) -> str | None:
    """Strip leading @ characters; reject whitespace, control characters and quotes."""
    s = normalize_text(value).lstrip("@").strip()
    if not s or len(s) > max_length:
        return None
    for ch in s:
        if ch.isspace() or is_control(ch) or ch == '"':
            return None
    return s


def try_normalize_email(value: str | None) -> tuple[str, str, str] | None:
    """Returns (email, local_part, domain) with a lower-cased domain, or None."""
    s = normalize_text(value).strip("<>\"'")
    if not _EMAIL.match(s) or len(s) > 254:
        return None
    at = s.rfind("@")
    local = s[:at]
    domain = s[at + 1:].lower()
    return f"{local}@{domain}", local, domain


def try_parse_filename(value: str | None) -> tuple[str, str] | None:
    s = normalize_text(value).strip('"')
    m = _FILENAME.match(s)
    if not m:
        return None
    stem = m.group("stem").strip()
    if not stem or '"' in stem:
        return None
    return stem, m.group("ext").lower()


def try_parse_iso_date(value: str | None) -> str | None:
    """Strict YYYY-MM-DD; returns the same string when valid."""
    s = normalize_text(value)
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", s):
        return None
    try:
        y, m, d = (int(p) for p in s.split("-"))
        date(y, m, d)
    except ValueError:
        return None
    return s


def canonicalize(query: str) -> str:
    """Canonical form for duplicate detection: collapsed whitespace, lower-cased operator prefixes, upper-cased OR/AND."""
    s = _WS.sub(" ", query).strip()
    out: list[str] = []
    in_quote = False
    start = 0
    for i in range(len(s) + 1):
        end = i == len(s)
        c = " " if end else s[i]
        if not end and c == '"':
            in_quote = not in_quote
        if (c == " " and not in_quote) or end:
            out.append(_canonical_token(s[start:i]))
            start = i + 1
    return " ".join(out).strip()


def _canonical_token(token: str) -> str:
    low = token.lower()
    if low == "or":
        return "OR"
    if low == "and":
        return "AND"
    body = token.lstrip("(-")
    colon = body.find(":")
    if colon > 0 and all(is_letter(ch) for ch in body[:colon]):
        prefix_len = len(token) - len(body)
        return token[:prefix_len] + body[:colon].lower() + body[colon:]
    return token
