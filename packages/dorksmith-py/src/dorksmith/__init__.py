"""dorksmith — deterministic search-dork generator and OSINT/SOCMINT query engine.

    from dorksmith import generate, expand_handle, validate_query
    result = generate("example.com", "domain", "public-documents", options={"fileTypes": ["pdf"]})
    for v in result.variants:
        print(v.label, v.query)

The engine is pure: same request + same catalogs = same output. Pass ``catalogs=load_catalogs(path)``
to run with edited catalogs; the bundled ones are used otherwise.
"""
from __future__ import annotations

from urllib.parse import quote as _url_quote

from .analyzer import GOOGLE_WORD_LIMIT, QueryAnalysis, analyze_query
from .catalogs import Catalogs, bundled_catalog_files, bundled_catalogs, load_catalogs
from .errors import InputValidationError
from .generator import (
    DEFAULT_LIMITS, INPUT_TYPES, GenerateResult, Limits, OperatorUse, ValidateQueryResult, Variant, build_context,
    expand_template, generate, validate_query,
)
from .handles import HANDLE_NOTICE, NOT_CHECKED, HandleExpandResult, HandleQuery, ProfileCandidate, escape_data_string, expand_handle
from .infer import infer_input_type
from .normalizer import (
    canonicalize, domain_label, normalize_text, try_normalize_domain, try_normalize_email, try_normalize_url,
    try_normalize_username, try_parse_filename, try_parse_iso_date,
)
from .placeholders import ALL_PLACEHOLDERS, OPTIONAL_BY_DEFAULT, placeholders_in
from .quoting import exclusion, looks_like_syntax, operator_value, or_group, quote, safe_term, safe_terms, unquote
from .validator import validate_catalogs

__version__ = "0.2.0"


def catalog_version(catalogs: Catalogs | None = None) -> str:
    """Version string of the catalogs in use (from intents.json)."""
    return (catalogs or bundled_catalogs()).catalog_version


def search_url(query: str, engine: str = "google", catalogs: Catalogs | None = None) -> str:
    """Search-engine URL for a query, built locally with percent-encoding."""
    c = catalogs or bundled_catalogs()
    template = c.operators.get(engine, {}).get("searchUrlTemplate", "https://www.google.com/search?q={query}")
    return template.replace("{query}", _url_quote(query, safe="-_.!~*'()"))


__all__ = [
    "__version__", "ALL_PLACEHOLDERS", "Catalogs", "DEFAULT_LIMITS", "GOOGLE_WORD_LIMIT", "GenerateResult", "HANDLE_NOTICE",
    "HandleExpandResult", "HandleQuery", "INPUT_TYPES", "InputValidationError", "Limits", "NOT_CHECKED", "OPTIONAL_BY_DEFAULT",
    "OperatorUse", "ProfileCandidate", "QueryAnalysis", "ValidateQueryResult", "Variant", "analyze_query", "build_context",
    "bundled_catalog_files", "bundled_catalogs", "canonicalize", "catalog_version", "domain_label", "escape_data_string",
    "exclusion", "expand_handle", "expand_template", "generate", "infer_input_type", "load_catalogs", "looks_like_syntax",
    "normalize_text", "operator_value", "or_group", "placeholders_in", "quote", "safe_term", "safe_terms", "search_url",
    "try_normalize_domain", "try_normalize_email", "try_normalize_url", "try_normalize_username", "try_parse_filename",
    "try_parse_iso_date", "unquote", "validate_catalogs", "validate_query",
]
