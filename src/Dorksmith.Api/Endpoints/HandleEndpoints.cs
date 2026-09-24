using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Handles;
using Dorksmith.Api.Http;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Endpoints;

public static class HandleEndpoints
{
    public static IEndpointRouteBuilder MapHandleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/handles/expand", async (ICatalogProvider catalogs, IHandleExpander expander, IOptions<UsernameSearchOptions> options, HttpContext ctx) =>
        {
            if (!options.Value.Enabled) return ApiErrors.FeatureDisabled("Username search is disabled on this instance.");
            if (!catalogs.IsLoaded) return ApiErrors.NotReady();
            var (request, error) = await JsonBody.ReadAsync<HandleExpandRequest>(ctx.Request, ctx.RequestAborted);
            if (error is not null || request is null) return error!;
            try
            {
                ctx.Response.Headers.CacheControl = "no-store";
                return Results.Ok(expander.Expand(request));
            }
            catch (InputValidationException ex) { return ex.ToResult(); }
        });
        return app;
    }
}
