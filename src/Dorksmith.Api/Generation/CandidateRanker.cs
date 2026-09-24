using Dorksmith.Api.Catalogs;

namespace Dorksmith.Api.Generation;

public sealed record Candidate(
    QueryTemplate Template,
    int CatalogOrder,
    string Query,
    string Canonical,
    QueryAnalysis Analysis)
{
    public double BaseScore { get; init; }
    public string BaseReason { get; init; } = "";
}

/// <summary>
/// Deterministic scoring, de-duplication and diversity-aware selection.
///
///   score = template_weight + intent_match + operator_reliability + input_type_match
///           + diversity_bonus − deprecated_penalty − duplicate_similarity_penalty − complexity_penalty
///
/// Ties are broken by catalog order so identical input always yields identical output.
/// </summary>
public static class CandidateRanker
{
    public const int IntentMatchWeight = 10;
    public const int InputTypeMatchWeight = 10;
    public const int DiversityBonus = 15;
    public const int SimilarityPenalty = 20;
    public const double SimilarityThreshold = 0.7;
    public const int ComplexityFreeOperators = 4;
    public const int ComplexityPenaltyPerOperator = 3;
    public const int LongQueryPenalty = 5;
    public const int LongQueryLength = 160;

    public static IReadOnlyList<Candidate> Rank(IEnumerable<Candidate> candidates, string inputTypeId, OperatorCatalog operators, int take)
    {
        var scored = Deduplicate(candidates.Select(c => Score(c, inputTypeId, operators)))
            .OrderByDescending(c => c.BaseScore)
            .ThenBy(c => c.CatalogOrder)
            .ToList();

        var selected = new List<Candidate>(take);
        var families = new HashSet<string>(StringComparer.Ordinal);
        var tokenSets = new List<HashSet<string>>();

        while (selected.Count < take && scored.Count > 0)
        {
            Candidate? best = null;
            var bestScore = double.NegativeInfinity;
            var bestReason = "";
            foreach (var c in scored)
            {
                var tokens = Tokens(c.Canonical);
                var diversity = families.Contains(c.Template.Family) ? 0 : DiversityBonus;
                var similarity = tokenSets.Count == 0 ? 0 : tokenSets.Max(s => Jaccard(s, tokens));
                var simPenalty = similarity > SimilarityThreshold ? SimilarityPenalty * similarity : 0;
                var score = c.BaseScore + diversity - simPenalty;
                if (score > bestScore || (score == bestScore && best is not null && c.CatalogOrder < best.CatalogOrder))
                {
                    best = c; bestScore = score;
                    bestReason = c.BaseReason
                        + (diversity > 0 ? $" · +{diversity} new family ({c.Template.Family})" : "")
                        + (simPenalty > 0 ? $" · −{simPenalty:0} similar to a selected variant" : "");
                }
            }
            if (best is null) break;
            selected.Add(best with { BaseScore = bestScore, BaseReason = bestReason });
            families.Add(best.Template.Family);
            tokenSets.Add(Tokens(best.Canonical));
            scored.Remove(best);
        }
        return selected;
    }

    public static Candidate Score(Candidate c, string inputTypeId, OperatorCatalog operators)
    {
        var reason = new List<string> { $"weight {c.Template.Weight}", $"+{IntentMatchWeight} intent" };
        double score = c.Template.Weight + IntentMatchWeight;

        if (c.Template.When.Contains(inputTypeId, StringComparer.OrdinalIgnoreCase))
        {
            score += InputTypeMatchWeight;
            reason.Add($"+{InputTypeMatchWeight} exact input type");
        }

        var reliability = 0;
        var deprecatedPenalty = 0;
        foreach (var token in c.Analysis.Operators)
        {
            if (!operators.ByToken.TryGetValue(token, out var op)) { reliability -= 15; continue; }
            switch (op.Support)
            {
                case OperatorSupport.Official: reliability += 4; break;
                case OperatorSupport.Working: reliability += 2; break;
                case OperatorSupport.Unreliable: reliability -= 10; break;
                case OperatorSupport.Unknown: reliability -= 15; break;
                case OperatorSupport.Deprecated: deprecatedPenalty += 30; break;
            }
        }
        score += reliability;
        reason.Add($"{(reliability >= 0 ? "+" : "")}{reliability} operator reliability");
        if (deprecatedPenalty > 0) { score -= deprecatedPenalty; reason.Add($"−{deprecatedPenalty} deprecated operator"); }

        var extraOps = c.Analysis.Operators.Count - ComplexityFreeOperators;
        if (extraOps > 0) { var p = extraOps * ComplexityPenaltyPerOperator; score -= p; reason.Add($"−{p} complexity"); }
        if (c.Query.Length > LongQueryLength) { score -= LongQueryPenalty; reason.Add($"−{LongQueryPenalty} long query"); }

        return c with { BaseScore = score, BaseReason = string.Join(" · ", reason) };
    }

    /// <summary>Same canonical query from two templates → keep the higher-scored (then earlier) one.</summary>
    public static IEnumerable<Candidate> Deduplicate(IEnumerable<Candidate> candidates)
        => candidates
            .GroupBy(c => c.Canonical, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(c => c.BaseScore).ThenBy(c => c.CatalogOrder).First());

    private static HashSet<string> Tokens(string canonical)
        => canonical.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim('(', ')')).ToHashSet(StringComparer.Ordinal);

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1;
        var intersection = a.Intersect(b).Count();
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }
}
