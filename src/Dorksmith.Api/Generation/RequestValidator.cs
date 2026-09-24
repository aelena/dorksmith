using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Contracts;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Generation;

/// <summary>Turns a raw <see cref="GenerateRequest"/> into a <see cref="GenerationContext"/> or throws <see cref="InputValidationException"/>.</summary>
public sealed class RequestValidator(ICatalogProvider catalogs, IOptions<GenerationOptions> generation, IOptions<UsernameSearchOptions> usernames)
{
    private const int MaxContextLength = 200;
    private const int MaxExcludeTermLength = 100;

    public GenerationContext Build(GenerateRequest request)
    {
        var g = generation.Value;
        var o = request.Options ?? new GenerateOptions();

        // --- engine / intent / type ---
        var engine = string.IsNullOrWhiteSpace(request.Engine) ? "google" : request.Engine.Trim().ToLowerInvariant();
        if (!g.Engines.Contains(engine, StringComparer.OrdinalIgnoreCase) || !catalogs.TryGetOperators(engine, out var operators))
            throw new InputValidationException($"Engine '{engine}' is not supported. Supported: {string.Join(", ", g.Engines)}.", "engine");

        if (!InputTypes.TryParse(request.InputType, out var inputType))
            throw new InputValidationException($"inputType must be one of: {string.Join(", ", InputTypes.All.Select(t => t.ToId()))}.", "inputType");

        if (string.IsNullOrWhiteSpace(request.Intent) || !catalogs.Intents.ById.TryGetValue(request.Intent.Trim(), out var intent))
            throw new InputValidationException("Unknown intent. See GET /api/v1/intents for the catalog.", "intent");

        var typeId = inputType.ToId();
        if (!intent.CompatibleInputTypes.Contains(typeId, StringComparer.OrdinalIgnoreCase))
            throw new InputValidationException($"Intent '{intent.Id}' does not support inputType '{typeId}'. Compatible: {string.Join(", ", intent.CompatibleInputTypes)}.", "intent", unprocessable: true);

        // --- main input ---
        var raw = request.Input ?? "";
        if (raw.Length > g.MaxQueryLength)
            throw new InputValidationException($"input exceeds {g.MaxQueryLength} characters.", "input");
        var input = QueryNormalizer.NormalizeText(raw);
        if (input.Length == 0)
            throw new InputValidationException("input is required.", "input");

        string? domain = null, username = null, email = null, emailUser = null, stem = null, ext = null;
        Uri? url = null;
        IReadOnlyList<string> words;

        switch (inputType)
        {
            case InputType.Domain:
                if (!QueryNormalizer.TryNormalizeDomain(input, out var d))
                    throw new InputValidationException("A valid domain is required for inputType=domain.", "input");
                domain = d; input = d; words = [d];
                break;
            case InputType.Url:
                if (!QueryNormalizer.TryNormalizeUrl(input, out var u))
                    throw new InputValidationException("A valid http(s) URL is required for inputType=url.", "input");
                url = u; domain = u.Host.ToLowerInvariant(); input = u.ToString(); words = [input];
                break;
            case InputType.Email:
                if (!QueryNormalizer.TryNormalizeEmail(input, out var e, out var lp, out var ed))
                    throw new InputValidationException("A valid email address is required for inputType=email.", "input");
                email = e; emailUser = lp; domain = ed; input = e; words = [e];
                break;
            case InputType.Username:
                if (!QueryNormalizer.TryNormalizeUsername(input, usernames.Value.MaxUsernameLength, out var un))
                    throw new InputValidationException($"A username without whitespace (max {usernames.Value.MaxUsernameLength} chars) is required for inputType=username.", "input");
                username = un; words = [un];
                break;
            case InputType.Filename:
                if (!QueryNormalizer.TryParseFilename(input, out stem, out ext))
                    throw new InputValidationException("A file name with an extension (report.pdf) is required for inputType=filename.", "input");
                words = [stem + "." + ext];
                break;
            default:
                var (w, _) = QueryNormalizer.Words(input);
                if (w.Count == 0) throw new InputValidationException("input must contain at least one word.", "input");
                words = w;
                break;
        }

        // --- options ---
        var fileTypes = new List<string>();
        foreach (var ft in o.FileTypes ?? [])
        {
            var x = (ft ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (x.Length == 0) continue;
            if (!catalogs.FileTypes.ByExtension.ContainsKey(x))
                throw new InputValidationException($"Unknown file type '{x}'. See GET /api/v1/filetypes.", "options.fileTypes");
            if (!fileTypes.Contains(x)) fileTypes.Add(x);
        }
        if (fileTypes.Count > g.MaxFileTypes)
            throw new InputValidationException($"At most {g.MaxFileTypes} file types are allowed.", "options.fileTypes");

        var excludes = new List<string>();
        foreach (var term in o.ExcludeTerms ?? [])
        {
            var x = QueryNormalizer.NormalizeText(term).TrimStart('-').Trim();
            if (x.Length == 0) continue;
            if (x.Length > MaxExcludeTermLength) throw new InputValidationException($"Excluded terms must be at most {MaxExcludeTermLength} characters.", "options.excludeTerms");
            if (!excludes.Contains(x, StringComparer.OrdinalIgnoreCase)) excludes.Add(x);
        }
        if (excludes.Count > g.MaxExcludeTerms)
            throw new InputValidationException($"At most {g.MaxExcludeTerms} excluded terms are allowed.", "options.excludeTerms");

        DateOnly? after = null, before = null;
        if (!string.IsNullOrWhiteSpace(o.After))
        {
            if (!QueryNormalizer.TryParseIsoDate(o.After, out var a)) throw new InputValidationException("after must be an ISO date (YYYY-MM-DD).", "options.after");
            after = a;
        }
        if (!string.IsNullOrWhiteSpace(o.Before))
        {
            if (!QueryNormalizer.TryParseIsoDate(o.Before, out var b)) throw new InputValidationException("before must be an ISO date (YYYY-MM-DD).", "options.before");
            before = b;
        }
        if (after is not null && before is not null && after > before)
            throw new InputValidationException("after must be on or before the before date.", "options.after");

        string? site = null;
        if (!string.IsNullOrWhiteSpace(o.Site))
        {
            if (!QueryNormalizer.TryNormalizeDomain(o.Site, out var s)) throw new InputValidationException("site must be a valid domain.", "options.site");
            site = s;
        }

        var maxVariants = o.MaxVariants ?? g.DefaultVariants;
        if (maxVariants < 1 || maxVariants > g.MaxVariants)
            throw new InputValidationException($"maxVariants must be between 1 and {g.MaxVariants}.", "options.maxVariants");

        var ctx = new GenerationContext
        {
            InputType = inputType, Input = input, Words = words, Intent = intent, Operators = operators, MaxVariants = maxVariants,
            Domain = domain, Url = url, Username = username, Email = email, EmailUser = emailUser, FilenameStem = stem, FilenameExt = ext,
            FileTypes = fileTypes, ExcludeTerms = excludes, After = after, Before = before, Site = site,
            Organization = Context(o.Organization, "options.organization"),
            Location = Context(o.Location, "options.location"),
            Role = Context(o.Role, "options.role"),
            DisplayName = Context(o.DisplayName, "options.displayName"),
        };

        // --- intent option requirements (422: valid request that cannot be generated) ---
        var required = intent.RequiresOptions
            .Concat(intent.RequiresOptionsForInputTypes.TryGetValue(typeId, out var perType) ? perType : [])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in required)
        {
            var satisfied = name.ToLowerInvariant() switch
            {
                "site" => ctx.Site is not null || ctx.Domain is not null,
                "dates" => ctx.HasDates,
                "filetypes" => ctx.FileTypes.Count > 0,
                "excludeterms" => ctx.ExcludeTerms.Count > 0,
                "organization" => ctx.Organization is not null,
                "location" => ctx.Location is not null,
                "role" => ctx.Role is not null,
                "displayname" => ctx.DisplayName is not null,
                "context" => ctx.HasContext,
                _ => true,
            };
            if (!satisfied)
                throw new InputValidationException($"Intent '{intent.Id}' requires options.{name} for inputType '{typeId}'.", $"options.{name}", unprocessable: true);
        }

        return ctx;
    }

    private static string? Context(string? value, string field)
    {
        var s = QueryNormalizer.NormalizeText(value);
        if (s.Length == 0) return null;
        if (s.Length > MaxContextLength) throw new InputValidationException($"{field} must be at most {MaxContextLength} characters.", field);
        return s;
    }
}
