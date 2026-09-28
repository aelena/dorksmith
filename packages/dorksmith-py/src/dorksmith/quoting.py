"""Safe search-string quoting: never double-quotes, neutralises user text that looks like syntax."""
from __future__ import annotations

from collections.abc import Callable, Iterable

from .normalizer import is_letter

_RESERVED = {"OR", "AND", "|"}


def _strip_outer(s: str) -> str:
    return s[1:-1] if len(s) >= 2 and s[0] == '"' and s[-1] == '"' else s


def quote(phrase: str | None) -> str:
    """Wrap a phrase in ASCII double quotes; existing surrounding quotes are reused, inner quotes removed."""
    s = _strip_outer((phrase or "").strip()).replace('"', "").strip()
    return f'"{s}"' if s else ""


def unquote(phrase: str | None) -> str:
    return _strip_outer((phrase or "").strip()).replace('"', "").strip()


def looks_like_syntax(s: str) -> bool:
    if s in _RESERVED:
        return True
    if s[0] in "-+~()|":
        return True
    if s[-1] in "()":
        return True
    colon = s.find(":")
    if 0 < colon < len(s) - 1 and all(is_letter(ch) for ch in s[:colon]):
        return True
    return s.startswith("AROUND(")


def safe_term(word: str | None) -> str:
    """A single word emitted unquoted; words that would parse as syntax are quoted so they stay literal."""
    s = (word or "").strip().replace('"', "")
    if not s:
        return ""
    if looks_like_syntax(s):
        inner = s if s.startswith("AROUND(") else s.strip("()")
        return f'"{inner}"'
    return s


def safe_terms(items: Iterable[str]) -> str:
    return " ".join(t for t in (safe_term(w) for w in items) if t)


def operator_value(phrase: str | None) -> str:
    """Value for a prefix operator: bare for a single safe word, quoted otherwise."""
    s = unquote(phrase)
    if not s:
        return ""
    return f'"{s}"' if " " in s or looks_like_syntax(s) else s


def or_group(items: Iterable[str | None]) -> str:
    """(a OR b OR c); a single item is returned bare; none returns empty."""
    seen: list[str] = []
    for i in items:
        if i and i.strip() and i not in seen:
            seen.append(i)
    if not seen:
        return ""
    if len(seen) == 1:
        return seen[0]
    return "(" + " OR ".join(seen) + ")"


def exclusion(term: str | None, is_generatable_operator: Callable[[str], bool]) -> str:
    """-term for exclusions. Known generatable operators (site:x) pass through; phrases get quoted."""
    s = (term or "").strip().lstrip("-").replace('"', "").strip()
    if not s:
        return ""
    colon = s.find(":")
    if 0 < colon < len(s) - 1 and " " not in s and is_generatable_operator(s[: colon + 1].lower()):
        return "-" + s[:colon].lower() + s[colon:]
    return f'-"{s}"' if " " in s or looks_like_syntax(s) else f"-{s}"
