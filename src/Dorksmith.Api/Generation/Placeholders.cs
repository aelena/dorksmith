namespace Dorksmith.Api.Generation;

/// <summary>
/// Names of every placeholder a template pattern may use. The catalog validator rejects unknown names
/// so that a typo in intents.json fails readiness instead of silently producing broken queries.
/// </summary>
public static class Placeholders
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        // input words
        "terms", "termsAny", "quoted", "quotedReversed", "quotedInitialLast", "phraseWildcard",
        "intitle", "intitleAny", "inurl", "inurlAny", "intext", "intextAny",
        // domain-derived
        "domain", "domainQuoted", "domainLabel", "domainLabelBare", "site", "siteOption", "siteWildcard",
        "excludeSite", "excludeWww", "atDomainQuoted",
        // username
        "username", "quotedUsername", "atUsername", "inurlUsername", "intitleUsername", "hashtagUsername",
        // email
        "emailQuoted", "emailUserQuoted",
        // url
        "url", "urlQuoted", "urlBareQuoted", "inurlPath",
        // filename
        "filename", "filenameStem", "filetypeFromFilename",
        // options / template data
        "filetypeGroup", "filetypeFirst", "exclusions", "dates", "after", "before",
        "keywords", "keywordsAnd", "platformSites",
        "organization", "location", "role", "displayName", "context",
    };

    /// <summary>Placeholders that may resolve to nothing without disqualifying the template.</summary>
    public static readonly IReadOnlySet<string> OptionalByDefault = new HashSet<string>(StringComparer.Ordinal)
    {
        "exclusions", "dates", "after", "before", "site", "siteOption", "context",
    };

    /// <summary>Extracts {placeholder} names from a pattern in order of appearance.</summary>
    public static IEnumerable<string> In(string pattern)
    {
        var i = 0;
        while ((i = pattern.IndexOf('{', i)) >= 0)
        {
            var j = pattern.IndexOf('}', i + 1);
            if (j < 0) yield break;
            yield return pattern[(i + 1)..j];
            i = j + 1;
        }
    }
}
