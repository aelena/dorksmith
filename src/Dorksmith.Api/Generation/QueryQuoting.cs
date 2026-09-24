namespace Dorksmith.Api.Generation;

/// <summary>
/// Safe search-string quoting. Prevents user text from becoming application-generated syntax,
/// never double-quotes, strips nested quotes deterministically and preserves Unicode.
/// </summary>
public static class QueryQuoting
{
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal) { "OR", "AND", "|" };

    /// <summary>Wraps a phrase in ASCII double quotes. Existing surrounding quotes are reused, inner quotes removed.</summary>
    public static string Quote(string? phrase)
    {
        var s = (phrase ?? "").Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        s = s.Replace("\"", "").Trim();
        return s.Length == 0 ? "" : "\"" + s + "\"";
    }

    public static string Unquote(string? phrase)
    {
        var s = (phrase ?? "").Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return s.Replace("\"", "").Trim();
    }

    /// <summary>
    /// A single word emitted unquoted. Words that would be parsed as syntax (leading -, +, ~, an operator
    /// prefix like foo:, reserved OR/AND, parentheses) are quoted so they stay literal search terms.
    /// </summary>
    public static string SafeTerm(string? word)
    {
        var s = (word ?? "").Trim().Replace("\"", "");
        if (s.Length == 0) return "";
        if (LooksLikeSyntax(s)) return "\"" + (s.StartsWith("AROUND(", StringComparison.Ordinal) ? s : s.Trim('(', ')')) + "\"";
        return s;
    }

    public static string SafeTerms(IEnumerable<string> words) => string.Join(' ', words.Select(SafeTerm).Where(w => w.Length > 0));

    public static bool LooksLikeSyntax(string s)
    {
        if (ReservedWords.Contains(s)) return true;
        if (s[0] is '-' or '+' or '~' or '(' or ')' or '|') return true;
        if (s[^1] is '(' or ')') return true;
        var colon = s.IndexOf(':');
        if (colon > 0 && colon < s.Length - 1 && s[..colon].All(char.IsLetter)) return true;
        if (s.StartsWith("AROUND(", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Value for a prefix operator (intitle:, inurl:, intext:): bare for a single safe word, quoted otherwise.</summary>
    public static string OperatorValue(string? phrase)
    {
        var s = Unquote(phrase);
        if (s.Length == 0) return "";
        return s.Contains(' ') || LooksLikeSyntax(s) ? "\"" + s + "\"" : s;
    }

    /// <summary>(a OR b OR c); a single item is returned bare; none returns empty.</summary>
    public static string OrGroup(IEnumerable<string?> items)
    {
        var list = items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i!).Distinct(StringComparer.Ordinal).ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => "(" + string.Join(" OR ", list) + ")",
        };
    }

    /// <summary>-term for exclusions. Known operator exclusions (site:x) pass through; phrases get quoted.</summary>
    public static string Exclusion(string? term, Func<string, bool> isGeneratableOperator)
    {
        var s = (term ?? "").Trim().TrimStart('-').Replace("\"", "").Trim();
        if (s.Length == 0) return "";
        var colon = s.IndexOf(':');
        if (colon > 0 && colon < s.Length - 1 && !s.Contains(' ') && isGeneratableOperator(s[..(colon + 1)].ToLowerInvariant()))
            return "-" + s[..colon].ToLowerInvariant() + s[colon..];
        return s.Contains(' ') || LooksLikeSyntax(s) ? "-\"" + s + "\"" : "-" + s;
    }
}
