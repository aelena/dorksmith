namespace Dorksmith.Api.Logging;

/// <summary>One record per generation/expansion request. Never contains headers, cookies or result contents.</summary>
public sealed record SearchLogEntry(
    string Id,
    DateTimeOffset OccurredAtUtc,
    string? ClientKey,
    string IpMode,
    string InputType,
    string Intent,
    string Engine,
    string NormalizedInput,
    string? OptionsJson,
    int VariantCount,
    int HttpStatus,
    long RequestDurationMs,
    string CatalogVersion,
    string? UserAgentFamily);

public interface ISearchLogStore
{
    Task AppendAsync(SearchLogEntry entry, CancellationToken ct);

    /// <summary>Deletes entries older than <paramref name="cutoffUtc"/>; returns the number removed.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct);
}

/// <summary>Read side, used by tests and future admin tooling. Not exposed over HTTP.</summary>
public interface ISearchLogReader
{
    Task<IReadOnlyList<SearchLogEntry>> ReadRecentAsync(int take, CancellationToken ct);
    Task<long> CountAsync(CancellationToken ct);
}

/// <summary>Used when <c>SearchLog:Enabled=false</c>.</summary>
public sealed class NullSearchLogStore : ISearchLogStore, ISearchLogReader
{
    public Task AppendAsync(SearchLogEntry entry, CancellationToken ct) => Task.CompletedTask;
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct) => Task.FromResult(0);
    public Task<IReadOnlyList<SearchLogEntry>> ReadRecentAsync(int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<SearchLogEntry>>([]);
    public Task<long> CountAsync(CancellationToken ct) => Task.FromResult(0L);
}
