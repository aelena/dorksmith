"""Quoted phrases and social-style tokens inside free-text input."""
from __future__ import annotations

from dorksmith import analyze_query, bundled_catalogs, generate, safe_term
from dorksmith.normalizer import words


def test_words_keeps_user_quoted_phrases() -> None:
    assert words('aelena "antonio elena"')[0] == ["aelena", '"antonio elena"']
    assert words('"antonio elena" aelena x')[0] == ['"antonio elena"', "aelena", "x"]
    assert words('a "" b')[0] == ["a", "b"]
    assert words('a "unterminated phrase')[0] == ["a", '"unterminated phrase"']
    assert words('"whole input quoted"') == (["whole", "input", "quoted"], True)


def test_safe_term_keeps_phrases_and_neutralises_social_tokens() -> None:
    assert safe_term('"antonio elena"') == '"antonio elena"'
    assert safe_term('"nested "quotes" inside"') == '"nested quotes inside"'
    assert safe_term('""') == ""
    assert safe_term("@aelena") == '"@aelena"'
    assert safe_term("#osint") == '"#osint"'
    assert safe_term("@") == "@"
    assert safe_term("a@b") == "a@b"


def test_quoted_phrase_survives_variants() -> None:
    r = generate('aelena "antonio elena"', "keyword", "general-discovery", options={"maxVariants": 12})
    queries = [v.query for v in r.variants]
    assert 'aelena "antonio elena"' in queries
    assert '(aelena OR "antonio elena")' in queries
    assert '"aelena antonio elena"' in queries
    assert all('""' not in q for q in queries)


def test_leading_at_is_quoted_without_operator_warning() -> None:
    r = generate("@aelena antonio elena", "keyword", "general-discovery", options={"maxVariants": 12})
    for v in r.variants:
        assert "@" not in v.operators, v.query
        assert v.warnings == [], v.query
    assert any(v.query == '"@aelena" antonio elena' for v in r.variants)
    catalogs = bundled_catalogs()
    assert analyze_query('"@aelena" antonio elena', catalogs.operators["google"], catalogs.operator_index("google")).operators == ['"']
