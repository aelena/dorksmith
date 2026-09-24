using System.Diagnostics;
using System.Text.Json;
using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Generation;
using Dorksmith.Api.Http;
using Dorksmith.Api.Privacy;

namespace Dorksmith.Api.Logging;

/// <summary>
/// Builds and persists one <see cref="SearchLogEntry"/> per request through <see cref="ISearchLogStore"/>.
/// Failures are logged as "database write failure" and never fail the user's request.
/// Only the fields listed in the spec are stored: no headers, cookies or result contents.
/// </summary>
public sealed class SearchLogWriter(ISearchLogStore store, ICatalogProvider catalogs, TimeProvider time, ILogger<SearchLogWriter> logger)
{
    public async Task WriteAsync(HttpContext ctx, ClientIdentity identity, string requestId, string inputType, string intent, string engine,
        string? rawInput, object? options, int variantCount, int httpStatus, Stopwatch stopwatch, CancellationToken ct)
    {
        try
        {
            var entry = new SearchLogEntry(
                Id: requestId,
                OccurredAtUtc: time.GetUtcNow(),
                ClientKey: identity.PersistentKey,
                IpMode: identity.Mode.ToString(),
                InputType: Trunc(inputType, 32),
                Intent: Trunc(intent, 64),
                Engine: Trunc(engine, 32),
                NormalizedInput: Trunc(QueryNormalizer.NormalizeText(rawInput), 500),
                OptionsJson: options is null ? null : Trunc(JsonSerializer.Serialize(options, AppJson.Options), 2000),
                VariantCount: variantCount,
                HttpStatus: httpStatus,
                RequestDurationMs: stopwatch.ElapsedMilliseconds,
                CatalogVersion: catalogs.CatalogVersion,
                UserAgentFamily: UserAgentFamily(ctx.Request.Headers.UserAgent.ToString()));
            await store.AppendAsync(entry, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Database write failure: search log entry {RequestId} was not persisted", requestId);
        }
    }

    /// <summary>Coarse family only (no fingerprinting): browser, cli, bot, other, or null when absent.</summary>
    public static string? UserAgentFamily(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return null;
        var s = ua.ToLowerInvariant();
        if (s.Contains("bot") || s.Contains("spider") || s.Contains("crawler")) return "bot";
        if (s.StartsWith("curl") || s.StartsWith("wget") || s.Contains("httpie") || s.Contains("python-requests") || s.Contains("powershell") || s.Contains("postman")) return "cli";
        if (s.Contains("mozilla") || s.Contains("chrome") || s.Contains("safari") || s.Contains("firefox")) return "browser";
        return "other";
    }

    private static string Trunc(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max];
}
