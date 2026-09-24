using System.Text.Json.Serialization;

namespace Dorksmith.Api.Catalogs;

public enum OperatorSupport { Official, Working, Unreliable, Deprecated, Unknown }

// ---------- operators.<engine>.json ----------

public sealed class OperatorCatalog
{
    public int SchemaVersion { get; init; }
    public string CatalogVersion { get; init; } = "";
    public string Engine { get; init; } = "";
    public string EngineName { get; init; } = "";
    public string SearchUrlTemplate { get; init; } = "";
    public IReadOnlyList<string> Notes { get; init; } = [];
    public IReadOnlyList<OperatorDefinition> Operators { get; init; } = [];

    [JsonIgnore] public IReadOnlyDictionary<string, OperatorDefinition> ByToken { get; init; } = new Dictionary<string, OperatorDefinition>();
}

public sealed class OperatorDefinition
{
    public string Token { get; init; } = "";
    public string Name { get; init; } = "";
    public string Syntax { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Example { get; init; }
    public OperatorSupport Support { get; init; } = OperatorSupport.Unknown;
    public string Category { get; init; } = "";
    public bool TakesValue { get; init; }
    public bool AllowMultiple { get; init; }
    public bool Generate { get; init; }
    public IReadOnlyList<string> Caveats { get; init; } = [];
    public IReadOnlyList<string> SourceUrls { get; init; } = [];

    [JsonIgnore] public bool IsReliable => Support is OperatorSupport.Official or OperatorSupport.Working;
    [JsonIgnore] public bool IsPrefixOperator => Token.EndsWith(':') || Token.EndsWith('(');
}

// ---------- filetypes.json ----------

public sealed class FileTypeCatalog
{
    public int SchemaVersion { get; init; }
    public string CatalogVersion { get; init; } = "";
    public IReadOnlyList<FileTypeGroup> Groups { get; init; } = [];
    public IReadOnlyList<FileTypeDefinition> Extensions { get; init; } = [];

    [JsonIgnore] public IReadOnlyDictionary<string, FileTypeDefinition> ByExtension { get; init; } = new Dictionary<string, FileTypeDefinition>();
}

public sealed class FileTypeGroup
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public IReadOnlyList<string> Extensions { get; init; } = [];
}

public sealed class FileTypeDefinition
{
    public string Ext { get; init; } = "";
    public string Label { get; init; } = "";
    public string Group { get; init; } = "";
    public int Weight { get; init; }
    public bool Defensive { get; init; }
}

// ---------- platforms.json ----------

public sealed class PlatformCatalog
{
    public int SchemaVersion { get; init; }
    public string CatalogVersion { get; init; } = "";
    public IReadOnlyList<string> Notes { get; init; } = [];
    public IReadOnlyList<PlatformCategory> Categories { get; init; } = [];
    public IReadOnlyList<Platform> Platforms { get; init; } = [];

    [JsonIgnore] public IReadOnlyDictionary<string, Platform> ById { get; init; } = new Dictionary<string, Platform>();
}

public sealed class PlatformCategory
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class Platform
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string ProfileUrlTemplate { get; init; } = "";
    public string SearchDomain { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool CaseSensitive { get; init; }
    public int Weight { get; init; }
    public string? Caveat { get; init; }
    public UsernameRule? UsernameRule { get; init; }
}

public sealed class UsernameRule
{
    public string Pattern { get; init; } = "";
    public string Note { get; init; } = "";
}

// ---------- intents.json ----------

public sealed class IntentCatalog
{
    public int SchemaVersion { get; init; }
    public string CatalogVersion { get; init; } = "";
    public IReadOnlyList<string> Notes { get; init; } = [];
    public IReadOnlyList<VariantFamily> Families { get; init; } = [];
    public IReadOnlyList<IntentGroup> Groups { get; init; } = [];
    public IReadOnlyList<Intent> Intents { get; init; } = [];

    [JsonIgnore] public IReadOnlyDictionary<string, Intent> ById { get; init; } = new Dictionary<string, Intent>();
    [JsonIgnore] public IReadOnlyDictionary<string, VariantFamily> FamilyById { get; init; } = new Dictionary<string, VariantFamily>();
}

public sealed class VariantFamily
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class IntentGroup
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class Intent
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Group { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>"standard" or "defensive-exposure".</summary>
    public string Safety { get; init; } = "standard";
    public IReadOnlyList<string> CompatibleInputTypes { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>Option names (site, dates, organization, location, role, displayName) that must be supplied for this intent.</summary>
    public IReadOnlyList<string> RequiresOptions { get; init; } = [];
    /// <summary>Per input type option requirements, e.g. site-scoped needs a site for keyword input but not for domain input.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RequiresOptionsForInputTypes { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyList<string> DefaultFileTypes { get; init; } = [];
    public IReadOnlyList<QueryTemplate> Templates { get; init; } = [];

    [JsonIgnore] public bool IsDefensive => string.Equals(Safety, "defensive-exposure", StringComparison.OrdinalIgnoreCase);
}

public sealed class QueryTemplate
{
    public string Id { get; init; } = "";
    public string Family { get; init; } = "balanced";
    /// <summary>Input type ids or "*".</summary>
    public IReadOnlyList<string> When { get; init; } = ["*"];
    public string Pattern { get; init; } = "";
    public int Weight { get; init; } = 50;
    public string Explanation { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>Static extra exclusions appended through {exclusions}.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];
    /// <summary>Static OR-group of terms exposed through {keywords}.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Overrides the intent's default file types for {filetypeGroup}.</summary>
    public IReadOnlyList<string>? FileTypes { get; init; }
    public IReadOnlyList<string> PlatformIds { get; init; } = [];
    public IReadOnlyList<string> PlatformCategories { get; init; } = [];
    public int PlatformLimit { get; init; } = 6;
    /// <summary>Placeholders that must resolve even if optional by default.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];
    /// <summary>Placeholders that may resolve empty even if required by default.</summary>
    public IReadOnlyList<string> Optional { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool AppliesTo(string inputTypeId) =>
        When.Any(w => w == "*" || string.Equals(w, inputTypeId, StringComparison.OrdinalIgnoreCase));
}
