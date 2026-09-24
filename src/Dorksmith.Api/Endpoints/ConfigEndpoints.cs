using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Endpoints;

public static class ConfigEndpoints
{
    /// <summary>Non-secret runtime configuration the SPA needs. Never add secrets here.</summary>
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/config/public", (
            HttpContext ctx,
            ICatalogProvider catalogs,
            IOptions<GenerationOptions> generation,
            IOptions<RateLimitingOptions> rateLimiting,
            IOptions<SearchLogOptions> searchLog,
            IOptions<PrivacyOptions> privacy,
            IOptions<UsernameSearchOptions> usernames) =>
        {
            var g = generation.Value;
            var r = rateLimiting.Value;
            var perHour = r.Enabled ? (int)Math.Round(r.PermitLimit * 60.0 / r.WindowMinutes) : 0;
            ctx.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Ok(new PublicConfig(
                MaxVariants: g.MaxVariants,
                DefaultVariants: g.DefaultVariants,
                MaxQueryLength: g.MaxQueryLength,
                MaxExcludeTerms: g.MaxExcludeTerms,
                MaxFileTypes: g.MaxFileTypes,
                RateLimitEnabled: r.Enabled,
                RateLimitPerHour: perHour,
                RateLimitPermit: r.PermitLimit,
                RateLimitWindowMinutes: r.WindowMinutes,
                SupportedEngines: g.SupportedEngines,
                UsernameSearchEnabled: usernames.Value.Enabled,
                MaxUsernameLength: usernames.Value.MaxUsernameLength,
                MaxPlatforms: usernames.Value.MaxPlatforms,
                SearchLogEnabled: searchLog.Value.Enabled,
                SearchLogRetentionDays: searchLog.Value.RetentionDays,
                IpLoggingMode: privacy.Value.IpLoggingMode,
                CatalogVersion: catalogs.CatalogVersion));
        });
        return app;
    }
}

public sealed record PublicConfig(
    int MaxVariants,
    int DefaultVariants,
    int MaxQueryLength,
    int MaxExcludeTerms,
    int MaxFileTypes,
    bool RateLimitEnabled,
    int RateLimitPerHour,
    int RateLimitPermit,
    int RateLimitWindowMinutes,
    string[] SupportedEngines,
    bool UsernameSearchEnabled,
    int MaxUsernameLength,
    int MaxPlatforms,
    bool SearchLogEnabled,
    int SearchLogRetentionDays,
    IpLoggingMode IpLoggingMode,
    string CatalogVersion);
