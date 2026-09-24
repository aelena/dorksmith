using System.Diagnostics;
using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Generation;
using Dorksmith.Api.Http;
using Dorksmith.Api.Logging;
using Dorksmith.Api.Privacy;
using Dorksmith.Api.RateLimiting;

namespace Dorksmith.Api.Endpoints;

public static class DorkEndpoints
{
    /// <summary>Generation and handle expansion share one hourly bucket per client.</summary>
    public const string QuotaBucket = "generate";

    public static IEndpointRouteBuilder MapDorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dorks");

        group.MapPost("/generate", async (
            HttpContext ctx, ICatalogProvider catalogs, IDorkGenerator generator, ClientKeyProvider clientKeys,
            IRequestQuotaService quotas, SearchLogWriter log, TimeProvider time, ILogger<DorkGenerator> logger) =>
        {
            if (!catalogs.IsLoaded) return ApiErrors.NotReady();
            var sw = Stopwatch.StartNew();
            var requestId = RequestId.New(time);
            ctx.Response.Headers["X-Request-Id"] = requestId;
            ctx.Response.Headers.CacheControl = "no-store";

            var (request, error) = await JsonBody.ReadAsync<GenerateRequest>(ctx.Request, ctx.RequestAborted);
            if (error is not null || request is null) return error!;

            var identity = clientKeys.Resolve(ctx);
            var quota = await quotas.ConsumeAsync(identity.RateLimitKey, QuotaBucket, ctx.RequestAborted);
            var now = time.GetUtcNow();
            RateLimitHeaders.Apply(ctx.Response, quota, now);
            if (!quota.Allowed)
            {
                logger.LogInformation("Quota rejected for {Bucket} (request {RequestId})", QuotaBucket, requestId);
                await log.WriteAsync(ctx, identity, requestId, request.InputType ?? "", request.Intent ?? "", request.Engine ?? "google", request.Input, request.Options, 0, 429, sw, ctx.RequestAborted);
                return ApiErrors.RateLimited(quota.RetryAfterSeconds(now));
            }

            try
            {
                var result = generator.Generate(request);
                await log.WriteAsync(ctx, identity, requestId, result.Context.InputTypeId, result.Context.Intent.Id, result.Context.Operators.Engine,
                    result.Context.Input, request.Options, result.Variants.Count, 200, sw, ctx.RequestAborted);
                logger.LogInformation("Generation completed: {Intent}/{InputType} → {Count} variants in {Elapsed} ms", result.Context.Intent.Id, result.Context.InputTypeId, result.Variants.Count, sw.ElapsedMilliseconds);
                return Results.Ok(new GenerateResponse(
                    RequestId: requestId,
                    CatalogVersion: result.CatalogVersion,
                    Engine: result.Context.Operators.Engine,
                    Intent: result.Context.Intent.Id,
                    InputType: result.Context.InputTypeId,
                    NormalizedInput: result.Context.Input,
                    Variants: result.Variants,
                    RateLimit: RateLimitHeaders.ToInfo(quota)));
            }
            catch (InputValidationException ex)
            {
                await log.WriteAsync(ctx, identity, requestId, request.InputType ?? "", request.Intent ?? "", request.Engine ?? "google", request.Input, request.Options, 0, ex.Unprocessable ? 422 : 400, sw, ctx.RequestAborted);
                return ex.ToResult();
            }
        });

        group.MapPost("/validate", async (HttpContext ctx, ICatalogProvider catalogs, IDorkGenerator generator) =>
        {
            if (!catalogs.IsLoaded) return ApiErrors.NotReady();
            var (request, error) = await JsonBody.ReadAsync<ValidateQueryRequest>(ctx.Request, ctx.RequestAborted);
            if (error is not null || request is null) return error!;
            if ((request.Query ?? "").Length > 2000) return ApiErrors.InvalidInput("query exceeds 2000 characters.", "query");
            ctx.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(generator.Validate(request.Query ?? "", request.Engine ?? "google")); }
            catch (InputValidationException ex) { return ex.ToResult(); }
        });

        return app;
    }
}
