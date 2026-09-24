using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Dorksmith.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Privacy;

/// <summary>What the rest of the pipeline may know about the caller.</summary>
/// <param name="RateLimitKey">Always an HMAC of the normalised IP; used only in memory for quota buckets.</param>
/// <param name="PersistentKey">What the search log stores: HMAC, raw IP or nothing depending on <see cref="IpLoggingMode"/>.</param>
public sealed record ClientIdentity(string RateLimitKey, string? PersistentKey, IpLoggingMode Mode);

/// <summary>
/// Derives client keys from the connection's remote address (after forwarded-header processing).
/// <c>client_key = HMAC-SHA256(server_secret, normalized_ip)</c>. The raw IP never leaves this class
/// except in <see cref="IpLoggingMode.Raw"/>.
/// </summary>
public sealed class ClientKeyProvider
{
    private readonly byte[] _secret;
    private readonly IpLoggingMode _mode;

    public ClientKeyProvider(IOptions<PrivacyOptions> privacy, ILogger<ClientKeyProvider> logger)
    {
        _mode = privacy.Value.IpLoggingMode;
        var configured = privacy.Value.IpHmacSecret;
        if (string.IsNullOrWhiteSpace(configured) || configured.Length < 16)
        {
            _secret = RandomNumberGenerator.GetBytes(32);
            UsingEphemeralSecret = true;
            logger.LogWarning("IP_HMAC_SECRET is missing or shorter than 16 characters; using an ephemeral per-process secret. Client keys will not be stable across restarts");
        }
        else
        {
            _secret = Encoding.UTF8.GetBytes(configured);
        }
    }

    public bool UsingEphemeralSecret { get; }
    public IpLoggingMode Mode => _mode;

    public ClientIdentity Resolve(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        var normalized = NormalizeIp(ip);
        var hmac = Hmac(normalized);
        var persistent = _mode switch
        {
            IpLoggingMode.Hmac => hmac,
            IpLoggingMode.Raw => normalized,
            _ => null,
        };
        return new ClientIdentity(hmac, persistent, _mode);
    }

    public string Hmac(string normalizedIp)
        => Convert.ToHexString(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(normalizedIp))).ToLowerInvariant();

    /// <summary>IPv4-mapped IPv6 → dotted IPv4; IPv6 → canonical form without scope id; IPv4 as-is.</summary>
    public static string NormalizeIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.ScopeId != 0)
            ip = new IPAddress(ip.GetAddressBytes());
        return ip.ToString();
    }
}
