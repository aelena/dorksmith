using Dorksmith.Api.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Http;

/// <summary>Serves the plain HTML/JS SPA directly from the API in development or single-container setups.
/// In the Compose topology nginx serves the SPA and this is disabled with <c>SERVE_STATIC=false</c>.</summary>
public static class StaticWeb
{
    public static WebApplication MapStaticWeb(this WebApplication app)
    {
        var opts = app.Services.GetRequiredService<IOptions<WebOptions>>().Value;
        if (!opts.ServeStatic) return app;

        var path = ContentPaths.Resolve(app.Environment.ContentRootPath, opts.Path, "index.html");
        if (path is null)
        {
            app.Logger.LogWarning("Static web root {Path} not found; SPA will not be served by the API", opts.Path);
            return app;
        }

        var provider = new PhysicalFileProvider(path);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
                ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "no-cache" : "public, max-age=3600",
        });
        // SPA fallback for client routes only; /api and /health must keep returning real 404s.
        app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|health/).*$)}", "index.html", new StaticFileOptions { FileProvider = provider });
        app.Logger.LogInformation("Serving static web from {Path}", path);
        return app;
    }
}
