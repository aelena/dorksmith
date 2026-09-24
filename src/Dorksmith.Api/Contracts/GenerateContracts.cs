namespace Dorksmith.Api.Contracts;

public sealed record GenerateOptions
{
    public IReadOnlyList<string>? FileTypes { get; init; }
    public IReadOnlyList<string>? ExcludeTerms { get; init; }
    /// <summary>ISO date YYYY-MM-DD.</summary>
    public string? After { get; init; }
    /// <summary>ISO date YYYY-MM-DD.</summary>
    public string? Before { get; init; }
    /// <summary>Optional target site for non-domain inputs.</summary>
    public string? Site { get; init; }
    public int? MaxVariants { get; init; }
    // Context used by person / username intents.
    public string? Organization { get; init; }
    public string? Location { get; init; }
    public string? Role { get; init; }
    public string? DisplayName { get; init; }
}

public sealed record GenerateRequest(
    string? Input,
    string? InputType,
    string? Intent,
    string? Engine = "google",
    GenerateOptions? Options = null);

public sealed record DorkVariant(
    string Id,
    string Label,
    string Family,
    string Query,
    string Explanation,
    IReadOnlyList<string> Operators,
    IReadOnlyList<string> Warnings,
    string RankReason);

public sealed record RateLimitInfo(int Limit, int Remaining, DateTimeOffset ResetAtUtc);

public sealed record GenerateResponse(
    string RequestId,
    string CatalogVersion,
    string Engine,
    string Intent,
    string InputType,
    string NormalizedInput,
    IReadOnlyList<DorkVariant> Variants,
    RateLimitInfo? RateLimit);

public sealed record ValidateQueryRequest(string? Query, string? Engine = "google");

public sealed record OperatorUse(string Token, string Name, string Support, bool Generate);

public sealed record ValidateQueryResponse(
    string Query,
    string Engine,
    IReadOnlyList<OperatorUse> Operators,
    IReadOnlyList<string> Warnings,
    bool HasErrors);
