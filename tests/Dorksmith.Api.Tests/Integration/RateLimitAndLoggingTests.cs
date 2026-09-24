using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dorksmith.Api.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Dorksmith.Api.Tests.Integration;

public class RateLimitAndLoggingTests
{
    private static readonly object Payload = new { input = "example.com", inputType = "domain", intent = "public-documents", options = new { fileTypes = new[] { "pdf" } } };

    [Fact]
    public async Task Requests_are_rejected_with_429_after_the_configured_quota()
    {
        using var factory = new DorksmithFactory().WithSettings(("RateLimiting:PermitLimit", "3"), ("RateLimiting:WindowMinutes", "60"));
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/v1/dorks/generate", Payload);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("3", ok.Headers.GetValues("RateLimit-Limit").Single());
            Assert.Equal((2 - i).ToString(), ok.Headers.GetValues("RateLimit-Remaining").Single());
            var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2 - i, body.GetProperty("rateLimit").GetProperty("remaining").GetInt32());
        }

        var limited = await client.PostAsJsonAsync("/api/v1/dorks/generate", Payload);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        var err = await limited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limit_exceeded", err.GetProperty("error").GetString());
        Assert.True(err.GetProperty("retryAfterSeconds").GetInt32() > 0);

        // Handle expansion shares the bucket.
        var handle = await client.PostAsJsonAsync("/api/v1/handles/expand", new { username = "alice42" });
        Assert.Equal(HttpStatusCode.TooManyRequests, handle.StatusCode);

        // Catalog reads are never rate limited.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/operators")).StatusCode);
    }

    [Fact]
    public async Task Rate_limiting_can_be_disabled()
    {
        using var factory = new DorksmithFactory().WithSettings(("RateLimiting:Enabled", "false"), ("RateLimiting:PermitLimit", "1"));
        var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var res = await client.PostAsJsonAsync("/api/v1/dorks/generate", Payload);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.False(res.Headers.Contains("RateLimit-Limit"));
        }
    }

    [Fact]
    public async Task Generation_requests_are_logged_through_the_store_without_raw_ip()
    {
        using var factory = new DorksmithFactory();
        var client = factory.CreateClient();
        var reader = factory.Services.GetRequiredService<ISearchLogReader>();
        var before = await reader.CountAsync(default);

        var res = await client.PostAsJsonAsync("/api/v1/dorks/generate", Payload);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var requestId = res.Headers.GetValues("X-Request-Id").Single();

        Assert.Equal(before + 1, await reader.CountAsync(default));
        var entry = (await reader.ReadRecentAsync(1, default)).Single();
        Assert.Equal(requestId, entry.Id);
        Assert.Equal("domain", entry.InputType);
        Assert.Equal("public-documents", entry.Intent);
        Assert.Equal("google", entry.Engine);
        Assert.Equal("example.com", entry.NormalizedInput);
        Assert.Contains("\"fileTypes\":[\"pdf\"]", entry.OptionsJson);
        Assert.InRange(entry.VariantCount, 3, 6);
        Assert.Equal(200, entry.HttpStatus);
        Assert.Equal("Hmac", entry.IpMode);
        Assert.Matches("^[0-9a-f]{64}$", entry.ClientKey);
        Assert.DoesNotContain("127.0.0.1", entry.ClientKey);
        Assert.DoesNotContain("::1", entry.ClientKey);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", entry.CatalogVersion);
    }

    [Fact]
    public async Task Rejected_and_invalid_requests_are_logged_with_their_status()
    {
        using var factory = new DorksmithFactory().WithSettings(("RateLimiting:PermitLimit", "1"));
        var client = factory.CreateClient();
        var reader = factory.Services.GetRequiredService<ISearchLogReader>();

        await client.PostAsJsonAsync("/api/v1/dorks/generate", new { input = "not a domain", inputType = "domain", intent = "public-documents" });
        await client.PostAsJsonAsync("/api/v1/dorks/generate", Payload);

        var entries = await reader.ReadRecentAsync(2, default);
        Assert.Equal([429, 400], entries.Select(e => e.HttpStatus));
        Assert.Equal("not a domain", entries[1].NormalizedInput);
    }

    [Fact]
    public async Task Raw_and_none_ip_modes_are_honoured()
    {
        using var raw = new DorksmithFactory().WithSettings(("Privacy:IpLoggingMode", "Raw"));
        await raw.CreateClient().PostAsJsonAsync("/api/v1/dorks/generate", Payload);
        var rawEntry = (await raw.Services.GetRequiredService<ISearchLogReader>().ReadRecentAsync(1, default)).Single();
        Assert.Equal("Raw", rawEntry.IpMode);
        Assert.NotNull(rawEntry.ClientKey);
        Assert.True(IPAddress.TryParse(rawEntry.ClientKey, out _));

        using var none = new DorksmithFactory().WithSettings(("Privacy:IpLoggingMode", "None"));
        await none.CreateClient().PostAsJsonAsync("/api/v1/dorks/generate", Payload);
        var noneEntry = (await none.Services.GetRequiredService<ISearchLogReader>().ReadRecentAsync(1, default)).Single();
        Assert.Equal("None", noneEntry.IpMode);
        Assert.Null(noneEntry.ClientKey);
    }

    [Fact]
    public async Task Search_log_can_be_disabled()
    {
        using var factory = new DorksmithFactory().WithSettings(("SearchLog:Enabled", "false"));
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/dorks/generate", Payload)).StatusCode);
        Assert.Equal(0, await factory.Services.GetRequiredService<ISearchLogReader>().CountAsync(default));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }
}
