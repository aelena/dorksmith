using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dorksmith.Api.Tests.Integration;

public class DorkEndpointTests : IClassFixture<DorksmithFactory>
{
    private readonly HttpClient _client;

    public DorkEndpointTests(DorksmithFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Generate_returns_variants_with_request_id_and_catalog_version()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/dorks/generate", new
        {
            input = "example.com",
            inputType = "domain",
            intent = "public-documents",
            engine = "google",
            options = new { fileTypes = new[] { "pdf", "docx", "xlsx" }, excludeTerms = new[] { "jobs" }, maxVariants = 6 },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(26, body.GetProperty("requestId").GetString()!.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", body.GetProperty("catalogVersion").GetString());
        var variants = body.GetProperty("variants");
        Assert.Equal(6, variants.GetArrayLength());
        var first = variants[0];
        Assert.Equal("site:example.com (filetype:pdf OR filetype:docx OR filetype:xlsx) -jobs", first.GetProperty("query").GetString());
        Assert.Equal("Balanced", first.GetProperty("label").GetString());
        Assert.True(first.GetProperty("explanation").GetString()!.Length > 10);
        Assert.Contains("site:", first.GetProperty("operators").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal("no-store", res.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Generate_is_deterministic_across_requests()
    {
        var payload = new { input = "Alice Smith", inputType = "person", intent = "person-social-profiles" };
        var a = await (await _client.PostAsJsonAsync("/api/v1/dorks/generate", payload)).Content.ReadFromJsonAsync<JsonElement>();
        var b = await (await _client.PostAsJsonAsync("/api/v1/dorks/generate", payload)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("variants").ToString(), b.GetProperty("variants").ToString());
    }

    [Fact]
    public async Task Invalid_input_is_400_with_stable_error_shape()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/dorks/generate", new { input = "not a domain", inputType = "domain", intent = "public-documents" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_input", body.GetProperty("error").GetString());
        Assert.Equal("input", body.GetProperty("field").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Incompatible_intent_is_422()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/dorks/generate", new { input = "alice42", inputType = "username", intent = "exposed-config-files" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cannot_generate", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Malformed_json_is_400_not_500()
    {
        var res = await _client.PostAsync("/api/v1/dorks/generate", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_input", body.GetProperty("error").GetString());
        Assert.DoesNotContain("Exception", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Validate_analyses_a_hand_written_query()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/dorks/validate", new { query = "cache:example.com site:example.com filetype:pdf", engine = "google" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var ops = body.GetProperty("operators").EnumerateArray().Select(o => o.GetProperty("token").GetString()).ToList();
        Assert.Equal(["cache:", "site:", "filetype:"], ops);
        Assert.Contains(body.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("deprecated"));
    }
}
