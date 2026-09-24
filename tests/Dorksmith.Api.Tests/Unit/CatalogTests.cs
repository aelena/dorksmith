using System.Text.Json;
using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Tests.Unit;

public class CatalogTests
{
    public static ICatalogProvider LoadRepoCatalogs(string? path = null)
    {
        var env = new FakeHostEnvironment { ContentRootPath = DorksmithFactory.RepoRoot };
        return new JsonCatalogProvider(Options.Create(new CatalogOptions { Path = path ?? Path.Combine(DorksmithFactory.RepoRoot, "data") }), env, NullLogger<JsonCatalogProvider>.Instance);
    }

    [Fact]
    public void Repository_catalogs_load_without_errors()
    {
        var c = LoadRepoCatalogs();
        Assert.True(c.IsLoaded, string.Join("\n", c.Errors));
        Assert.Contains("google", c.Engines);
        Assert.NotEmpty(c.Intents.Intents);
        Assert.NotEmpty(c.Platforms.Platforms);
        Assert.NotEmpty(c.FileTypes.Extensions);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", c.CatalogVersion);
    }

    [Fact]
    public void Deprecated_operators_never_generate()
    {
        var c = LoadRepoCatalogs();
        Assert.True(c.TryGetOperators("google", out var ops));
        var deprecated = ops.Operators.Where(o => o.Support == OperatorSupport.Deprecated).ToList();
        Assert.NotEmpty(deprecated);
        Assert.All(deprecated, o => Assert.False(o.Generate, $"{o.Token} is deprecated but generate=true"));
        Assert.Equal(OperatorSupport.Deprecated, ops.ByToken["cache:"].Support);
    }

    [Fact]
    public void High_confidence_google_operators_are_present_and_generatable()
    {
        var c = LoadRepoCatalogs();
        c.TryGetOperators("google", out var ops);
        foreach (var token in new[] { "\"", "-", "OR", "(", "site:", "filetype:", "before:", "after:", "intitle:", "inurl:", "intext:" })
        {
            Assert.True(ops.ByToken.TryGetValue(token, out var op), $"missing operator {token}");
            Assert.True(op!.Generate, $"{token} must be generatable");
            Assert.True(op.IsReliable, $"{token} must be official or working");
        }
    }

    [Fact]
    public void Every_template_uses_only_known_placeholders_and_valid_when_types()
    {
        var c = LoadRepoCatalogs();
        foreach (var intent in c.Intents.Intents)
        foreach (var t in intent.Templates)
        {
            foreach (var name in Generation.Placeholders.In(t.Pattern))
                Assert.Contains(name, Generation.Placeholders.All);
            foreach (var w in t.When.Where(w => w != "*"))
                Assert.Contains(w, intent.CompatibleInputTypes);
        }
    }

    [Fact]
    public void Missing_directory_reports_error_and_not_loaded()
    {
        var c = LoadRepoCatalogs(Path.Combine(Path.GetTempPath(), "dorksmith-missing-" + Guid.NewGuid().ToString("N")));
        Assert.False(c.IsLoaded);
        Assert.Contains(c.Errors, e => e.Contains("not found"));
    }

    [Fact]
    public void Malformed_catalog_fails_validation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dorksmith-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(DorksmithFactory.RepoRoot, "data");
            foreach (var f in Directory.GetFiles(src, "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));

            // Flip cache: to generate=true — a deprecated operator must never generate.
            var opsPath = Path.Combine(dir, "operators.google.json");
            var doc = JsonNode.Parse(File.ReadAllText(opsPath))!;
            var cache = doc["operators"]!.AsArray().First(o => o!["token"]!.GetValue<string>() == "cache:")!;
            cache["generate"] = true;
            File.WriteAllText(opsPath, doc.ToJsonString(AppJson.Options));

            var c = LoadRepoCatalogs(dir);
            Assert.False(c.IsLoaded);
            Assert.Contains(c.Errors, e => e.Contains("cache:") && e.Contains("deprecated"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Unknown_placeholder_fails_validation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dorksmith-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(DorksmithFactory.RepoRoot, "data");
            foreach (var f in Directory.GetFiles(src, "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var intentsPath = Path.Combine(dir, "intents.json");
            var doc = JsonNode.Parse(File.ReadAllText(intentsPath))!;
            doc["intents"]![0]!["templates"]![0]!["pattern"] = "{nope} {site}";
            File.WriteAllText(intentsPath, doc.ToJsonString(AppJson.Options));

            var c = LoadRepoCatalogs(dir);
            Assert.False(c.IsLoaded);
            Assert.Contains(c.Errors, e => e.Contains("{nope}"));
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Dorksmith.Api.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
