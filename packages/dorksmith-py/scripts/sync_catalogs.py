#!/usr/bin/env python3
"""Copy the repository catalogs (../../data/*.json) into src/dorksmith/data.

`--check` exits non-zero when the embedded copies differ (used in CI).
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG = HERE.parent
SOURCE = PKG.parent.parent / "data"
TARGET = PKG / "src" / "dorksmith" / "data"


def main() -> int:
    check = "--check" in sys.argv
    if not SOURCE.is_dir():
        print(f"no repository data directory at {SOURCE}; keeping embedded catalogs")
        return 0
    TARGET.mkdir(parents=True, exist_ok=True)
    changed = False
    files = sorted(SOURCE.glob("*.json"))
    for src in files:
        dst = TARGET / src.name
        data = src.read_bytes()
        if not dst.exists() or dst.read_bytes() != data:
            changed = True
            if not check:
                dst.write_bytes(data)
    if check and changed:
        print("embedded catalogs are out of date; run `python scripts/sync_catalogs.py`", file=sys.stderr)
        return 1
    print("embedded catalogs are up to date" if check else f"synced {len(files)} catalog(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
