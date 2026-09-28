"""Catalog loading. Catalogs are plain dicts mirroring the JSON files in the repository's data/ directory."""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from importlib import resources
from pathlib import Path
from typing import Any

Json = dict[str, Any]


@dataclass(frozen=True)
class Catalogs:
    """The four catalogs the engine consumes. ``operators`` is keyed by engine id."""

    operators: dict[str, Json]
    intents: Json
    platforms: Json
    file_types: Json
    _operator_index: dict[str, dict[str, Json]] = field(default_factory=dict, repr=False, compare=False)

    @property
    def catalog_version(self) -> str:
        return str(self.intents.get("catalogVersion", ""))

    def operator_index(self, engine: str) -> dict[str, Json]:
        """token -> operator definition for an engine (cached)."""
        if engine not in self._operator_index:
            index: dict[str, Json] = {}
            for op in self.operators[engine].get("operators", []):
                index.setdefault(op["token"], op)
            self._operator_index[engine] = index
        return self._operator_index[engine]

    def intent(self, intent_id: str) -> Json | None:
        for i in self.intents.get("intents", []):
            if i.get("id") == intent_id:
                return i
        return None


def _assemble(files: dict[str, Json]) -> Catalogs:
    operators = {v["engine"]: v for k, v in files.items() if k.startswith("operators.")}
    return Catalogs(operators=operators, intents=files["intents.json"], platforms=files["platforms.json"], file_types=files["filetypes.json"])


def load_catalogs(directory: str | Path) -> Catalogs:
    """Load catalogs from a directory containing operators.<engine>.json, intents.json, platforms.json, filetypes.json."""
    d = Path(directory)
    files = {p.name: json.loads(p.read_text(encoding="utf-8")) for p in sorted(d.glob("*.json"))}
    return _assemble(files)


def bundled_catalog_files() -> dict[str, Json]:
    """The raw catalog files embedded in the package, keyed by file name."""
    root = resources.files("dorksmith") / "data"
    out: dict[str, Json] = {}
    for entry in sorted(root.iterdir(), key=lambda e: e.name):
        if entry.name.endswith(".json"):
            out[entry.name] = json.loads(entry.read_text(encoding="utf-8"))
    return out


_bundled: Catalogs | None = None


def bundled_catalogs() -> Catalogs:
    """The catalogs embedded in this package (loaded once)."""
    global _bundled
    if _bundled is None:
        _bundled = _assemble(bundled_catalog_files())
    return _bundled
