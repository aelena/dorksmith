"""Deterministic scoring, de-duplication and diversity-aware selection."""
from __future__ import annotations

import math
from dataclasses import dataclass, replace

from .analyzer import QueryAnalysis
from .catalogs import Json

INTENT_MATCH_WEIGHT = 10
INPUT_TYPE_MATCH_WEIGHT = 10
DIVERSITY_BONUS = 15
SIMILARITY_PENALTY = 20
SIMILARITY_THRESHOLD = 0.7
COMPLEXITY_FREE_OPERATORS = 4
COMPLEXITY_PENALTY_PER_OPERATOR = 3
LONG_QUERY_PENALTY = 5
LONG_QUERY_LENGTH = 160


@dataclass(frozen=True)
class Candidate:
    template: Json
    catalog_order: int
    query: str
    canonical: str
    analysis: QueryAnalysis
    base_score: float = 0.0
    base_reason: str = ""


def score(c: Candidate, input_type: str, by_token: dict[str, Json]) -> Candidate:
    weight = int(c.template.get("weight", 50))
    reason = [f"weight {weight}", f"+{INTENT_MATCH_WEIGHT} intent"]
    s: float = weight + INTENT_MATCH_WEIGHT

    if any(w.lower() == input_type.lower() for w in c.template.get("when", ["*"])):
        s += INPUT_TYPE_MATCH_WEIGHT
        reason.append(f"+{INPUT_TYPE_MATCH_WEIGHT} exact input type")

    reliability = 0
    deprecated_penalty = 0
    for token in c.analysis.operators:
        op = by_token.get(token)
        if not op:
            reliability -= 15
            continue
        support = op.get("support")
        if support == "official":
            reliability += 4
        elif support == "working":
            reliability += 2
        elif support == "unreliable":
            reliability -= 10
        elif support == "unknown":
            reliability -= 15
        elif support == "deprecated":
            deprecated_penalty += 30
    s += reliability
    reason.append(f"{'+' if reliability >= 0 else ''}{reliability} operator reliability")
    if deprecated_penalty > 0:
        s -= deprecated_penalty
        reason.append(f"−{deprecated_penalty} deprecated operator")

    extra = len(c.analysis.operators) - COMPLEXITY_FREE_OPERATORS
    if extra > 0:
        p = extra * COMPLEXITY_PENALTY_PER_OPERATOR
        s -= p
        reason.append(f"−{p} complexity")
    if len(c.query) > LONG_QUERY_LENGTH:
        s -= LONG_QUERY_PENALTY
        reason.append(f"−{LONG_QUERY_PENALTY} long query")

    return replace(c, base_score=s, base_reason=" · ".join(reason))


def deduplicate(candidates: list[Candidate]) -> list[Candidate]:
    """Same canonical query from two templates -> keep the higher-scored (then earlier) one."""
    groups: dict[str, list[Candidate]] = {}
    for c in candidates:
        groups.setdefault(c.canonical, []).append(c)
    return [sorted(g, key=lambda x: (-x.base_score, x.catalog_order))[0] for g in groups.values()]


def _tokens(canonical: str) -> set[str]:
    return {t.strip("()") for t in canonical.split(" ") if t}


def _jaccard(a: set[str], b: set[str]) -> float:
    if not a and not b:
        return 1.0
    inter = len(a & b)
    union = len(a) + len(b) - inter
    return 0.0 if union == 0 else inter / union


def _round_half_up(x: float) -> int:
    return int(math.floor(x + 0.5))


def rank(candidates: list[Candidate], input_type: str, by_token: dict[str, Json], take: int) -> list[Candidate]:
    scored = sorted(deduplicate([score(c, input_type, by_token) for c in candidates]), key=lambda c: (-c.base_score, c.catalog_order))
    selected: list[Candidate] = []
    families: set[str] = set()
    token_sets: list[set[str]] = []

    while len(selected) < take and scored:
        best: Candidate | None = None
        best_score = -math.inf
        best_reason = ""
        for c in scored:
            tokens = _tokens(c.canonical)
            family = c.template.get("family", "balanced")
            diversity = 0 if family in families else DIVERSITY_BONUS
            similarity = 0.0 if not token_sets else max(_jaccard(s, tokens) for s in token_sets)
            sim_penalty = SIMILARITY_PENALTY * similarity if similarity > SIMILARITY_THRESHOLD else 0.0
            total = c.base_score + diversity - sim_penalty
            if total > best_score or (total == best_score and best is not None and c.catalog_order < best.catalog_order):
                best, best_score = c, total
                best_reason = c.base_reason
                if diversity > 0:
                    best_reason += f" · +{diversity} new family ({family})"
                if sim_penalty > 0:
                    best_reason += f" · −{_round_half_up(sim_penalty)} similar to a selected variant"
        if best is None:
            break
        selected.append(replace(best, base_score=best_score, base_reason=best_reason))
        families.add(best.template.get("family", "balanced"))
        token_sets.append(_tokens(best.canonical))
        scored.remove(best)
    return selected
