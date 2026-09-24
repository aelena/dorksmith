using System.Net;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Privacy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Tests.Unit;

public class ClientKeyTests
{
    private static ClientKeyProvider Create(IpLoggingMode mode = IpLoggingMode.Hmac, string? secret = "a-sufficiently-long-test-secret")
        => new(Options.Create(new PrivacyOptions { IpLoggingMode = mode, IpHmacSecret = secret }), NullLogger<ClientKeyProvider>.Instance);

    private static HttpContext Context(string ip)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return ctx;
    }

    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("2001:DB8:0:0:0:0:0:1", "2001:db8::1")]
    [InlineData("fe80::1%3", "fe80::1")]
    public void NormalizeIp_canonicalises_v4_mapped_v6_and_scope_ids(string input, string expected)
        => Assert.Equal(expected, ClientKeyProvider.NormalizeIp(IPAddress.Parse(input)));

    [Fact]
    public void Hmac_mode_stores_pseudonym_not_ip()
    {
        var p = Create();
        var id = p.Resolve(Context("203.0.113.9"));
        Assert.Equal(64, id.PersistentKey!.Length);
        Assert.Matches("^[0-9a-f]{64}$", id.PersistentKey);
        Assert.DoesNotContain("203.0.113.9", id.PersistentKey);
        Assert.Equal(id.PersistentKey, id.RateLimitKey);
        Assert.Equal(IpLoggingMode.Hmac, id.Mode);
    }

    [Fact]
    public void Hmac_is_deterministic_per_secret_and_differs_across_secrets_and_ips()
    {
        var a = Create(secret: "secret-number-one-long-enough");
        var b = Create(secret: "secret-number-two-long-enough");
        Assert.Equal(a.Hmac("203.0.113.9"), a.Hmac("203.0.113.9"));
        Assert.NotEqual(a.Hmac("203.0.113.9"), b.Hmac("203.0.113.9"));
        Assert.NotEqual(a.Hmac("203.0.113.9"), a.Hmac("203.0.113.10"));
        Assert.Equal(a.Resolve(Context("::ffff:203.0.113.9")).RateLimitKey, a.Resolve(Context("203.0.113.9")).RateLimitKey);
    }

    [Fact]
    public void Raw_mode_stores_normalised_ip()
    {
        var id = Create(IpLoggingMode.Raw).Resolve(Context("::ffff:203.0.113.9"));
        Assert.Equal("203.0.113.9", id.PersistentKey);
        Assert.NotEqual(id.PersistentKey, id.RateLimitKey);
    }

    [Fact]
    public void None_mode_stores_nothing_but_still_rate_limits()
    {
        var id = Create(IpLoggingMode.None).Resolve(Context("203.0.113.9"));
        Assert.Null(id.PersistentKey);
        Assert.Equal(64, id.RateLimitKey.Length);
    }

    [Fact]
    public void Missing_or_short_secret_falls_back_to_ephemeral_key()
    {
        Assert.True(Create(secret: null).UsingEphemeralSecret);
        Assert.True(Create(secret: "short").UsingEphemeralSecret);
        Assert.False(Create().UsingEphemeralSecret);
        Assert.NotEqual(Create(secret: null).Hmac("1.2.3.4"), Create(secret: null).Hmac("1.2.3.4"));
    }
}
