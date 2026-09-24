using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Generation;

namespace Dorksmith.Api.Tests.Unit;

public class GeneratorTests
{
    private readonly DorkGenerator _gen = TestServices.Generator();

    private static GenerateRequest Req(string input, string type, string intent, GenerateOptions? o = null)
        => new(input, type, intent, "google", o);

    [Fact]
    public void Same_input_yields_identical_ordered_output()
    {
        var req = Req("example.com", "domain", "public-documents", new GenerateOptions { FileTypes = ["pdf", "docx"], ExcludeTerms = ["jobs"] });
        var a = _gen.Generate(req).Variants.Select(v => v.Query).ToList();
        var b = _gen.Generate(req).Variants.Select(v => v.Query).ToList();
        var c = TestServices.Generator().Generate(req).Variants.Select(v => v.Query).ToList();
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Spec_example_public_documents_balanced_variant()
    {
        var r = _gen.Generate(Req("example.com", "domain", "public-documents",
            new GenerateOptions { FileTypes = ["pdf", "docx", "xlsx"], ExcludeTerms = ["jobs"] }));
        var first = r.Variants[0];
        Assert.Equal("pd-domain-balanced", first.Id);
        Assert.Equal("Balanced", first.Label);
        Assert.Equal("site:example.com (filetype:pdf OR filetype:docx OR filetype:xlsx) -jobs", first.Query);
        Assert.Equal(["site:", "(", "filetype:", "OR", "-"], first.Operators);
        Assert.Empty(first.Warnings);
    }

    [Fact]
    public void Default_variant_count_is_six_and_configurable()
    {
        var r = _gen.Generate(Req("quarterly roadmap", "keyword", "general-discovery"));
        Assert.Equal(6, r.Variants.Count);
        var two = _gen.Generate(Req("quarterly roadmap", "keyword", "general-discovery", new GenerateOptions { MaxVariants = 2 }));
        Assert.Equal(2, two.Variants.Count);
        Assert.Equal(r.Variants.Take(2).Select(v => v.Id), two.Variants.Select(v => v.Id));
    }

    [Fact]
    public void Variants_are_distinct_after_canonicalisation()
    {
        var r = _gen.Generate(Req("quarterly roadmap", "keyword", "general-discovery", new GenerateOptions { MaxVariants = 12 }));
        var canon = r.Variants.Select(v => QueryNormalizer.Canonicalize(v.Query)).ToList();
        Assert.Equal(canon.Count, canon.Distinct().Count());
    }

    [Fact]
    public void Templates_that_collapse_to_the_same_query_are_deduplicated()
    {
        // Without site/dates/exclusions, several general templates would render identically; only one survives.
        var r = _gen.Generate(Req("roadmap", "keyword", "general-discovery", new GenerateOptions { MaxVariants = 12 }));
        var queries = r.Variants.Select(v => v.Query).ToList();
        Assert.Equal(queries.Count, queries.Distinct().Count());
        Assert.DoesNotContain(r.Variants, v => v.Id == "gd-broad"); // single word: {termsAny} cannot resolve
    }

    [Fact]
    public void Diversity_prefers_new_families_first()
    {
        var r = _gen.Generate(Req("quarterly roadmap", "keyword", "general-discovery"));
        var families = r.Variants.Select(v => v.Family).ToList();
        Assert.Equal(families.Count, families.Distinct().Count());
    }

    [Fact]
    public void Deprecated_operator_in_user_exclusion_is_neutralised_not_emitted()
    {
        var r = _gen.Generate(Req("example.com", "domain", "public-documents", new GenerateOptions { ExcludeTerms = ["cache:example.com"] }));
        Assert.All(r.Variants, v => Assert.DoesNotContain("cache:", v.Operators));
        Assert.Contains(r.Variants, v => v.Query.Contains("-\"cache:example.com\""));
    }

    [Fact]
    public void User_terms_never_become_operators()
    {
        var r = _gen.Generate(Req("-secret site:evil.com OR", "keyword", "general-discovery", new GenerateOptions { MaxVariants = 12 }));
        foreach (var v in r.Variants)
        {
            var outsideQuotes = System.Text.RegularExpressions.Regex.Replace(v.Query, "\"[^\"]*\"", "\"\"");
            Assert.DoesNotContain("site:evil.com", outsideQuotes);
            Assert.DoesNotContain("-secret", outsideQuotes);
            Assert.DoesNotContain("site:", v.Operators.Where(o => v.Query.Contains("site:evil")).Where(_ => !outsideQuotes.Contains("site:")));
        }
    }

    [Fact]
    public void Dates_are_emitted_as_iso_after_before()
    {
        var r = _gen.Generate(Req("example.com", "domain", "date-bounded", new GenerateOptions { After = "2024-01-01", Before = "2024-12-31" }));
        Assert.All(r.Variants, v => Assert.Contains("after:2024-01-01 before:2024-12-31", v.Query));
    }

    [Fact]
    public void Recent_family_is_skipped_without_dates()
    {
        var r = _gen.Generate(Req("quarterly roadmap", "keyword", "general-discovery", new GenerateOptions { MaxVariants = 12 }));
        Assert.DoesNotContain(r.Variants, v => v.Family == "recent");
        var with = _gen.Generate(Req("quarterly roadmap", "keyword", "general-discovery", new GenerateOptions { MaxVariants = 12, After = "2025-01-01" }));
        Assert.Contains(with.Variants, v => v.Family == "recent" && v.Query.Contains("after:2025-01-01"));
    }

    [Fact]
    public void Username_input_strips_at_for_syntax_but_keeps_quoted_at_form()
    {
        var r = _gen.Generate(Req("@alice42", "username", "username-exact", new GenerateOptions { MaxVariants = 12 }));
        Assert.Equal("alice42", r.Context.Username);
        Assert.Contains(r.Variants, v => v.Query.StartsWith("\"alice42\""));
        Assert.Contains(r.Variants, v => v.Query.Contains("\"@alice42\""));
        Assert.Contains(r.Variants, v => v.Query.Contains("inurl:alice42"));
    }

    [Fact]
    public void Platform_sites_come_from_catalog_ordered_by_weight()
    {
        var r = _gen.Generate(Req("alice42", "username", "username-profiles", new GenerateOptions { MaxVariants = 1 }));
        var q = r.Variants[0].Query;
        Assert.StartsWith("\"alice42\" (site:x.com OR site:instagram.com", q);
        Assert.Equal(6, q.Split("site:").Length - 1);
    }

    [Fact]
    public void Email_input_derives_domain_for_site_scoping()
    {
        var r = _gen.Generate(Req("Alice@Example.com", "email", "email-mentions", new GenerateOptions { MaxVariants = 12 }));
        Assert.Contains(r.Variants, v => v.Query == "\"Alice@example.com\"");
        Assert.Contains(r.Variants, v => v.Query.Contains("-site:example.com"));
        Assert.Contains(r.Variants, v => v.Query.Contains("\"Alice\" site:example.com"));
    }

    [Fact]
    public void Url_input_scopes_to_host_and_path()
    {
        var r = _gen.Generate(Req("https://example.com/docs/api/", "url", "url-mentions", new GenerateOptions { MaxVariants = 12 }));
        Assert.Contains(r.Variants, v => v.Query.Contains("site:example.com inurl:\"docs/api\""));
        Assert.Contains(r.Variants, v => v.Query.Contains("\"example.com/docs/api\""));
    }

    [Fact]
    public void Person_name_variants_reverse_and_initial()
    {
        var r = _gen.Generate(Req("Alice Smith", "person", "person-name-variants", new GenerateOptions { MaxVariants = 12 }));
        var q = r.Variants.Select(v => v.Query).ToList();
        Assert.Contains("\"Alice Smith\"", q);
        Assert.Contains("\"Smith Alice\"", q);
        Assert.Contains("\"A. Smith\"", q);
        Assert.Contains("\"Alice * Smith\"", q);
    }

    [Fact]
    public void Defensive_intent_only_targets_domain_and_uses_default_file_types()
    {
        var r = _gen.Generate(Req("example.com", "domain", "exposed-config-files"));
        Assert.True(r.Context.Intent.IsDefensive);
        Assert.Contains(r.Variants, v => v.Query.StartsWith("site:example.com (filetype:env OR filetype:ini"));
    }

    [Fact]
    public void Unreliable_operators_are_never_emitted_by_templates()
    {
        var ops = TestServices.GoogleOperators();
        var blocked = ops.Operators.Where(o => !o.Generate).Select(o => o.Token).ToHashSet();
        foreach (var intent in TestServices.Catalogs.Value.Intents.Intents)
        foreach (var type in intent.CompatibleInputTypes)
        {
            var input = SampleInput(type);
            var opts = new GenerateOptions { MaxVariants = 12, Organization = "Example Corp", Location = "Madrid", Role = "CTO", DisplayName = "Alice Smith", After = "2024-01-01", Site = type == "domain" ? null : "example.com" };
            var r = _gen.Generate(Req(input, type, intent.Id, opts));
            foreach (var v in r.Variants)
                Assert.DoesNotContain(v.Operators, blocked.Contains);
        }
    }

    [Fact]
    public void Every_template_is_reachable_by_at_least_one_request()
    {
        var reached = new HashSet<string>();
        foreach (var intent in TestServices.Catalogs.Value.Intents.Intents)
        foreach (var type in intent.CompatibleInputTypes)
        foreach (var opts in SampleOptions(type))
        {
            try
            {
                var r = _gen.Generate(Req(SampleInput(type), type, intent.Id, opts));
                foreach (var v in r.Variants) reached.Add(v.Id);
            }
            catch (InputValidationException) { /* options not satisfying requirements */ }
        }
        var all = TestServices.Catalogs.Value.Intents.Intents.SelectMany(i => i.Templates.Select(t => t.Id)).ToList();
        var unreachable = all.Except(reached).ToList();
        Assert.True(unreachable.Count == 0, "Unreachable templates: " + string.Join(", ", unreachable));
    }

    [Theory]
    [InlineData("", "keyword", "general-discovery", "input")]
    [InlineData("example.com", "bogus", "general-discovery", "inputType")]
    [InlineData("example.com", "domain", "no-such-intent", "intent")]
    [InlineData("not a domain", "domain", "public-documents", "input")]
    [InlineData("nope", "email", "email-mentions", "input")]
    [InlineData("has space", "username", "username-exact", "input")]
    public void Invalid_requests_are_400_with_field(string input, string type, string intent, string field)
    {
        var ex = Assert.Throws<InputValidationException>(() => _gen.Generate(Req(input, type, intent)));
        Assert.False(ex.Unprocessable);
        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Incompatible_intent_and_missing_required_options_are_422()
    {
        var ex = Assert.Throws<InputValidationException>(() => _gen.Generate(Req("alice42", "username", "public-documents")));
        Assert.True(ex.Unprocessable);

        var ex2 = Assert.Throws<InputValidationException>(() => _gen.Generate(Req("Alice Smith", "person", "person-organization")));
        Assert.True(ex2.Unprocessable);
        Assert.Equal("options.organization", ex2.Field);

        var ex3 = Assert.Throws<InputValidationException>(() => _gen.Generate(Req("roadmap", "keyword", "site-scoped")));
        Assert.True(ex3.Unprocessable);
        Assert.Equal("options.site", ex3.Field);
    }

    [Fact]
    public void Option_limits_are_enforced()
    {
        var gen = TestServices.Generator(new GenerationOptions { MaxExcludeTerms = 2, MaxFileTypes = 1, MaxQueryLength = 20 });
        Assert.Equal("options.excludeTerms", Assert.Throws<InputValidationException>(() => gen.Generate(Req("x", "keyword", "general-discovery", new GenerateOptions { ExcludeTerms = ["a", "b", "c"] }))).Field);
        Assert.Equal("options.fileTypes", Assert.Throws<InputValidationException>(() => gen.Generate(Req("x", "keyword", "general-discovery", new GenerateOptions { FileTypes = ["pdf", "docx"] }))).Field);
        Assert.Equal("options.fileTypes", Assert.Throws<InputValidationException>(() => gen.Generate(Req("x", "keyword", "general-discovery", new GenerateOptions { FileTypes = ["exe"] }))).Field);
        Assert.Equal("input", Assert.Throws<InputValidationException>(() => gen.Generate(Req(new string('a', 21), "keyword", "general-discovery"))).Field);
        Assert.Equal("options.after", Assert.Throws<InputValidationException>(() => gen.Generate(Req("x", "keyword", "general-discovery", new GenerateOptions { After = "2025-01-01", Before = "2024-01-01" }))).Field);
        Assert.Equal("options.maxVariants", Assert.Throws<InputValidationException>(() => gen.Generate(Req("x", "keyword", "general-discovery", new GenerateOptions { MaxVariants = 13 }))).Field);
    }

    [Fact]
    public void Validate_reports_operators_and_warnings_without_rewriting()
    {
        var v = _gen.Validate("cache:example.com site:example.com or intitle:\"index of\" filetype:.pdf bogus:x", "google");
        Assert.Equal("cache:example.com site:example.com or intitle:\"index of\" filetype:.pdf bogus:x", v.Query);
        Assert.Contains(v.Operators, o => o.Token == "cache:" && o.Support == "deprecated");
        Assert.Contains(v.Operators, o => o.Token == "site:" && o.Support == "official");
        Assert.Contains(v.Warnings, w => w.Contains("deprecated"));
        Assert.Contains(v.Warnings, w => w.Contains("Lower-case 'or'"));
        Assert.Contains(v.Warnings, w => w.Contains("no leading dot"));
        Assert.Contains(v.Warnings, w => w.Contains("'bogus:'"));
    }

    [Fact]
    public void Analyzer_flags_unbalanced_syntax()
    {
        var a = QueryValidator.Analyze("(site:example.com \"open", TestServices.GoogleOperators());
        Assert.True(a.HasErrors);
        Assert.Contains(a.Warnings, w => w.Contains("Unbalanced double quotes"));
        Assert.Contains(a.Warnings, w => w.Contains("Unbalanced parentheses"));
    }

    private static string SampleInput(string type) => type switch
    {
        "domain" => "example.com",
        "url" => "https://example.com/docs/api",
        "username" => "alice42",
        "email" => "alice@example.com",
        "filename" => "annual-report.pdf",
        "person" => "Alice Smith",
        "organization" => "Example Corp",
        "technology" => "Kubernetes",
        _ => "quarterly roadmap",
    };

    private static IEnumerable<GenerateOptions> SampleOptions(string type)
    {
        yield return new GenerateOptions { MaxVariants = 12 };
        yield return new GenerateOptions { MaxVariants = 12, Site = type == "domain" ? null : "example.com", After = "2024-01-01", Before = "2024-12-31", ExcludeTerms = ["jobs"], FileTypes = ["pdf", "docx"] };
        yield return new GenerateOptions { MaxVariants = 12, Organization = "Example Corp", Location = "Madrid", Role = "CTO", DisplayName = "Alice Smith" };
        yield return new GenerateOptions { MaxVariants = 12, After = "2024-01-01" };
    }
}
