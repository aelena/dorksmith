using System.ComponentModel.DataAnnotations;

namespace Dorksmith.Api.Configuration;

public sealed class CatalogOptions
{
    public const string Section = "Catalogs";
    public string Path { get; set; } = "data";
}

public sealed class WebOptions
{
    public const string Section = "Web";
    public bool ServeStatic { get; set; } = true;
    public string Path { get; set; } = "web";
    public string[] CorsAllowedOrigins { get; set; } = [];
}

public sealed class GenerationOptions
{
    public const string Section = "Generation";
    [Range(16, 10_000)] public int MaxQueryLength { get; set; } = 500;
    [Range(1, 12)] public int MaxVariants { get; set; } = 12;
    [Range(1, 12)] public int DefaultVariants { get; set; } = 6;
    [Range(0, 100)] public int MaxExcludeTerms { get; set; } = 20;
    [Range(0, 50)] public int MaxFileTypes { get; set; } = 10;
    /// <summary>Bound from config; the binder appends arrays, so read <see cref="Engines"/> instead.</summary>
    public string[] SupportedEngines { get; set; } = [];
    public IReadOnlyList<string> Engines => SupportedEngines.Length == 0
        ? ["google"]
        : SupportedEngines.Select(e => e.Trim().ToLowerInvariant()).Distinct().ToArray();
}

public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";
    public bool Enabled { get; set; } = true;
    [Range(1, int.MaxValue)] public int PermitLimit { get; set; } = 25;
    [Range(1, 24 * 60)] public int WindowMinutes { get; set; } = 60;
    [Range(0, 1000)] public int QueueLimit { get; set; } = 0;
}

public sealed class SearchLogOptions
{
    public const string Section = "SearchLog";
    public bool Enabled { get; set; } = true;
    [Range(0, 3650)] public int RetentionDays { get; set; } = 30;
    public string SqlitePath { get; set; } = "state/dorksmith.db";
    [Range(1, 24 * 60)] public int CleanupIntervalMinutes { get; set; } = 60;
}

public enum IpLoggingMode { Hmac, Raw, None }

public sealed class PrivacyOptions
{
    public const string Section = "Privacy";
    public IpLoggingMode IpLoggingMode { get; set; } = IpLoggingMode.Hmac;
    public string? IpHmacSecret { get; set; }
}

public sealed class ProxyOptions
{
    public const string Section = "Proxy";
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
    /// <summary>Number of trusted reverse-proxy hops (X-Forwarded-For entries) to unwind. 1 = nginx only; 2 = Caddy → nginx.</summary>
    [Range(1, 8)] public int ForwardLimit { get; set; } = 1;
}

public sealed class UsernameSearchOptions
{
    public const string Section = "UsernameSearch";
    public bool Enabled { get; set; } = true;
    [Range(1, 500)] public int MaxUsernameLength { get; set; } = 100;
    [Range(1, 500)] public int MaxPlatforms { get; set; } = 100;
    [Range(1, 500)] public int DefaultPlatforms { get; set; } = 30;
}

public static class OptionsRegistration
{
    public static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration config)
    {
        Bind<CatalogOptions>(services, config, CatalogOptions.Section);
        Bind<WebOptions>(services, config, WebOptions.Section);
        Bind<GenerationOptions>(services, config, GenerationOptions.Section);
        Bind<RateLimitingOptions>(services, config, RateLimitingOptions.Section);
        Bind<SearchLogOptions>(services, config, SearchLogOptions.Section);
        Bind<PrivacyOptions>(services, config, PrivacyOptions.Section);
        Bind<ProxyOptions>(services, config, ProxyOptions.Section);
        Bind<UsernameSearchOptions>(services, config, UsernameSearchOptions.Section);
        return services;
    }

    private static void Bind<T>(IServiceCollection services, IConfiguration config, string section) where T : class
        => services.AddOptions<T>().Bind(config.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();
}
