using Dorksmith.Api.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace Dorksmith.Api.Http;

/// <summary>
/// Converts every unhandled exception into the stable <see cref="ApiError"/> envelope. Malformed JSON and
/// oversized bodies become 400/413; anything else is a 500 without internal details. The exception is
/// still written to the application log with its request id.
/// </summary>
public static class ErrorHandling
{
    public static IApplicationBuilder UseApiErrorHandling(this IApplicationBuilder app) => app.UseExceptionHandler(errors => errors.Run(async ctx =>
    {
        var feature = ctx.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dorksmith.Api.Errors");

        var (status, error) = ex switch
        {
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => (StatusCodes.Status413PayloadTooLarge, new ApiError("payload_too_large", "Request payload too large.")),
            BadHttpRequestException bad when bad.Message.Contains("JSON", StringComparison.OrdinalIgnoreCase) => (StatusCodes.Status400BadRequest, new ApiError("invalid_input", "Request body is not valid JSON.", "body")),
            BadHttpRequestException bad => (bad.StatusCode, new ApiError("invalid_input", "Malformed request.")),
            _ => (StatusCodes.Status500InternalServerError, new ApiError("internal_error", "Unexpected server error.")),
        };

        if (status >= 500) logger.LogError(ex, "Unhandled exception for {Method} {Path} (trace {TraceId})", ctx.Request.Method, ctx.Request.Path, ctx.TraceIdentifier);
        else logger.LogInformation("Rejected request {Method} {Path}: {Error}", ctx.Request.Method, ctx.Request.Path, error.Error);

        ctx.Response.StatusCode = status;
        ctx.Response.Headers.CacheControl = "no-store";
        await ctx.Response.WriteAsJsonAsync(error, AppJson.Options);
    }));
}
