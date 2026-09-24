using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Contracts;
using Microsoft.Net.Http.Headers;

namespace Dorksmith.Api.Endpoints;

/// <summary>Read-only catalog resources. All support ETag / If-None-Match and are cacheable by browsers and proxies.</summary>
public static class CatalogEndpoints
{
    private const string CacheControl = "public, max-age=300, stale-while-revalidate=3600";

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapGet("/operators", (HttpContext ctx, ICatalogProvider catalogs, string? engine) =>
        {
            if (!catalogs.IsLoaded) return ApiErrors.NotReady();
            engine = string.IsNullOrWhiteSpace(engine) ? "google" : engine.Trim().ToLowerInvariant();
            return catalogs.TryGetResource($"operators:{engine}", out var res)
                ? Conditional(ctx, res)
                : ApiErrors.NotFound($"No operator catalog for engine '{engine}'. Available: {string.Join(", ", catalogs.Engines)}.");
        });

        group.MapGet("/intents", (HttpContext ctx, ICatalogProvider catalogs) =>
            catalogs.IsLoaded && catalogs.TryGetResource("intents", out var res) ? Conditional(ctx, res) : ApiErrors.NotReady());

        group.MapGet("/filetypes", (HttpContext ctx, ICatalogProvider catalogs) =>
            catalogs.IsLoaded && catalogs.TryGetResource("filetypes", out var res) ? Conditional(ctx, res) : ApiErrors.NotReady());

        group.MapGet("/platforms", (HttpContext ctx, ICatalogProvider catalogs, string? q, string? category) =>
        {
            if (!catalogs.IsLoaded) return ApiErrors.NotReady();
            if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(category) && catalogs.TryGetResource("platforms", out var res))
                return Conditional(ctx, res);

            var needle = q?.Trim() ?? "";
            var cat = category?.Trim() ?? "";
            var platforms = catalogs.Platforms.Platforms
                .Where(p => p.Enabled)
                .Where(p => cat.Length == 0 || string.Equals(p.Category, cat, StringComparison.OrdinalIgnoreCase))
                .Where(p => needle.Length == 0
                            || p.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                            || p.SearchDomain.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Weight).ThenBy(p => p.Id, StringComparer.Ordinal)
                .ToList();

            ctx.Response.Headers.CacheControl = CacheControl;
            return Results.Ok(new { catalogs.Platforms.CatalogVersion, catalogs.Platforms.Categories, Platforms = platforms });
        });

        return app;
    }

    private static IResult Conditional(HttpContext ctx, CatalogResource res)
    {
        ctx.Response.Headers[HeaderNames.ETag] = res.ETag;
        ctx.Response.Headers.CacheControl = CacheControl;
        ctx.Response.Headers.Vary = HeaderNames.AcceptEncoding;

        var inm = ctx.Request.Headers.IfNoneMatch;
        if (inm.Count > 0 && inm.Any(v => v is not null && (v == "*" || v.Split(',').Select(s => s.Trim()).Contains(res.ETag))))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        return Results.Bytes(res.Json, "application/json; charset=utf-8");
    }
}
