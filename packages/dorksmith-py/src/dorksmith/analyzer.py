"""Quote-aware operator scanner with plain-language warnings."""
from __future__ import annotations

import re
from dataclasses import dataclass

from .catalogs import Json
from .normalizer import is_letter

GOOGLE_WORD_LIMIT = 32


@dataclass(frozen=True)
class QueryAnalysis:
    operators: list[str]
    warnings: list[str]
    has_errors: bool


def _tokenize(query: str) -> list[tuple[str, bool]]:
    tokens: list[tuple[str, bool]] = []
    i, n = 0, len(query)
    while i < n:
        if query[i].isspace():
            i += 1
            continue
        if query[i] == '"':
            end = query.find('"', i + 1)
            if end < 0:
                end = n - 1
            tokens.append((query[i + 1: max(i + 1, end)], True))
            i = end + 1
            continue
        start = i
        while i < n and not query[i].isspace():
            if query[i] == '"':
                end = query.find('"', i + 1)
                i = n if end < 0 else end + 1
                continue
            i += 1
        tokens.append((query[start:i], False))
    return tokens


def analyze_query(query: str, catalog: Json, by_token: dict[str, Json]) -> QueryAnalysis:
    operators: list[str] = []
    warnings: list[str] = []
    has_errors = False

    def use(token: str) -> None:
        if token not in operators:
            operators.append(token)

    depth = 0
    tokens = _tokenize(query)
    quote_count = query.count('"')
    if quote_count > 0:
        use('"')
    if quote_count % 2 == 1:
        warnings.append("Unbalanced double quotes: the last phrase will not be treated as exact.")
        has_errors = True

    for text, quoted in tokens:
        if quoted:
            if "*" in text:
                use("*")
            continue
        t = text
        if not t:
            continue
        if t == "OR":
            use("OR"); continue
        if t == "AND":
            use("AND"); continue
        if t == "|":
            use("|"); continue
        if t in ("or", "and"):
            warnings.append(f"Lower-case '{t}' is treated as an ordinary word; use upper-case {t.upper()} for a boolean operator.")
            continue

        body = t
        while body and body[0] == "(":
            depth += 1; use("("); body = body[1:]
        closing = 0
        while body and body[-1] == ")":
            closing += 1; body = body[:-1]
        depth -= closing

        if body.startswith("AROUND("):
            use("AROUND("); continue
        if len(body) > 1 and body[0] == "-":
            use("-"); body = body[1:]
        elif len(body) > 1 and body[0] == "+":
            use("+"); body = body[1:]
        elif len(body) > 1 and body[0] == "~":
            use("~"); body = body[1:]
        if len(body) > 1 and body[0] == "#":
            use("#"); continue
        if len(body) > 1 and body[0] == "@":
            use("@"); continue
        if body == "*":
            use("*"); continue
        if ".." in body and any(ch.isdigit() for ch in body):
            use(".."); continue
        if len(body) > 1 and body[0] == "$" and all(ch.isdigit() or ch in ".," for ch in body[1:]):
            use("$"); continue

        colon = body.find(":")
        if colon > 0 and all(is_letter(ch) for ch in body[:colon]):
            prefix = body[: colon + 1].lower()
            value = body[colon + 1:]
            op = by_token.get(prefix)
            if op:
                use(prefix)
                if prefix == "filetype:" and value.startswith("."):
                    warnings.append("filetype: values take no leading dot (filetype:pdf).")
                if prefix == "site:" and "://" in value:
                    warnings.append("site: values take no scheme (site:example.com).")
                if prefix in ("before:", "after:") and len(value) not in (4, 10):
                    warnings.append(f"{prefix} expects YYYY-MM-DD or YYYY.")
                if op.get("takesValue") and not value:
                    warnings.append(f"{prefix} has no value.")
                    has_errors = True
            elif len(body[:colon]) <= 15 and "://" not in body:
                warnings.append(f"'{prefix}' is not a known {catalog.get('engineName', '')} operator and will be searched as plain text.")

    if depth != 0:
        warnings.append("Unbalanced parentheses.")
        has_errors = True

    for token in operators:
        op = by_token.get(token)
        if not op:
            continue
        support = op.get("support")
        caveats = op.get("caveats") or []
        if support == "deprecated":
            warnings.append(f"{op['token']} is deprecated: {caveats[0] if caveats else 'it no longer works.'}")
        elif support == "unreliable":
            warnings.append(f"{op['token']} is unreliable: {caveats[0] if caveats else 'results are inconsistent.'}")
        elif support == "unknown":
            warnings.append(f"{op['token']} has unknown support status.")

    word_count = sum(1 for text, _ in tokens if text)
    if word_count > GOOGLE_WORD_LIMIT:
        warnings.append(f"Query has {word_count} terms; Google ignores everything after the first {GOOGLE_WORD_LIMIT}.")

    return QueryAnalysis(operators, warnings, has_errors)


_ = re  # keep re available for callers that extend the analyzer
