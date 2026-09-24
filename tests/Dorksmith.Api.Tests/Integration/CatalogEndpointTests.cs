using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dorksmith.Api.Tests.Integration;

public class CatalogEndpointTests : IClassFixture<DorksmithFactory>
{
    private readonly HttpClient _client;

    public CatalogEndpointTests(DorksmithFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Operators_returns_google_catalog_with_etag_and_cache_headers()
    {
        var res = await _client.GetAsync("/api/v1/operators?engine=google");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotNull(res.Headers.ETag);
        Assert.Contains("max-age", res.Headers.CacheControl!.ToString());
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("google", body.GetProperty("engine").GetString());
        Assert.True(body.GetProperty("operators").GetArrayLength() > 10);
    }

    [Fact]
    public async Task Operators_honours_if_none_match()
    {
        var first = await _client.GetAsync("/api/v1/operators");
        var etag = first.Headers.ETag!.Tag;

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/operators");
        req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Operators_unknown_engine_is_404()
    {
        var res = await _client.GetAsync("/api/v1/operators?engine=altavista");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Intents_exposes_public_metadata_without_patterns()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/intents");
        var intents = body.GetProperty("intents");
        Assert.True(intents.GetArrayLength() > 5);
        var first = intents[0];
        Assert.True(first.TryGetProperty("compatibleInputTypes", out _));
        Assert.False(first.TryGetProperty("templates", out _));
        Assert.True(body.GetProperty("families").GetArrayLength() >= 8);
    }

    [Fact]
    public async Task Platforms_supports_query_and_category_filters()
    {
        var all = await _client.GetFromJsonAsync<JsonElement>("/api/v1/platforms");
        var dev = await _client.GetFromJsonAsync<JsonElement>("/api/v1/platforms?category=developer");
        var git = await _client.GetFromJsonAsync<JsonElement>("/api/v1/platforms?q=git");

        Assert.True(all.GetProperty("platforms").GetArrayLength() > dev.GetProperty("platforms").GetArrayLength());
        Assert.All(dev.GetProperty("platforms").EnumerateArray(), p => Assert.Equal("developer", p.GetProperty("category").GetString()));
        Assert.Contains(git.GetProperty("platforms").EnumerateArray(), p => p.GetProperty("id").GetString() == "github");
        Assert.DoesNotContain(all.GetProperty("platforms").EnumerateArray(), p => p.GetProperty("enabled").GetBoolean() == false);
    }

    [Fact]
    public async Task Filetypes_returns_groups_and_extensions()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/filetypes");
        Assert.Contains(body.GetProperty("extensions").EnumerateArray(), e => e.GetProperty("ext").GetString() == "pdf");
        Assert.True(body.GetProperty("groups").GetArrayLength() >= 4);
    }

    [Fact]
    public async Task Public_config_has_no_secrets_and_expected_fields()
    {
        var res = await _client.GetAsync("/api/v1/config/public");
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(12, body.GetProperty("maxVariants").GetInt32());
        Assert.Equal(["google"], body.GetProperty("supportedEngines").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("hmac", body.GetProperty("ipLoggingMode").GetString());
        Assert.True(body.GetProperty("rateLimitPerHour").GetInt32() > 0);
    }
}
