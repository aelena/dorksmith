using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Dorksmith.Api.Tests;

/// <summary>Boots the API against the repository's real catalogs with an in-memory SQLite log and
/// generous rate limits. Individual tests override settings through <see cref="WithSettings"/>.</summary>
public class DorksmithFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new();

    public static string RepoRoot { get; } = FindRepoRoot();

    public DorksmithFactory WithSettings(params (string Key, string? Value)[] settings)
    {
        foreach (var (k, v) in settings) _settings[k] = v;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(RepoRoot);
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            var defaults = new Dictionary<string, string?>
            {
                ["Catalogs:Path"] = Path.Combine(RepoRoot, "data"),
                ["Web:ServeStatic"] = "false",
                ["SearchLog:SqlitePath"] = ":memory:",
                ["RateLimiting:PermitLimit"] = "10000",
                ["Privacy:IpHmacSecret"] = "test-secret-not-for-production",
            };
            foreach (var (k, v) in _settings) defaults[k] = v;
            cfg.AddInMemoryCollection(defaults);
        });
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "intents.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (containing data/intents.json) not found.");
    }
}
