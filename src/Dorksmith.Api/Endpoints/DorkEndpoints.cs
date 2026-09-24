using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Generation;
using Dorksmith.Api.Http;

namespace Dorksmith.Api.Endpoints;

public static class DorkEndpoints
{
    public static IEndpointRouteBuilder MapDorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dorks");

        group.MapPost("/generate", async (ICatalogProvider catalogs, IDorkGenerator generator, HttpContext ctx) =>
        {
            if (!catalogs.IsLoaded) return ApiErrors.NotReady();
            var (request, error) = await JsonBody.ReadAsync<GenerateRequest>(ctx.Request, ctx.RequestAborted);
            if (error is not null || request is null) return error!;
            try
            {
                var result = generator.Generate(request);
                ctx.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new GenerateResponse(
                    RequestId: RequestId.New(),
                    CatalogVersion: result.CatalogVersion,
                    Engine: result.Context.Operators.Engine,
                    Intent: result.Context.Intent.Id,
                    InputType: result.Context.InputTypeId,
                    NormalizedInput: result.Context.Input,
                    Variants: result.Variants,
                    RateLimit: null));
            }
            catch (InputValidationException ex) { return ex.ToResult(); }
        });

        group.MapPost("/validate", async (ICatalogProvider catalogs, IDorkGenerator generator, HttpContext ctx) =>
        {
            if (!catalogs.IsLoaded) return ApiErrors.NotReady();
            var (request, error) = await JsonBody.ReadAsync<ValidateQueryRequest>(ctx.Request, ctx.RequestAborted);
            if (error is not null || request is null) return error!;
            if ((request.Query ?? "").Length > 2000) return ApiErrors.InvalidInput("query exceeds 2000 characters.", "query");
            try { return Results.Ok(generator.Validate(request.Query ?? "", request.Engine ?? "google")); }
            catch (InputValidationException ex) { return ex.ToResult(); }
        });

        return app;
    }
}
