using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Contracts;
using static Dorksmith.Api.Generation.QueryQuoting;

namespace Dorksmith.Api.Generation;

/// <summary>
/// Resolves a template placeholder to operator syntax, or to null when the input/options cannot supply it.
/// Every composite placeholder ({site}, {filetypeGroup}, {exclusions}, ...) yields complete, valid syntax
/// so templates never leave a dangling operator behind.
/// </summary>
public sealed class PlaceholderResolver(ICatalogProvider catalogs)
{
    public string? Resolve(string name, GenerationContext c, QueryTemplate t)
    {
        var phrase = string.Join(' ', c.Words);
        return name switch
        {
            // ----- words -----
            "terms" => c.InputType switch
            {
                InputType.Domain => c.Domain,
                InputType.Username => c.Username,
                InputType.Email => Quote(c.Email),
                InputType.Url => Quote(QueryNormalizer.BareUrl(c.Url!)),
                _ => SafeTerms(c.Words),
            },
            "termsAny" => IsWordy(c) && c.Words.Count >= 2 ? OrGroup(c.Words.Select(SafeTerm)) : null,
            "quoted" => c.InputType == InputType.Url ? Quote(c.Url!.ToString()) : Quote(phrase),
            "quotedReversed" => IsWordy(c) && c.Words.Count >= 2 ? Quote(c.Words[^1] + " " + string.Join(' ', c.Words.Take(c.Words.Count - 1))) : null,
            "quotedInitialLast" => IsWordy(c) && c.Words.Count >= 2 && char.IsLetter(c.Words[0][0]) ? Quote(char.ToUpperInvariant(c.Words[0][0]) + ". " + c.Words[^1]) : null,
            "phraseWildcard" => IsWordy(c) && c.Words.Count >= 2 ? Quote(c.Words[0] + " * " + c.Words[^1]) : null,
            "intitle" => Prefix("intitle:", OperatorValue(phrase)),
            "intitleAny" => c.Words.Count >= 2 ? OrGroup(c.Words.Select(w => Prefix("intitle:", OperatorValue(w)))) : null,
            "inurl" => c.InputType == InputType.Url ? Resolve("inurlPath", c, t) : Prefix("inurl:", OperatorValue(phrase)),
            "inurlAny" => c.Words.Count >= 2 ? OrGroup(c.Words.Select(w => Prefix("inurl:", OperatorValue(w)))) : null,
            "intext" => Prefix("intext:", c.InputType switch { InputType.Url => Quote(c.Url!.ToString()), InputType.Email => Quote(c.Email), _ => OperatorValue(phrase) }),
            "intextAny" => c.Words.Count >= 2 ? OrGroup(c.Words.Select(w => Prefix("intext:", OperatorValue(w)))) : null,

            // ----- domain -----
            "domain" => c.Domain,
            "domainQuoted" => Quote(c.Domain),
            "domainLabel" => c.Domain is null ? null : Quote(QueryNormalizer.DomainLabel(c.Domain)),
            "domainLabelBare" => c.Domain is null ? null : QueryNormalizer.DomainLabel(c.Domain),
            "site" => c.Domain is not null ? "site:" + c.Domain : c.Site is not null ? "site:" + c.Site : null,
            "siteOption" => c.Site is not null ? "site:" + c.Site : null,
            "siteWildcard" => c.Domain is null ? null : "site:*." + c.Domain,
            "excludeSite" => c.Domain is null ? null : "-site:" + c.Domain,
            "excludeWww" => c.Domain is null ? null : "-site:www." + c.Domain,
            "atDomainQuoted" => c.Domain is null ? null : Quote("@" + c.Domain),

            // ----- username -----
            "username" => c.Username,
            "quotedUsername" => Quote(c.Username),
            "atUsername" => c.Username is null ? null : Quote("@" + c.Username),
            "inurlUsername" => Prefix("inurl:", OperatorValue(c.Username)),
            "intitleUsername" => Prefix("intitle:", OperatorValue(c.Username)),
            "hashtagUsername" => c.Username is not null && c.Username.All(ch => char.IsLetterOrDigit(ch) || ch == '_') ? "#" + c.Username : null,

            // ----- email -----
            "emailQuoted" => Quote(c.Email),
            "emailUserQuoted" => Quote(c.EmailUser),

            // ----- url -----
            "url" => c.Url?.ToString(),
            "urlQuoted" => c.Url is null ? null : Quote(c.Url.ToString()),
            "urlBareQuoted" => c.Url is null ? null : Quote(QueryNormalizer.BareUrl(c.Url)),
            "inurlPath" => c.Url is { AbsolutePath.Length: > 1 } u ? "inurl:" + Quote(u.AbsolutePath.Trim('/')) : null,

            // ----- filename -----
            "filename" => c.FilenameStem is null ? null : Quote(c.FilenameStem + "." + c.FilenameExt),
            "filenameStem" => Quote(c.FilenameStem),
            "filetypeFromFilename" => c.FilenameExt is null ? null : "filetype:" + c.FilenameExt,

            // ----- options & template data -----
            "filetypeGroup" => OrGroup(FileTypes(c, t).Select(x => "filetype:" + x)),
            "filetypeFirst" => FileTypes(c, t).FirstOrDefault() is { } first ? "filetype:" + first : null,
            "exclusions" => string.Join(' ', c.ExcludeTerms.Concat(t.Exclude).Select(x => Exclusion(x, IsGeneratable(c))).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)),
            "dates" => string.Join(' ', new[] { Resolve("after", c, t), Resolve("before", c, t) }.Where(x => x is not null)),
            "after" => c.After is { } a ? "after:" + a.ToString("yyyy-MM-dd") : null,
            "before" => c.Before is { } b ? "before:" + b.ToString("yyyy-MM-dd") : null,
            "keywords" => OrGroup(t.Keywords.Select(k => k.Contains(' ') ? Quote(k) : SafeTerm(k))),
            "keywordsAnd" => string.Join(' ', t.Keywords.Select(k => k.Contains(' ') ? Quote(k) : SafeTerm(k))),
            "platformSites" => OrGroup(Platforms(t).Select(p => "site:" + p.SearchDomain)),
            "organization" => Quote(c.Organization),
            "location" => Quote(c.Location),
            "role" => Quote(c.Role),
            "displayName" => Quote(c.DisplayName),
            "context" => string.Join(' ', new[] { c.Organization, c.Location, c.Role, c.DisplayName }.Where(x => x is not null).Select(Quote)),

            _ => throw new InvalidOperationException($"Unknown placeholder {{{name}}}"),
        } is { Length: > 0 } value ? value : null;
    }

    private static bool IsWordy(GenerationContext c) => c.InputType is InputType.Keyword or InputType.Person or InputType.Organization or InputType.Technology;

    private static string? Prefix(string op, string value) => value.Length == 0 ? null : op + value;

    private static Func<string, bool> IsGeneratable(GenerationContext c) => token => c.Operators.ByToken.TryGetValue(token, out var op) && op.Generate;

    private static IReadOnlyList<string> FileTypes(GenerationContext c, QueryTemplate t)
        => c.FileTypes.Count > 0 ? c.FileTypes : t.FileTypes ?? c.Intent.DefaultFileTypes;

    private IEnumerable<Platform> Platforms(QueryTemplate t)
    {
        var all = catalogs.Platforms;
        if (t.PlatformIds.Count > 0)
            return t.PlatformIds.Select(id => all.ById.TryGetValue(id, out var p) ? p : null).Where(p => p is { Enabled: true })!;
        var cats = t.PlatformCategories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Platforms
            .Where(p => p.Enabled && cats.Contains(p.Category))
            .OrderByDescending(p => p.Weight).ThenBy(p => p.Id, StringComparer.Ordinal)
            .Take(Math.Max(1, t.PlatformLimit));
    }
}
