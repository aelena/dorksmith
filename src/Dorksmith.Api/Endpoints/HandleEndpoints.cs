using System.Diagnostics;
using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Handles;
using Dorksmith.Api.Http;
using Dorksmith.Api.Logging;
using Dorksmith.Api.Privacy;
using Dorksmith.Api.RateLimiting;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Endpoints;

public static class HandleEndpoints
{
    public static IEndpointRouteBuilder MapHandleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/handles/expand", async (
            HttpContext ctx, ICatalogProvider catalogs, IHandleExpander expander, IOptions<UsernameSearchOptions> options,
            ClientKeyProvider clientKeys, IRequestQuotaService quotas, SearchLogWriter log, TimeProvider time) =>
        {
            if (!options.Value.Enabled) return ApiErrors.FeatureDisabled("Username search is disabled on this instance.");
            if (!catalogs.IsLoaded) return ApiErrors.NotReady();
            var sw = Stopwatch.StartNew();
            var requestId = RequestId.New(time);
            ctx.Response.Headers["X-Request-Id"] = requestId;
            ctx.Response.Headers.CacheControl = "no-store";

            var (request, error) = await JsonBody.ReadAsync<HandleExpandRequest>(ctx.Request, ctx.RequestAborted);
            if (error is not null || request is null) return error!;

            var identity = clientKeys.Resolve(ctx);
            var quota = await quotas.ConsumeAsync(identity.RateLimitKey, DorkEndpoints.QuotaBucket, ctx.RequestAborted);
            var now = time.GetUtcNow();
            RateLimitHeaders.Apply(ctx.Response, quota, now);
            var options0 = new { request.Categories, request.PlatformIds, request.MaxPlatforms };
            if (!quota.Allowed)
            {
                await log.WriteAsync(ctx, identity, requestId, "username", "handle-expand", "google", request.Username, options0, 0, 429, sw, ctx.RequestAborted);
                return ApiErrors.RateLimited(quota.RetryAfterSeconds(now));
            }

            try
            {
                var result = expander.Expand(request) with { RateLimit = RateLimitHeaders.ToInfo(quota) };
                await log.WriteAsync(ctx, identity, requestId, "username", "handle-expand", "google", result.NormalizedUsername, options0, result.Profiles.Count, 200, sw, ctx.RequestAborted);
                return Results.Ok(result);
            }
            catch (InputValidationException ex)
            {
                await log.WriteAsync(ctx, identity, requestId, "username", "handle-expand", "google", request.Username, options0, 0, ex.Unprocessable ? 422 : 400, sw, ctx.RequestAborted);
                return ex.ToResult();
            }
        });
        return app;
    }
}
