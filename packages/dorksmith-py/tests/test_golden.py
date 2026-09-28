"""Conformance: the repository's golden fixtures must produce byte-identical queries from this port.

Skipped when the fixtures are not present (package tested outside the monorepo).
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from dorksmith import bundled_catalogs, generate, validate_catalogs

FIXTURES = Path(__file__).resolve().parents[3] / "tests" / "Dorksmith.Api.Tests" / "Golden" / "fixtures"
FILES = sorted(FIXTURES.glob("*.json")) if FIXTURES.is_dir() else []
_covered: set[str] = set()


def test_bundled_catalogs_validate() -> None:
    assert validate_catalogs(bundled_catalogs()) == []


@pytest.mark.skipif(not FILES, reason="golden fixtures not found (outside the monorepo)")
@pytest.mark.parametrize("path", FILES, ids=[p.stem for p in FILES])
def test_golden_fixture(path: Path) -> None:
    fx = json.loads(path.read_text(encoding="utf-8"))
    r = generate(fx["input"], fx["inputType"], fx["intent"], engine="google", options=fx.get("options"))
    actual = [(v.id, v.query) for v in r.variants]
    for needle in fx.get("expectedContains", []):
        assert any(needle in q for _, q in actual), f"missing {needle}"
    assert [i for i, _ in actual] == [e["id"] for e in fx["expected"]]
    assert [q for _, q in actual] == [e["query"] for e in fx["expected"]]
    _covered.update(i for i, _ in actual)


@pytest.mark.skipif(not FILES, reason="golden fixtures not found (outside the monorepo)")
def test_every_template_is_covered() -> None:
    all_ids = [t["id"] for i in bundled_catalogs().intents["intents"] for t in i["templates"]]
    assert sorted(set(all_ids) - _covered) == []
