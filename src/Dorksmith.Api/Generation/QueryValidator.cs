using Dorksmith.Api.Catalogs;

namespace Dorksmith.Api.Generation;

public sealed record QueryAnalysis(IReadOnlyList<string> Operators, IReadOnlyList<string> Warnings, bool HasErrors);

/// <summary>
/// Quote-aware scanner that lists the operators a query uses (in order of first appearance) and produces
/// plain-language warnings: unknown/unreliable/deprecated operators, unbalanced quotes or parentheses,
/// lower-case or, dotted filetype values, schemes inside site:, and Google's 32-word limit.
/// </summary>
public static class QueryValidator
{
    public const int GoogleWordLimit = 32;

    public static QueryAnalysis Analyze(string query, OperatorCatalog catalog)
    {
        var operators = new List<string>();
        var warnings = new List<string>();
        var hasErrors = false;
        void Use(string token) { if (!operators.Contains(token, StringComparer.Ordinal)) operators.Add(token); }

        var depth = 0;
        var tokens = Tokenize(query);
        var quoteCount = query.Count(c => c == '"');
        if (quoteCount > 0) Use("\"");
        if (quoteCount % 2 == 1) { warnings.Add("Unbalanced double quotes: the last phrase will not be treated as exact."); hasErrors = true; }

        foreach (var raw in tokens)
        {
            if (raw.Quoted)
            {
                if (raw.Text.Contains('*')) Use("*");
                continue;
            }
            var t = raw.Text;
            if (t.Length == 0) continue;

            if (t == "OR") { Use("OR"); continue; }
            if (t == "AND") { Use("AND"); continue; }
            if (t == "|") { Use("|"); continue; }
            if (t.Equals("or", StringComparison.Ordinal) || t.Equals("and", StringComparison.Ordinal))
            {
                warnings.Add($"Lower-case '{t}' is treated as an ordinary word; use upper-case {t.ToUpperInvariant()} for a boolean operator.");
                continue;
            }

            var body = t;
            while (body.Length > 0 && body[0] == '(') { depth++; Use("("); body = body[1..]; }
            var closing = 0;
            while (body.Length > 0 && body[^1] == ')') { closing++; body = body[..^1]; }
            depth -= closing;

            if (body.StartsWith("AROUND(", StringComparison.Ordinal)) { Use("AROUND("); continue; }
            if (body.Length > 1 && body[0] == '-') { Use("-"); body = body[1..]; }
            else if (body.Length > 1 && body[0] == '+') { Use("+"); body = body[1..]; }
            else if (body.Length > 1 && body[0] == '~') { Use("~"); body = body[1..]; }
            if (body.Length > 1 && body[0] == '#') { Use("#"); continue; }
            if (body.Length > 1 && body[0] == '@') { Use("@"); continue; }
            if (body == "*") { Use("*"); continue; }
            if (body.Contains("..") && body.Any(char.IsDigit)) { Use(".."); continue; }
            if (body.Length > 1 && body[0] == '$' && body.Skip(1).All(c => char.IsDigit(c) || c == '.' || c == ',')) { Use("$"); continue; }

            var colon = body.IndexOf(':');
            if (colon > 0 && body[..colon].All(char.IsLetter))
            {
                var prefix = body[..(colon + 1)].ToLowerInvariant();
                var value = body[(colon + 1)..];
                if (catalog.ByToken.TryGetValue(prefix, out var op))
                {
                    Use(prefix);
                    if (prefix == "filetype:" && value.StartsWith('.')) warnings.Add("filetype: values take no leading dot (filetype:pdf).");
                    if (prefix == "site:" && value.Contains("://")) warnings.Add("site: values take no scheme (site:example.com).");
                    if (prefix is "before:" or "after:" && !(value.Length is 4 or 10)) warnings.Add($"{prefix} expects YYYY-MM-DD or YYYY.");
                    if (op.TakesValue && value.Length == 0) { warnings.Add($"{prefix} has no value."); hasErrors = true; }
                }
                else if (body[..colon].Length <= 15 && !body.Contains("://"))
                {
                    warnings.Add($"'{prefix}' is not a known {catalog.EngineName} operator and will be searched as plain text.");
                }
            }
        }

        if (depth != 0) { warnings.Add("Unbalanced parentheses."); hasErrors = true; }

        foreach (var token in operators)
        {
            if (!catalog.ByToken.TryGetValue(token, out var op)) continue;
            switch (op.Support)
            {
                case OperatorSupport.Deprecated:
                    warnings.Add($"{op.Token} is deprecated: {op.Caveats.FirstOrDefault() ?? "it no longer works."}");
                    break;
                case OperatorSupport.Unreliable:
                    warnings.Add($"{op.Token} is unreliable: {op.Caveats.FirstOrDefault() ?? "results are inconsistent."}");
                    break;
                case OperatorSupport.Unknown:
                    warnings.Add($"{op.Token} has unknown support status.");
                    break;
            }
        }

        var wordCount = tokens.Count(t => t.Text.Length > 0);
        if (wordCount > GoogleWordLimit) warnings.Add($"Query has {wordCount} terms; Google ignores everything after the first {GoogleWordLimit}.");

        return new QueryAnalysis(operators, warnings, hasErrors);
    }

    private readonly record struct Token(string Text, bool Quoted);

    private static List<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < query.Length)
        {
            if (char.IsWhiteSpace(query[i])) { i++; continue; }
            if (query[i] == '"')
            {
                var end = query.IndexOf('"', i + 1);
                if (end < 0) end = query.Length - 1;
                tokens.Add(new Token(query[(i + 1)..Math.Max(i + 1, end)], true));
                i = end + 1;
                continue;
            }
            var start = i;
            while (i < query.Length && !char.IsWhiteSpace(query[i]))
            {
                if (query[i] == '"')
                {
                    // operator with quoted value: intitle:"a b"
                    var end = query.IndexOf('"', i + 1);
                    i = end < 0 ? query.Length : end + 1;
                    continue;
                }
                i++;
            }
            tokens.Add(new Token(query[start..i], false));
        }
        return tokens;
    }
}
