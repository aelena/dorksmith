namespace Dorksmith.Api.Contracts;

public sealed record HandleExpandRequest(
    string? Username,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? PlatformIds = null,
    int? MaxPlatforms = null);

/// <summary>A constructible public profile URL. <c>Status</c> is always "not-checked": Dorksmith never probes.</summary>
public sealed record ProfileCandidate(
    string PlatformId,
    string PlatformName,
    string Category,
    string? Url,
    string SearchQuery,
    string Status,
    string? Caveat);

public sealed record HandleQuery(string Id, string Label, string Query, string Explanation);

public sealed record HandleExpandResponse(
    string Username,
    string NormalizedUsername,
    string Notice,
    IReadOnlyList<ProfileCandidate> Profiles,
    IReadOnlyList<HandleQuery> Queries,
    IReadOnlyList<string> Warnings,
    string CatalogVersion,
    RateLimitInfo? RateLimit);
