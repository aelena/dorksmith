using System.Text;
using System.Text.RegularExpressions;

namespace Dorksmith.Api.Generation;

/// <summary>Deterministic, non-destructive normalisation of user input. Never lower-cases search terms.</summary>
public static partial class QueryNormalizer
{
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*://")] private static partial Regex Scheme();
    [GeneratedRegex(@"^(?:\*\.)?(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})$")] private static partial Regex Hostname();
    [GeneratedRegex(@"^(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}$")] private static partial Regex IPv4();
    [GeneratedRegex(@"^[^\s@""'<>()\[\],;:\\]+@((?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63})$", RegexOptions.IgnoreCase)] private static partial Regex Email();
    [GeneratedRegex(@"^(?<stem>.+)\.(?<ext>[A-Za-z0-9]{1,12})$")] private static partial Regex Filename();

    /// <summary>Trims, collapses whitespace, maps Unicode quotes to ASCII and drops control characters.</summary>
    public static string NormalizeText(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case '“' or '”' or '„' or '‟' or '«' or '»' or '″' or '〃': sb.Append('"'); break;
                case '‘' or '’' or '‚' or '‛' or '′': sb.Append('\''); break;
                case ' ' or ' ' or ' ' or '　': sb.Append(' '); break;
                default:
                    if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r') break;
                    sb.Append(ch);
                    break;
            }
        }
        return Whitespace().Replace(sb.ToString(), " ").Trim();
    }

    public static bool ContainsControlCharacters(string s) => s.Any(c => char.IsControl(c));

    /// <summary>Splits normalised text into words, unwrapping a fully quoted phrase ("a b" → a b).</summary>
    public static (IReadOnlyList<string> Words, bool WasQuoted) Words(string normalized)
    {
        var s = normalized;
        var quoted = s.Length >= 2 && s[0] == '"' && s[^1] == '"' && s.IndexOf('"', 1) == s.Length - 1;
        if (quoted) s = s[1..^1].Trim();
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (words, quoted);
    }

    /// <summary>Accepts host names with optional scheme/path/port, returns the bare lower-case host without a leading www.</summary>
    public static bool TryNormalizeDomain(string? input, out string domain)
    {
        domain = "";
        var s = NormalizeText(input);
        if (s.Length == 0 || s.Contains(' ')) return false;
        s = Scheme().Replace(s, "");
        var cut = s.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) s = s[..cut];
        var at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        var colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];
        s = s.TrimEnd('.').ToLowerInvariant();
        if (s.StartsWith("*.", StringComparison.Ordinal)) s = s[2..];
        if (s.StartsWith("www.", StringComparison.Ordinal) && s.Count(c => c == '.') >= 2) s = s[4..];
        if (s.Length == 0 || s.Length > 253) return false;
        if (!Hostname().IsMatch(s) && !IPv4().IsMatch(s)) return false;
        domain = s;
        return true;
    }

    /// <summary>The organisation-ish label of a domain: example for www.example.co.uk.</summary>
    public static string DomainLabel(string domain)
    {
        var labels = domain.Split('.');
        if (labels.Length < 2 || IPv4().IsMatch(domain)) return domain;
        var last = labels[^1];
        var secondLast = labels[^2];
        if (labels.Length >= 3 && last.Length <= 3 && secondLast.Length <= 3) return labels[^3];
        return secondLast;
    }

    /// <summary>http/https only. A missing scheme is assumed to be https. Never fetches.</summary>
    public static bool TryNormalizeUrl(string? input, out Uri url)
    {
        url = null!;
        var s = NormalizeText(input);
        if (s.Length == 0 || s.Contains(' ')) return false;
        if (!Scheme().IsMatch(s)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme is not ("http" or "https")) return false;
        if (parsed.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4)) return false;
        if (parsed.HostNameType == UriHostNameType.Dns && !Hostname().IsMatch(parsed.Host.ToLowerInvariant())) return false;

        var b = new UriBuilder(parsed) { Host = parsed.Host.ToLowerInvariant(), Fragment = "" };
        if (b.Uri.IsDefaultPort) b.Port = -1;
        if (b.Path.Length > 1) b.Path = b.Path.TrimEnd('/');
        url = b.Uri;
        return true;
    }

    /// <summary>URL without scheme and trailing slash, as pages usually cite it.</summary>
    public static string BareUrl(Uri url)
    {
        var host = url.IsDefaultPort ? url.Host : url.Host + ":" + url.Port;
        return (host + url.PathAndQuery).TrimEnd('/');
    }

    /// <summary>Strips leading @ characters; rejects whitespace and control characters.</summary>
    public static bool TryNormalizeUsername(string? input, int maxLength, out string username)
    {
        username = "";
        var s = NormalizeText(input).TrimStart('@').Trim();
        if (s.Length == 0 || s.Length > maxLength) return false;
        if (s.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '"')) return false;
        username = s;
        return true;
    }

    public static bool TryNormalizeEmail(string? input, out string email, out string localPart, out string domain)
    {
        email = localPart = domain = "";
        var s = NormalizeText(input).Trim('<', '>', '"', '\'');
        var m = Email().Match(s);
        if (!m.Success || s.Length > 254) return false;
        var at = s.LastIndexOf('@');
        localPart = s[..at];
        domain = s[(at + 1)..].ToLowerInvariant();
        email = localPart + "@" + domain;
        return true;
    }

    public static bool TryParseFilename(string? input, out string stem, out string extension)
    {
        stem = extension = "";
        var s = NormalizeText(input).Trim('"');
        var m = Filename().Match(s);
        if (!m.Success) return false;
        stem = m.Groups["stem"].Value.Trim();
        extension = m.Groups["ext"].Value.ToLowerInvariant();
        return stem.Length > 0 && !stem.Contains('"');
    }

    public static bool TryParseIsoDate(string? input, out DateOnly date)
    {
        date = default;
        var s = NormalizeText(input);
        return s.Length == 10 && DateOnly.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>Canonical form used for duplicate detection: collapsed whitespace, lower-cased operator prefixes, upper-cased OR.</summary>
    public static string Canonicalize(string query)
    {
        var sb = new StringBuilder(query.Length);
        var inQuote = false;
        var tokenStart = 0;
        var s = Whitespace().Replace(query, " ").Trim();
        for (var i = 0; i <= s.Length; i++)
        {
            var end = i == s.Length;
            var c = end ? ' ' : s[i];
            if (!end && c == '"') inQuote = !inQuote;
            if ((c == ' ' && !inQuote) || end)
            {
                var token = s[tokenStart..i];
                sb.Append(CanonicalToken(token));
                if (!end) sb.Append(' ');
                tokenStart = i + 1;
            }
        }
        return sb.ToString().Trim();
    }

    private static string CanonicalToken(string token)
    {
        if (token.Equals("or", StringComparison.OrdinalIgnoreCase)) return "OR";
        if (token.Equals("and", StringComparison.OrdinalIgnoreCase)) return "AND";
        var body = token.TrimStart('(', '-');
        var colon = body.IndexOf(':');
        if (colon > 0 && body[..colon].All(char.IsLetter))
        {
            var prefixLen = token.Length - body.Length;
            return token[..prefixLen] + body[..colon].ToLowerInvariant() + body[colon..];
        }
        return token;
    }
}
