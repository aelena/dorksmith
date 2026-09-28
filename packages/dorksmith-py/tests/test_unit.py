"""Unit tests mirroring the C# suites for pieces the golden fixtures do not exercise directly."""
from __future__ import annotations

import pytest

from dorksmith import (
    InputValidationError, analyze_query, bundled_catalogs, canonicalize, domain_label, escape_data_string, exclusion,
    expand_handle, generate, infer_input_type, normalize_text, operator_value, or_group, quote, safe_term, search_url,
    try_normalize_domain, try_normalize_email, try_normalize_url, try_normalize_username, try_parse_filename,
    try_parse_iso_date, validate_query,
)
from dorksmith.cli import main

CATALOGS = bundled_catalogs()
GOOGLE = CATALOGS.operators["google"]
BY_TOKEN = CATALOGS.operator_index("google")


def _is_gen(token: str) -> bool:
    op = BY_TOKEN.get(token)
    return bool(op and op.get("generate"))


def test_normalize_text() -> None:
    assert normalize_text("  hello   world  ") == "hello world"
    assert normalize_text("“quarterly roadmap”") == '"quarterly roadmap"'
    assert normalize_text("it’s") == "it's"
    assert normalize_text("ctrl\u0001char") == "ctrlchar"
    assert normalize_text("Alice Smith") == "Alice Smith"


@pytest.mark.parametrize("value,expected", [
    ("example.com", "example.com"), ("https://www.example.com/path?q=1", "example.com"), ("EXAMPLE.COM.", "example.com"),
    ("sub.example.co.uk:8443", "sub.example.co.uk"), ("*.example.com", "example.com"), ("www.example.com", "example.com"),
    ("192.168.1.10", "192.168.1.10"), ("", None), ("not a domain", None), ("localhost", None), ("-bad.example.com", None), ("example.c", None),
])
def test_domain(value: str, expected: str | None) -> None:
    assert try_normalize_domain(value) == expected


def test_domain_label() -> None:
    assert domain_label("www.example.co.uk") == "example"
    assert domain_label("docs.example.org") == "example"


def test_url() -> None:
    assert try_normalize_url("https://Example.com/Path/").href == "https://example.com/Path"
    assert try_normalize_url("example.com/docs/api").href == "https://example.com/docs/api"
    assert try_normalize_url("http://example.com:80/x#frag").href == "http://example.com/x"
    assert try_normalize_url("https://example.com/a/b/").bare == "example.com/a/b"
    for bad in ("ftp://example.com/file", "javascript:alert(1)", "not a url"):
        assert try_normalize_url(bad) is None


def test_username_email_filename_date() -> None:
    assert try_normalize_username("@@alice", 100) == "alice"
    assert try_normalize_username("has space", 100) is None
    assert try_normalize_username("a" * 101, 100) is None
    assert try_normalize_email("Alice.Smith@Example.COM") == ("Alice.Smith@example.com", "Alice.Smith", "example.com")
    assert try_normalize_email("alice@localhost") is None
    assert try_parse_filename("Annual Report 2024.PDF") == ("Annual Report 2024", "pdf")
    assert try_parse_iso_date("2024-01-31") == "2024-01-31"
    assert try_parse_iso_date("2024-13-01") is None
    assert try_parse_iso_date("2024-1-1") is None


def test_canonicalize() -> None:
    assert canonicalize("SITE:example.com   FileType:pdf  or  x") == "site:example.com filetype:pdf OR x"
    assert canonicalize('(intitle:"Index Of" OR intext:x)') == '(intitle:"Index Of" OR intext:x)'


def test_quoting() -> None:
    assert quote('nested "inner" quotes') == '"nested inner quotes"'
    assert quote('"already"') == '"already"'
    assert safe_term("site:evil.com") == '"site:evil.com"'
    assert safe_term("OR") == '"OR"'
    assert safe_term("(group") == '"group"'
    assert safe_term("AROUND(3)") == '"AROUND(3)"'
    assert safe_term("10:30") == "10:30"
    assert operator_value("quarterly roadmap") == '"quarterly roadmap"'
    assert or_group(["filetype:pdf", "filetype:docx", "filetype:pdf"]) == "(filetype:pdf OR filetype:docx)"
    assert exclusion("SITE:pinterest.com", _is_gen) == "-site:pinterest.com"
    assert exclusion("cache:example.com", _is_gen) == '-"cache:example.com"'
    assert exclusion("privacy policy", _is_gen) == '-"privacy policy"'


