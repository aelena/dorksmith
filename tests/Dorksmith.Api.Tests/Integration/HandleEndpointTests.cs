using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dorksmith.Api.Tests.Integration;

public class HandleEndpointTests : IClassFixture<DorksmithFactory>
{
    private readonly DorksmithFactory _factory;
    private readonly HttpClient _client;

    public HandleEndpointTests(DorksmithFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Expand_returns_profiles_with_not_checked_status()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/handles/expand", new { username = "alice42", categories = new[] { "general-social", "developer" }, maxPlatforms = 30 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alice42", body.GetProperty("username").GetString());
        var profiles = body.GetProperty("profiles").EnumerateArray().ToList();
        Assert.NotEmpty(profiles);
        Assert.All(profiles, p => Assert.Equal("not-checked", p.GetProperty("status").GetString()));
        Assert.All(profiles, p => Assert.Contains(p.GetProperty("category").GetString(), new[] { "general-social", "developer" }));
        var github = profiles.Single(p => p.GetProperty("platformId").GetString() == "github");
        Assert.Equal("https://github.com/alice42", github.GetProperty("url").GetString());
        Assert.Equal("site:github.com \"alice42\"", github.GetProperty("searchQuery").GetString());
        Assert.True(body.GetProperty("queries").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Whitespace_username_is_400()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/handles/expand", new { username = "alice smith" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_input", body.GetProperty("error").GetString());
        Assert.Equal("username", body.GetProperty("field").GetString());
    }

    [Fact]
    public async Task Unknown_category_is_400()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/handles/expand", new { username = "alice42", categories = new[] { "bogus" } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Feature_can_be_disabled()
    {
        using var factory = new DorksmithFactory().WithSettings(("UsernameSearch:Enabled", "false"));
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/handles/expand", new { username = "alice42" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("feature_disabled", body.GetProperty("error").GetString());
        var cfg = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/public");
        Assert.False(cfg.GetProperty("usernameSearchEnabled").GetBoolean());
    }
}
