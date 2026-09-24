using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dorksmith.Api.Tests.Integration;

public class HealthEndpointTests : IClassFixture<DorksmithFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(DorksmithFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Live_returns_200()
    {
        var res = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_returns_200_with_valid_catalogs()
    {
        var res = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var res = await _client.GetAsync("/health/live");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
    }
}
