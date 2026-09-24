namespace Dorksmith.Api.Catalogs;

/// <summary>Read-only access to the versioned JSON catalogs. Loaded once at startup; readiness fails if invalid.</summary>
public interface ICatalogProvider
{
    bool IsLoaded { get; }
    IReadOnlyList<string> Errors { get; }
    string CatalogVersion { get; }
    string CatalogPath { get; }

    IReadOnlyList<string> Engines { get; }
    bool TryGetOperators(string engine, out OperatorCatalog catalog);
    IntentCatalog Intents { get; }
    PlatformCatalog Platforms { get; }
    FileTypeCatalog FileTypes { get; }

    /// <summary>Pre-serialised JSON + ETag for a cacheable catalog resource ("operators:google", "intents", "filetypes", "platforms").</summary>
    bool TryGetResource(string key, out CatalogResource resource);
}

public sealed record CatalogResource(string Key, byte[] Json, string ETag);
