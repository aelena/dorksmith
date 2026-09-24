using System.Security.Cryptography;
using System.Text.Json;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Catalogs;

/// <summary>
/// Loads every catalog from the configured data directory exactly once, validates them, and keeps
/// pre-serialised responses with ETags. Failures are captured (not thrown) so the process starts and
/// <c>/health/ready</c> reports the problem.
/// </summary>
public sealed class JsonCatalogProvider : ICatalogProvider
{
    private static readonly IntentCatalog EmptyIntents = new();
    private static readonly PlatformCatalog EmptyPlatforms = new();
    private static readonly FileTypeCatalog EmptyFileTypes = new();

    private readonly Dictionary<string, OperatorCatalog> _operators = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CatalogResource> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _errors = [];

    public JsonCatalogProvider(IOptions<CatalogOptions> options, IHostEnvironment env, ILogger<JsonCatalogProvider> logger)
    {
        CatalogPath = ContentPaths.Resolve(env.ContentRootPath, options.Value.Path, "intents.json") ?? options.Value.Path;
        Load(logger);
    }

    public bool IsLoaded => _errors.Count == 0;
    public IReadOnlyList<string> Errors => _errors;
    public string CatalogVersion { get; private set; } = "unloaded";
    public string CatalogPath { get; }
    public IReadOnlyList<string> Engines => _operators.Keys.Order(StringComparer.Ordinal).ToList();
    public IntentCatalog Intents { get; private set; } = EmptyIntents;
    public PlatformCatalog Platforms { get; private set; } = EmptyPlatforms;
    public FileTypeCatalog FileTypes { get; private set; } = EmptyFileTypes;

    public bool TryGetOperators(string engine, out OperatorCatalog catalog) => _operators.TryGetValue(engine ?? "", out catalog!);
    public bool TryGetResource(string key, out CatalogResource resource) => _resources.TryGetValue(key, out resource!);

    private void Load(ILogger logger)
    {
        if (!Directory.Exists(CatalogPath))
        {
            _errors.Add($"catalog directory not found: {CatalogPath}");
            logger.LogError("Catalog directory {Path} not found", CatalogPath);
            return;
        }

        var operatorCatalogs = new List<OperatorCatalog>();
        foreach (var file in Directory.EnumerateFiles(CatalogPath, "operators.*.json").Order(StringComparer.Ordinal))
        {
            var raw = Read<OperatorCatalog>(file);
            if (raw is null) continue;
            var catalog = new OperatorCatalog
            {
                SchemaVersion = raw.SchemaVersion, CatalogVersion = raw.CatalogVersion, Engine = raw.Engine, EngineName = raw.EngineName,
                SearchUrlTemplate = raw.SearchUrlTemplate, Notes = raw.Notes, Operators = raw.Operators,
                ByToken = raw.Operators.GroupBy(o => o.Token, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            };
            operatorCatalogs.Add(catalog);
        }

        var fileTypesRaw = Read<FileTypeCatalog>(Path.Combine(CatalogPath, "filetypes.json"));
        var platformsRaw = Read<PlatformCatalog>(Path.Combine(CatalogPath, "platforms.json"));
        var intentsRaw = Read<IntentCatalog>(Path.Combine(CatalogPath, "intents.json"));
        if (_errors.Count > 0 || fileTypesRaw is null || platformsRaw is null || intentsRaw is null) return;

        var fileTypes = new FileTypeCatalog
        {
            SchemaVersion = fileTypesRaw.SchemaVersion, CatalogVersion = fileTypesRaw.CatalogVersion, Groups = fileTypesRaw.Groups, Extensions = fileTypesRaw.Extensions,
            ByExtension = fileTypesRaw.Extensions.GroupBy(e => e.Ext, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
        };
        var platforms = new PlatformCatalog
        {
            SchemaVersion = platformsRaw.SchemaVersion, CatalogVersion = platformsRaw.CatalogVersion, Notes = platformsRaw.Notes, Categories = platformsRaw.Categories, Platforms = platformsRaw.Platforms,
            ById = platformsRaw.Platforms.GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
        };
        var intents = new IntentCatalog
        {
            SchemaVersion = intentsRaw.SchemaVersion, CatalogVersion = intentsRaw.CatalogVersion, Notes = intentsRaw.Notes, Families = intentsRaw.Families, Groups = intentsRaw.Groups, Intents = intentsRaw.Intents,
            ById = intentsRaw.Intents.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            FamilyById = intentsRaw.Families.GroupBy(f => f.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
        };

        _errors.AddRange(CatalogValidator.Validate(operatorCatalogs, intents, platforms, fileTypes));
        if (_errors.Count > 0)
        {
            foreach (var e in _errors) logger.LogError("Malformed catalog: {Error}", e);
            return;
        }

        foreach (var c in operatorCatalogs) { _operators[c.Engine] = c; AddResource($"operators:{c.Engine}", c); }
        Intents = intents;
        Platforms = platforms;
        FileTypes = fileTypes;
        CatalogVersion = intents.CatalogVersion;
        AddResource("filetypes", fileTypes);
        AddResource("platforms", new { platforms.CatalogVersion, platforms.Categories, Platforms = platforms.Platforms.Where(p => p.Enabled) });
        AddResource("intents", PublicIntents.Project(intents));

        logger.LogInformation("Catalogs loaded from {Path}: version {Version}, {Engines} engine(s), {Intents} intents, {Templates} templates, {Platforms} platforms, {FileTypes} file types",
            CatalogPath, CatalogVersion, _operators.Count, intents.Intents.Count, intents.Intents.Sum(i => i.Templates.Count), platforms.Platforms.Count, fileTypes.Extensions.Count);
    }

    private T? Read<T>(string file) where T : class
    {
        if (!File.Exists(file)) { _errors.Add($"missing catalog file: {Path.GetFileName(file)}"); return null; }
        try
        {
            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize<T>(stream, AppJson.Options) ?? throw new JsonException("empty document");
        }
        catch (JsonException ex)
        {
            _errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            return null;
        }
    }

    private void AddResource(string key, object payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, AppJson.Options);
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(json))[..32].ToLowerInvariant() + "\"";
        _resources[key] = new CatalogResource(key, json, etag);
    }
}

/// <summary>Projection of the intent catalog for the UI: everything except the template patterns.</summary>
public static class PublicIntents
{
    public static object Project(IntentCatalog c) => new
    {
        c.CatalogVersion,
        c.Families,
        c.Groups,
        Intents = c.Intents.Select(i => new
        {
            i.Id, i.Label, i.Group, i.Description, i.Safety, i.CompatibleInputTypes, i.Tags,
            i.RequiresOptions, i.RequiresOptionsForInputTypes, i.DefaultFileTypes,
            TemplateCount = i.Templates.Count,
            Families = i.Templates.Select(t => t.Family).Distinct().ToList(),
        }),
    };
}

public sealed class CatalogHealthCheck(ICatalogProvider catalogs) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(catalogs.IsLoaded
            ? HealthCheckResult.Healthy($"catalog {catalogs.CatalogVersion}")
            : HealthCheckResult.Unhealthy($"{catalogs.Errors.Count} catalog error(s): {string.Join("; ", catalogs.Errors.Take(5))}"));
}