def test_generation_deterministic_and_injection_safe() -> None:
    a = generate("-secret site:evil.com OR", "keyword", "general-discovery", options={"maxVariants": 12})
    b = generate("-secret site:evil.com OR", "keyword", "general-discovery", options={"maxVariants": 12})
    assert a == b
    import re
    for v in a.variants:
        outside = re.sub(r'"[^"]*"', '""', v.query)
        assert "site:evil.com" not in outside
        assert "-secret" not in outside


def test_validation_errors() -> None:
    with pytest.raises(InputValidationError) as e:
        generate("not a domain", "domain", "public-documents")
    assert e.value.field == "input" and not e.value.unprocessable
    with pytest.raises(InputValidationError) as e:
        generate("Alice Smith", "person", "person-organization")
    assert e.value.field == "options.organization" and e.value.unprocessable
    with pytest.raises(InputValidationError) as e:
        generate("x", "keyword", "general-discovery", options={"maxVariants": 13})
    assert e.value.field == "options.maxVariants"


def test_validate_query() -> None:
    v = validate_query("cache:example.com site:example.com or filetype:.pdf bogus:x")
    assert v.query == "cache:example.com site:example.com or filetype:.pdf bogus:x"
    assert [o.token for o in v.operators] == ["cache:", "site:", "filetype:"]
    assert any("deprecated" in w for w in v.warnings)
    assert any("'bogus:'" in w for w in v.warnings)
    assert analyze_query('(site:example.com "open', GOOGLE, BY_TOKEN).has_errors


def test_handles() -> None:
    r = expand_handle("@alice42", categories=["developer"], max_platforms=3)
    assert r.normalized_username == "alice42"
    assert len(r.profiles) == 3 and all(p.status == "not-checked" for p in r.profiles)
    assert next(p for p in r.profiles if p.platform_id == "github").url == "https://github.com/alice42"
    sub = expand_handle("alice.b_c", platform_ids=["tumblr", "github"])
    assert next(p for p in sub.profiles if p.platform_id == "tumblr").url is None
    assert expand_handle("a&b=c", platform_ids=["github"]).profiles[0].url == "https://github.com/a%26b%3Dc"
    assert expand_handle("AliceX", platform_ids=["hackernews"]).profiles[0].url == "https://news.ycombinator.com/user?id=AliceX"
    with pytest.raises(InputValidationError) as e:
        expand_handle("has space")
    assert e.value.field == "username"
    assert escape_data_string("a b!*'()~-_.") == "a%20b%21%2A%27%28%29~-_."


def test_infer_and_search_url() -> None:
    known = {e["ext"] for e in CATALOGS.file_types["extensions"]}
    assert infer_input_type("example.com") == "domain"
    assert infer_input_type("@alice") == "username"
    assert infer_input_type("alice@example.com") == "email"
    assert infer_input_type("https://example.com/x") == "url"
    assert infer_input_type("report.pdf", known) == "filename"
    assert infer_input_type("Alice Smith") == "person"
    assert infer_input_type("quarterly roadmap") == "keyword"
    assert search_url('site:example.com "a b"') == "https://www.google.com/search?q=site%3Aexample.com%20%22a%20b%22"


def test_cli(capsys: pytest.CaptureFixture[str]) -> None:
    assert main(["generate", "example.com", "--intent", "public-documents", "--max", "2"]) == 0
    out = capsys.readouterr().out
    assert "site:example.com" in out
    assert main(["handle", "alice42", "--categories", "developer", "--max", "2", "--json"]) == 0
    assert '"platformId": "github"' in capsys.readouterr().out
    assert main(["validate", "cache:example.com"]) == 0
    assert main(["generate", "not a domain", "--type", "domain", "--intent", "public-documents"]) == 2
