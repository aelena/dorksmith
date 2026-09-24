using System.Text.RegularExpressions;
using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Generation;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Handles;

public interface IHandleExpander
{
    HandleExpandResponse Expand(HandleExpandRequest request);
}

/// <summary>
/// Expands a handle into public profile URLs and search queries from the platform catalog.
/// No network access, no probing: every profile is reported as "not-checked".
/// </summary>
public sealed partial class HandleExpander(ICatalogProvider catalogs, IDorkGenerator generator, IOptions<UsernameSearchOptions> options) : IHandleExpander
{
    public const string NotChecked = "not-checked";
    public const string Notice = "URLs are constructed from templates and are NOT verified. A URL that resolves does not prove the account belongs to the person you are researching.";

    [GeneratedRegex(@"^[A-Za-z0-9-]+$")] private static partial Regex HostLabel();
    private static readonly Regex ExpandedPattern = new(@"\{username\}", RegexOptions.Compiled);

    public HandleExpandResponse Expand(HandleExpandRequest request)
    {
        var o = options.Value;
        if (!QueryNormalizer.TryNormalizeUsername(request.Username, o.MaxUsernameLength, out var username))
            throw new InputValidationException($"A username without whitespace (max {o.MaxUsernameLength} chars) is required.", "username");

        var categories = catalogs.Platforms.Categories.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = (request.Categories ?? []).Select(c => (c ?? "").Trim()).Where(c => c.Length > 0).ToList();
        foreach (var c in wanted)
            if (!categories.Contains(c)) throw new InputValidationException($"Unknown platform category '{c}'. Known: {string.Join(", ", categories.Order())}.", "categories");

        var ids = (request.PlatformIds ?? []).Select(p => (p ?? "").Trim()).Where(p => p.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            if (!catalogs.Platforms.ById.ContainsKey(id)) throw new InputValidationException($"Unknown platform '{id}'.", "platformIds");

        var max = request.MaxPlatforms ?? o.DefaultPlatforms;
        if (max < 1 || max > o.MaxPlatforms)
            throw new InputValidationException($"maxPlatforms must be between 1 and {o.MaxPlatforms}.", "maxPlatforms");

        var platforms = catalogs.Platforms.Platforms
            .Where(p => p.Enabled)
            .Where(p => wanted.Count == 0 || wanted.Contains(p.Category, StringComparer.OrdinalIgnoreCase))
            .Where(p => ids.Count == 0 || ids.Contains(p.Id))
            .OrderByDescending(p => p.Weight).ThenBy(p => p.Id, StringComparer.Ordinal)
            .Take(max)
            .ToList();

        var profiles = platforms.Select(p => Build(p, username)).ToList();

        var warnings = new List<string>();
        if (profiles.Count == 0) warnings.Add("No enabled platforms matched the requested filters.");
        if (username.Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '.' or '-')))
            warnings.Add("The handle contains characters many platforms do not allow; expect several URLs to be invalid.");

        var queries = GeneralQueries(username);

        return new HandleExpandResponse(
            Username: QueryNormalizer.NormalizeText(request.Username),
            NormalizedUsername: username,
            Notice: Notice,
            Profiles: profiles,
            Queries: queries,
            Warnings: warnings,
            CatalogVersion: catalogs.CatalogVersion,
            RateLimit: null);
    }

    private static ProfileCandidate Build(Platform p, string username)
    {
        var value = p.CaseSensitive ? username : username.ToLowerInvariant();
        var caveats = new List<string>();
        if (p.Caveat is { Length: > 0 }) caveats.Add(p.Caveat);

        string? url;
        var schemeEnd = p.ProfileUrlTemplate.IndexOf("://", StringComparison.Ordinal) + 3;
        var hostEnd = p.ProfileUrlTemplate.IndexOf('/', schemeEnd);
        var inHost = p.ProfileUrlTemplate.IndexOf("{username}", StringComparison.Ordinal) < (hostEnd < 0 ? p.ProfileUrlTemplate.Length : hostEnd);
        if (inHost && !HostLabel().IsMatch(value))
        {
            url = null;
            caveats.Add("The handle is used as a sub-domain on this platform and contains characters not valid in a host name.");
        }
        else
        {
            url = ExpandedPattern.Replace(p.ProfileUrlTemplate, inHost ? value.ToLowerInvariant() : Uri.EscapeDataString(value));
        }

        if (p.UsernameRule is { } rule && !Regex.IsMatch(username, rule.Pattern))
            caveats.Add(rule.Note.Length > 0 ? rule.Note : "The handle does not match this platform's username rules.");

        var query = $"site:{p.SearchDomain} {QueryQuoting.Quote(username)}";
        return new ProfileCandidate(p.Id, p.Name, p.Category, url, query, NotChecked, caveats.Count == 0 ? null : string.Join(" ", caveats));
    }

    private IReadOnlyList<HandleQuery> GeneralQueries(string username)
    {
        try
        {
            var result = generator.Generate(new GenerateRequest(username, "username", "username-exact", "google", new GenerateOptions { MaxVariants = 5 }));
            return result.Variants.Select(v => new HandleQuery(v.Id, v.Label, v.Query, v.Explanation)).ToList();
        }
        catch (InputValidationException)
        {
            return [];
        }
    }
}
