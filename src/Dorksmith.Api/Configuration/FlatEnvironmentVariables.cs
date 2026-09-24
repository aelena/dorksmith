namespace Dorksmith.Api.Configuration;

/// <summary>
/// Maps the flat, operator-friendly environment variables documented in the README
/// (e.g. <c>RATE_LIMIT_PERMIT_LIMIT</c>) onto the hierarchical configuration keys
/// (<c>RateLimiting:PermitLimit</c>). Standard <c>Section__Key</c> variables still work.
/// </summary>
public static class FlatEnvironmentVariables
{
    private static readonly IReadOnlyDictionary<string, string> Scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RATE_LIMIT_ENABLED"] = "RateLimiting:Enabled",
        ["RATE_LIMIT_PERMIT_LIMIT"] = "RateLimiting:PermitLimit",
        ["RATE_LIMIT_WINDOW_MINUTES"] = "RateLimiting:WindowMinutes",
        ["RATE_LIMIT_QUEUE_LIMIT"] = "RateLimiting:QueueLimit",
        ["SEARCH_LOG_ENABLED"] = "SearchLog:Enabled",
        ["SEARCH_LOG_RETENTION_DAYS"] = "SearchLog:RetentionDays",
        ["SEARCH_LOG_CLEANUP_INTERVAL_MINUTES"] = "SearchLog:CleanupIntervalMinutes",
        ["SQLITE_PATH"] = "SearchLog:SqlitePath",
        ["IP_LOGGING_MODE"] = "Privacy:IpLoggingMode",
        ["IP_HMAC_SECRET"] = "Privacy:IpHmacSecret",
        ["MAX_QUERY_LENGTH"] = "Generation:MaxQueryLength",
        ["MAX_VARIANTS"] = "Generation:MaxVariants",
        ["DEFAULT_VARIANTS"] = "Generation:DefaultVariants",
        ["CATALOG_PATH"] = "Catalogs:Path",
        ["WEB_PATH"] = "Web:Path",
        ["SERVE_STATIC"] = "Web:ServeStatic",
        ["USERNAME_SEARCH_ENABLED"] = "UsernameSearch:Enabled",
        ["USERNAME_MAX_PLATFORMS"] = "UsernameSearch:MaxPlatforms",
        ["FORWARD_LIMIT"] = "Proxy:ForwardLimit",
    };

    private static readonly IReadOnlyDictionary<string, string> Lists = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CORS_ALLOWED_ORIGINS"] = "Web:CorsAllowedOrigins",
        ["KNOWN_PROXIES"] = "Proxy:KnownProxies",
        ["KNOWN_NETWORKS"] = "Proxy:KnownNetworks",
        ["SUPPORTED_ENGINES"] = "Generation:SupportedEngines",
    };

    public static IConfigurationBuilder AddFlatEnvironmentVariables(this IConfigurationBuilder builder, Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var values = new Dictionary<string, string?>();

        foreach (var (env, key) in Scalars)
            if (read(env) is { Length: > 0 } v) values[key] = v;

        foreach (var (env, key) in Lists)
            if (read(env) is { Length: > 0 } v)
            {
                var items = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (var i = 0; i < items.Length; i++) values[$"{key}:{i}"] = items[i];
            }

        return values.Count == 0 ? builder : builder.AddInMemoryCollection(values);
    }
}
