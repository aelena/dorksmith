using Dorksmith.Api.Contracts;

namespace Dorksmith.Api.RateLimiting;

public static class RateLimitHeaders
{
    /// <summary>Writes draft-standard RateLimit-* headers plus the widely used X-RateLimit-* aliases.</summary>
    public static void Apply(HttpResponse response, QuotaDecision quota, DateTimeOffset now)
    {
        if (!quota.Enforced) return;
        var reset = quota.RetryAfterSeconds(now).ToString();
        response.Headers["RateLimit-Limit"] = quota.Limit.ToString();
        response.Headers["RateLimit-Remaining"] = quota.Remaining.ToString();
        response.Headers["RateLimit-Reset"] = reset;
        response.Headers["X-RateLimit-Limit"] = quota.Limit.ToString();
        response.Headers["X-RateLimit-Remaining"] = quota.Remaining.ToString();
        response.Headers["X-RateLimit-Reset"] = quota.ResetAtUtc.ToUnixTimeSeconds().ToString();
        if (!quota.Allowed) response.Headers.RetryAfter = reset;
    }

    public static RateLimitInfo? ToInfo(QuotaDecision quota)
        => quota.Enforced ? new RateLimitInfo(quota.Limit, quota.Remaining, quota.ResetAtUtc) : null;
}
