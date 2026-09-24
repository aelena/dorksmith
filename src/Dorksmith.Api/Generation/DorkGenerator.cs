using System.Text;
using System.Text.RegularExpressions;
using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Contracts;

namespace Dorksmith.Api.Generation;

public interface IDorkGenerator
{
    /// <summary>Validates, normalises and generates ranked variants. Throws <see cref="InputValidationException"/> for 400/422 cases.</summary>
    GenerationResult Generate(GenerateRequest request);

    /// <summary>Expert-mode analysis of a hand-written query. Never rewrites the query.</summary>
    ValidateQueryResponse Validate(string query, string engine);
}

public sealed record GenerationResult(GenerationContext Context, IReadOnlyList<DorkVariant> Variants, string CatalogVersion);

/// <summary>
/// raw input → normalise → classify/validate → load templates → expand placeholders → drop invalid
/// → canonicalise → de-duplicate → score/rank → top N with explanations. Fully deterministic.
/// </summary>
public sealed partial class DorkGenerator(ICatalogProvider catalogs, RequestValidator validator, PlaceholderResolver resolver) : IDorkGenerator
{
    [GeneratedRegex(@"\(\s*\)")] private static partial Regex EmptyParens();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();

    public GenerationResult Generate(GenerateRequest request)
    {
        if (!catalogs.IsLoaded) throw new InvalidOperationException("Catalogs are not loaded.");
        var ctx = validator.Build(request);

        var candidates = new List<Candidate>();
        var order = 0;
        foreach (var template in ctx.Intent.Templates)
        {
            order++;
            if (!template.AppliesTo(ctx.InputTypeId)) continue;
            var query = Expand(template, ctx);
            if (query is null) continue;

            var analysis = QueryValidator.Analyze(query, ctx.Operators);
            if (analysis.Operators.Any(t => ctx.Operators.ByToken.TryGetValue(t, out var op) && !op.Generate && op.Support == OperatorSupport.Deprecated))
                continue; // never emit deprecated operators, even if user-supplied exclusions smuggled one in

            candidates.Add(new Candidate(template, order, query, QueryNormalizer.Canonicalize(query), analysis));
        }

        var ranked = CandidateRanker.Rank(candidates, ctx.InputTypeId, ctx.Operators, ctx.MaxVariants);
        if (ranked.Count == 0)
            throw new InputValidationException($"No variants could be generated for intent '{ctx.Intent.Id}' with inputType '{ctx.InputTypeId}' and the supplied options.", "intent", unprocessable: true);

        var variants = ranked.Select(c => new DorkVariant(
            Id: c.Template.Id,
            Label: catalogs.Intents.FamilyById.TryGetValue(c.Template.Family, out var f) ? f.Label : c.Template.Family,
            Family: c.Template.Family,
            Query: c.Query,
            Explanation: c.Template.Explanation,
            Operators: c.Analysis.Operators,
            Warnings: c.Analysis.Warnings.Concat(c.Template.Warnings).Distinct().ToList(),
            RankReason: c.BaseReason)).ToList();

        return new GenerationResult(ctx, variants, catalogs.CatalogVersion);
    }

    public ValidateQueryResponse Validate(string query, string engine)
    {
        engine = string.IsNullOrWhiteSpace(engine) ? "google" : engine.Trim().ToLowerInvariant();
        if (!catalogs.TryGetOperators(engine, out var ops))
            throw new InputValidationException($"Engine '{engine}' is not supported.", "engine");
        var normalized = QueryNormalizer.NormalizeText(query);
        if (normalized.Length == 0) throw new InputValidationException("query is required.", "query");
        var analysis = QueryValidator.Analyze(normalized, ops);
        var uses = analysis.Operators.Select(t => ops.ByToken.TryGetValue(t, out var op)
            ? new OperatorUse(t, op.Name, op.Support.ToString().ToLowerInvariant(), op.Generate)
            : new OperatorUse(t, "Unknown", "unknown", false)).ToList();
        return new ValidateQueryResponse(normalized, engine, uses, analysis.Warnings, analysis.HasErrors);
    }

    /// <summary>Substitutes placeholders; returns null when a required placeholder cannot be resolved.</summary>
    public string? Expand(QueryTemplate template, GenerationContext ctx)
    {
        var optional = new HashSet<string>(Placeholders.OptionalByDefault, StringComparer.Ordinal);
        foreach (var o in template.Optional) optional.Add(o);
        foreach (var r in template.Requires) optional.Remove(r);

        foreach (var r in template.Requires)
            if (resolver.Resolve(r, ctx, template) is null) return null;

        var sb = new StringBuilder(template.Pattern.Length * 2);
        var pattern = template.Pattern;
        var i = 0;
        while (i < pattern.Length)
        {
            var open = pattern.IndexOf('{', i);
            if (open < 0) { sb.Append(pattern, i, pattern.Length - i); break; }
            var close = pattern.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(pattern, i, pattern.Length - i); break; }
            sb.Append(pattern, i, open - i);
            var name = pattern[(open + 1)..close];
            var value = resolver.Resolve(name, ctx, template);
            if (value is null && !optional.Contains(name)) return null;
            sb.Append(value);
            i = close + 1;
        }

        var query = EmptyParens().Replace(sb.ToString(), " ");
        query = Whitespace().Replace(query, " ").Trim();
        return query.Length == 0 ? null : query;
    }
}
