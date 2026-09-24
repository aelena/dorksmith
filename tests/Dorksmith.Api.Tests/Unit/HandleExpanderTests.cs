using Dorksmith.Api.Configuration;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Handles;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Tests.Unit;

public class HandleExpanderTests
{
    private static HandleExpander Create(UsernameSearchOptions? o = null)
        => new(TestServices.Catalogs.Value, TestServices.Generator(), Options.Create(o ?? new UsernameSearchOptions()));

    [Fact]
    public void Expands_handle_into_profiles_and_queries_without_claiming_existence()
    {
        var r = Create().Expand(new HandleExpandRequest("@alice42"));
        Assert.Equal("@alice42", r.Username);
        Assert.Equal("alice42", r.NormalizedUsername);
        Assert.Equal(30, r.Profiles.Count);
        Assert.All(r.Profiles, p => Assert.Equal("not-checked", p.Status));
        Assert.Contains(r.Profiles, p => p.PlatformId == "github" && p.Url == "https://github.com/alice42" && p.SearchQuery == "site:github.com \"alice42\"");
        Assert.NotEmpty(r.Queries);
        Assert.Contains(r.Queries, q => q.Query == "\"alice42\"");
        Assert.Contains("NOT verified", r.Notice);
    }

    [Fact]
    public void Profiles_are_ordered_by_weight_then_id()
    {
        var r = Create().Expand(new HandleExpandRequest("alice42", MaxPlatforms: 5));
        var expected = TestServices.Catalogs.Value.Platforms.Platforms.Where(p => p.Enabled)
            .OrderByDescending(p => p.Weight).ThenBy(p => p.Id, StringComparer.Ordinal).Take(5).Select(p => p.Id);
        Assert.Equal(expected, r.Profiles.Select(p => p.PlatformId));
    }

    [Fact]
    public void Category_and_platform_filters_apply()
    {
        var dev = Create().Expand(new HandleExpandRequest("alice42", Categories: ["developer"], MaxPlatforms: 100));
        Assert.NotEmpty(dev.Profiles);
        Assert.All(dev.Profiles, p => Assert.Equal("developer", p.Category));

        var two = Create().Expand(new HandleExpandRequest("alice42", PlatformIds: ["github", "reddit"]));
        Assert.Equal(["github", "reddit"], two.Profiles.Select(p => p.PlatformId).Order());
    }

    [Fact]
    public void Disabled_platforms_are_never_returned()
    {
        var r = Create().Expand(new HandleExpandRequest("alice42", MaxPlatforms: 100));
        Assert.DoesNotContain(r.Profiles, p => p.PlatformId == "discord");
    }

    [Fact]
    public void Subdomain_templates_reject_handles_that_are_not_host_labels()
    {
        var r = Create().Expand(new HandleExpandRequest("alice.b_c", PlatformIds: ["tumblr", "github"]));
        var tumblr = r.Profiles.Single(p => p.PlatformId == "tumblr");
        Assert.Null(tumblr.Url);
        Assert.Contains("sub-domain", tumblr.Caveat);
        var github = r.Profiles.Single(p => p.PlatformId == "github");
        Assert.Equal("https://github.com/alice.b_c", github.Url);
        Assert.Contains("GitHub logins", github.Caveat);
    }

    [Fact]
    public void Case_is_preserved_only_for_case_sensitive_platforms()
    {
        var r = Create().Expand(new HandleExpandRequest("AliceX", PlatformIds: ["hackernews", "github"]));
        Assert.Equal("https://news.ycombinator.com/user?id=AliceX", r.Profiles.Single(p => p.PlatformId == "hackernews").Url);
        Assert.Equal("https://github.com/alicex", r.Profiles.Single(p => p.PlatformId == "github").Url);
        Assert.All(r.Profiles, p => Assert.Contains("\"AliceX\"", p.SearchQuery));
    }

    [Fact]
    public void Url_values_are_escaped()
    {
        var r = Create().Expand(new HandleExpandRequest("a&b=c", PlatformIds: ["github"]));
        Assert.Equal("https://github.com/a%26b%3Dc", r.Profiles[0].Url);
        Assert.Contains(r.Warnings, w => w.Contains("characters"));
    }

    [Theory]
    [InlineData("", "username")]
    [InlineData("has space", "username")]
    [InlineData("@", "username")]
    public void Invalid_usernames_are_rejected(string input, string field)
    {
        var ex = Assert.Throws<InputValidationException>(() => Create().Expand(new HandleExpandRequest(input)));
        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Unknown_category_platform_and_out_of_range_max_are_rejected()
    {
        Assert.Equal("categories", Assert.Throws<InputValidationException>(() => Create().Expand(new HandleExpandRequest("a", Categories: ["nope"]))).Field);
        Assert.Equal("platformIds", Assert.Throws<InputValidationException>(() => Create().Expand(new HandleExpandRequest("a", PlatformIds: ["nope"]))).Field);
        Assert.Equal("maxPlatforms", Assert.Throws<InputValidationException>(() => Create().Expand(new HandleExpandRequest("a", MaxPlatforms: 101))).Field);
        Assert.Equal("username", Assert.Throws<InputValidationException>(() => Create(new UsernameSearchOptions { MaxUsernameLength = 3 }).Expand(new HandleExpandRequest("abcd"))).Field);
    }
}
