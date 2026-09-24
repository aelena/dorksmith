using System.Text.RegularExpressions;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Generation;

namespace Dorksmith.Api.Catalogs;

/// <summary>Structural and cross-reference validation of the catalogs. Any error fails readiness.</summary>
public static class CatalogValidator
{
    private static readonly Regex ExtensionPattern = new("^[a-z0-9]{1,12}$", RegexOptions.Compiled);
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<OperatorCatalog> operatorCatalogs,
        IntentCatalog intents,
        PlatformCatalog platforms,
        FileTypeCatalog fileTypes)
    {
        var errors = new List<string>();

        foreach (var ops in operatorCatalogs) ValidateOperators(ops, errors);
        if (operatorCatalogs.Count == 0) errors.Add("operators: no operators.<engine>.json catalog found");

        ValidateFileTypes(fileTypes, errors);
        ValidatePlatforms(platforms, errors);
        ValidateIntents(intents, operatorCatalogs, platforms, fileTypes, errors);

        return errors;
    }

    private static void ValidateOperators(OperatorCatalog c, List<string> errors)
    {
        var p = $"operators.{c.Engine}";
        if (c.SchemaVersion != 1) errors.Add($"{p}: unsupported schemaVersion {c.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(c.Engine)) errors.Add($"{p}: engine is required");
        if (string.IsNullOrWhiteSpace(c.CatalogVersion)) errors.Add($"{p}: catalogVersion is required");
        if (!c.SearchUrlTemplate.Contains("{query}")) errors.Add($"{p}: searchUrlTemplate must contain {{query}}");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in c.Operators)
        {
            var q = $"{p}[{op.Token}]";
            if (string.IsNullOrEmpty(op.Token)) { errors.Add($"{p}: operator with empty token"); continue; }
            if (!seen.Add(op.Token)) errors.Add($"{q}: duplicate token");
            if (string.IsNullOrWhiteSpace(op.Name)) errors.Add($"{q}: name is required");
            if (string.IsNullOrWhiteSpace(op.Description)) errors.Add($"{q}: description is required");
            if (op.Support == OperatorSupport.Deprecated && op.Generate) errors.Add($"{q}: deprecated operators must have generate=false");
            if (op.Generate && !op.IsReliable) errors.Add($"{q}: generate=true requires support official or working");
            foreach (var u in op.SourceUrls)
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    errors.Add($"{q}: invalid sourceUrl '{u}'");
        }
    }

    private static void ValidateFileTypes(FileTypeCatalog c, List<string> errors)
    {
        const string p = "filetypes";
        if (c.SchemaVersion != 1) errors.Add($"{p}: unsupported schemaVersion {c.SchemaVersion}");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in c.Extensions)
        {
            if (!ExtensionPattern.IsMatch(e.Ext)) errors.Add($"{p}: invalid extension '{e.Ext}' (lowercase alphanumerics only)");
            if (!seen.Add(e.Ext)) errors.Add($"{p}: duplicate extension '{e.Ext}'");
            if (c.Groups.All(g => g.Id != e.Group)) errors.Add($"{p}[{e.Ext}]: unknown group '{e.Group}'");
        }
        foreach (var g in c.Groups)
        foreach (var ext in g.Extensions)
            if (!seen.Contains(ext)) errors.Add($"{p}.groups[{g.Id}]: extension '{ext}' not defined");
    }

    private static void ValidatePlatforms(PlatformCatalog c, List<string> errors)
    {
        const string p = "platforms";
        if (c.SchemaVersion != 1) errors.Add($"{p}: unsupported schemaVersion {c.SchemaVersion}");
        var categories = c.Categories.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pl in c.Platforms)
        {
            var q = $"{p}[{pl.Id}]";
            if (!IdPattern.IsMatch(pl.Id)) errors.Add($"{q}: invalid id");
            if (!seen.Add(pl.Id)) errors.Add($"{q}: duplicate id");
            if (string.IsNullOrWhiteSpace(pl.Name)) errors.Add($"{q}: name is required");
            if (!categories.Contains(pl.Category)) errors.Add($"{q}: unknown category '{pl.Category}'");
            if (!pl.ProfileUrlTemplate.StartsWith("https://", StringComparison.Ordinal) || !pl.ProfileUrlTemplate.Contains("{username}"))
                errors.Add($"{q}: profileUrlTemplate must be https and contain {{username}}");
            if (string.IsNullOrWhiteSpace(pl.SearchDomain) || pl.SearchDomain.Contains("://"))
                errors.Add($"{q}: searchDomain must be a bare host name");
            if (pl.UsernameRule is { } rule)
            {
                try { _ = new Regex(rule.Pattern); }
                catch (ArgumentException) { errors.Add($"{q}: usernameRule.pattern is not a valid regex"); }
            }
        }
    }

    private static void ValidateIntents(IntentCatalog c, IReadOnlyList<OperatorCatalog> operatorCatalogs, PlatformCatalog platforms, FileTypeCatalog fileTypes, List<string> errors)
    {
        const string p = "intents";
        if (c.SchemaVersion != 1) errors.Add($"{p}: unsupported schemaVersion {c.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(c.CatalogVersion)) errors.Add($"{p}: catalogVersion is required");

        var families = c.Families.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var groups = c.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var platformCategories = platforms.Categories.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var intentIds = new HashSet<string>(StringComparer.Ordinal);
        var templateIds = new HashSet<string>(StringComparer.Ordinal);
        var blockedTokens = operatorCatalogs
            .SelectMany(o => o.Operators.Where(op => !op.Generate && op.IsPrefixOperator).Select(op => op.Token.ToLowerInvariant()))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var intent in c.Intents)
        {
            var q = $"{p}[{intent.Id}]";
            if (!IdPattern.IsMatch(intent.Id)) errors.Add($"{q}: invalid id");
            if (!intentIds.Add(intent.Id)) errors.Add($"{q}: duplicate id");
            if (string.IsNullOrWhiteSpace(intent.Label)) errors.Add($"{q}: label is required");
            if (!groups.Contains(intent.Group)) errors.Add($"{q}: unknown group '{intent.Group}'");
            if (intent.Safety is not ("standard" or "defensive-exposure")) errors.Add($"{q}: safety must be 'standard' or 'defensive-exposure'");
            if (intent.CompatibleInputTypes.Count == 0) errors.Add($"{q}: compatibleInputTypes is required");
            foreach (var t in intent.CompatibleInputTypes)
                if (!InputTypes.TryParse(t, out _)) errors.Add($"{q}: unknown input type '{t}'");
            foreach (var ext in intent.DefaultFileTypes)
                if (!fileTypes.ByExtension.ContainsKey(ext)) errors.Add($"{q}: defaultFileTypes references unknown extension '{ext}'");
            foreach (var (type, _) in intent.RequiresOptionsForInputTypes)
                if (!intent.CompatibleInputTypes.Contains(type)) errors.Add($"{q}: requiresOptionsForInputTypes references incompatible type '{type}'");
            if (intent.Templates.Count == 0) errors.Add($"{q}: at least one template is required");

            foreach (var t in intent.Templates)
            {
                var r = $"{q}.templates[{t.Id}]";
                if (!IdPattern.IsMatch(t.Id)) errors.Add($"{r}: invalid id");
                if (!templateIds.Add(t.Id)) errors.Add($"{r}: duplicate template id (ids are global)");
                if (!families.Contains(t.Family)) errors.Add($"{r}: unknown family '{t.Family}'");
                if (string.IsNullOrWhiteSpace(t.Pattern)) errors.Add($"{r}: pattern is required");
                if (string.IsNullOrWhiteSpace(t.Explanation)) errors.Add($"{r}: explanation is required");
                if (t.Weight is < 0 or > 1000) errors.Add($"{r}: weight must be 0..1000");

                foreach (var w in t.When)
                    if (w != "*" && !intent.CompatibleInputTypes.Contains(w, StringComparer.OrdinalIgnoreCase))
                        errors.Add($"{r}: 'when' contains '{w}' which is not in compatibleInputTypes");

                var names = Placeholders.In(t.Pattern).ToList();
                if (names.Count == 0) errors.Add($"{r}: pattern has no placeholders");
                foreach (var n in names.Concat(t.Requires).Concat(t.Optional))
                    if (!Placeholders.All.Contains(n)) errors.Add($"{r}: unknown placeholder '{{{n}}}'");
                if (names.Contains("keywords") || names.Contains("keywordsAnd"))
                    if (t.Keywords.Count == 0) errors.Add($"{r}: {{keywords}} used without a keywords list");
                if (names.Contains("platformSites") && t.PlatformIds.Count == 0 && t.PlatformCategories.Count == 0)
                    errors.Add($"{r}: {{platformSites}} used without platformIds or platformCategories");
                foreach (var id in t.PlatformIds)
                    if (!platforms.ById.ContainsKey(id)) errors.Add($"{r}: unknown platformId '{id}'");
                foreach (var cat in t.PlatformCategories)
                    if (!platformCategories.Contains(cat)) errors.Add($"{r}: unknown platform category '{cat}'");
                foreach (var ext in t.FileTypes ?? [])
                    if (!fileTypes.ByExtension.ContainsKey(ext)) errors.Add($"{r}: fileTypes references unknown extension '{ext}'");

                foreach (var token in t.Pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var bare = token.TrimStart('(', '-').ToLowerInvariant();
                    var colon = bare.IndexOf(':');
                    if (colon > 0 && blockedTokens.Contains(bare[..(colon + 1)]))
                        errors.Add($"{r}: pattern emits non-generatable operator '{bare[..(colon + 1)]}'");
                }
            }
        }
    }
}
