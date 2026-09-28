// Deterministic scoring, de-duplication and diversity-aware selection. Port of Dorksmith.Api.Generation.CandidateRanker.
import type { OperatorDefinition, QueryTemplate } from './types.js';
import type { QueryAnalysis } from './analyzer.js';

export interface Candidate {
  template: QueryTemplate;
  catalogOrder: number;
  query: string;
  canonical: string;
  analysis: QueryAnalysis;
  baseScore: number;
  baseReason: string;
}

export const INTENT_MATCH_WEIGHT = 10;
export const INPUT_TYPE_MATCH_WEIGHT = 10;
export const DIVERSITY_BONUS = 15;
export const SIMILARITY_PENALTY = 20;
export const SIMILARITY_THRESHOLD = 0.7;
export const COMPLEXITY_FREE_OPERATORS = 4;
export const COMPLEXITY_PENALTY_PER_OPERATOR = 3;
export const LONG_QUERY_PENALTY = 5;
export const LONG_QUERY_LENGTH = 160;

export function score(c: Candidate, inputTypeId: string, byToken: Map<string, OperatorDefinition>): Candidate {
  const reason: string[] = [`weight ${c.template.weight}`, `+${INTENT_MATCH_WEIGHT} intent`];
  let s = c.template.weight + INTENT_MATCH_WEIGHT;

  if (c.template.when.some(w => w.toLowerCase() === inputTypeId.toLowerCase())) {
    s += INPUT_TYPE_MATCH_WEIGHT;
    reason.push(`+${INPUT_TYPE_MATCH_WEIGHT} exact input type`);
  }

  let reliability = 0;
  let deprecatedPenalty = 0;
  for (const token of c.analysis.operators) {
    const op = byToken.get(token);
    if (!op) { reliability -= 15; continue; }
    switch (op.support) {
      case 'official': reliability += 4; break;
      case 'working': reliability += 2; break;
      case 'unreliable': reliability -= 10; break;
      case 'unknown': reliability -= 15; break;
      case 'deprecated': deprecatedPenalty += 30; break;
    }
  }
  s += reliability;
  reason.push(`${reliability >= 0 ? '+' : ''}${reliability} operator reliability`);
  if (deprecatedPenalty > 0) { s -= deprecatedPenalty; reason.push(`−${deprecatedPenalty} deprecated operator`); }

  const extraOps = c.analysis.operators.length - COMPLEXITY_FREE_OPERATORS;
  if (extraOps > 0) { const p = extraOps * COMPLEXITY_PENALTY_PER_OPERATOR; s -= p; reason.push(`−${p} complexity`); }
  if (c.query.length > LONG_QUERY_LENGTH) { s -= LONG_QUERY_PENALTY; reason.push(`−${LONG_QUERY_PENALTY} long query`); }

  return { ...c, baseScore: s, baseReason: reason.join(' · ') };
}

/** Same canonical query from two templates → keep the higher-scored (then earlier) one. Preserves first-seen order of groups. */
export function deduplicate(candidates: Candidate[]): Candidate[] {
  const groups = new Map<string, Candidate[]>();
  for (const c of candidates) {
    const g = groups.get(c.canonical);
    if (g) g.push(c); else groups.set(c.canonical, [c]);
  }
  return [...groups.values()].map(g => [...g].sort((a, b) => b.baseScore - a.baseScore || a.catalogOrder - b.catalogOrder)[0]);
}

function tokensOf(canonical: string): Set<string> {
  return new Set(canonical.split(' ').filter(Boolean).map(t => t.replace(/^[()]+|[()]+$/g, '')));
}

function jaccard(a: Set<string>, b: Set<string>): number {
  if (a.size === 0 && b.size === 0) return 1;
  let inter = 0;
  for (const x of a) if (b.has(x)) inter++;
  const union = a.size + b.size - inter;
  return union === 0 ? 0 : inter / union;
}

export function rank(candidates: Candidate[], inputTypeId: string, byToken: Map<string, OperatorDefinition>, take: number): Candidate[] {
  const scored = deduplicate(candidates.map(c => score(c, inputTypeId, byToken)))
    .sort((a, b) => b.baseScore - a.baseScore || a.catalogOrder - b.catalogOrder);

  const selected: Candidate[] = [];
  const families = new Set<string>();
  const tokenSets: Set<string>[] = [];

  while (selected.length < take && scored.length > 0) {
    let best: Candidate | null = null;
    let bestScore = -Infinity;
    let bestReason = '';
    for (const c of scored) {
      const tokens = tokensOf(c.canonical);
      const diversity = families.has(c.template.family) ? 0 : DIVERSITY_BONUS;
      const similarity = tokenSets.length === 0 ? 0 : Math.max(...tokenSets.map(s => jaccard(s, tokens)));
      const simPenalty = similarity > SIMILARITY_THRESHOLD ? SIMILARITY_PENALTY * similarity : 0;
      const total = c.baseScore + diversity - simPenalty;
      if (total > bestScore || (total === bestScore && best !== null && c.catalogOrder < best.catalogOrder)) {
        best = c; bestScore = total;
        bestReason = c.baseReason
          + (diversity > 0 ? ` · +${diversity} new family (${c.template.family})` : '')
          + (simPenalty > 0 ? ` · −${Math.round(simPenalty)} similar to a selected variant` : '');
      }
    }
    if (best === null) break;
    selected.push({ ...best, baseScore: bestScore, baseReason: bestReason });
    families.add(best.template.family);
    tokenSets.push(tokensOf(best.canonical));
    scored.splice(scored.indexOf(best), 1);
  }
  return selected;
}
