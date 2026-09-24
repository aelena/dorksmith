namespace Dorksmith.Api.RateLimiting;

/// <summary>Outcome of consuming one permit from a client's quota bucket.</summary>
public sealed record QuotaDecision(bool Allowed, int Limit, int Remaining, DateTimeOffset ResetAtUtc, bool Enforced = true)
{
    public static QuotaDecision Unlimited(DateTimeOffset now) => new(true, 0, 0, now, Enforced: false);

    public int RetryAfterSeconds(DateTimeOffset now) => Math.Max(1, (int)Math.Ceiling((ResetAtUtc - now).TotalSeconds));
}

/// <summary>
/// Quota abstraction so the single-instance in-memory limiter can later be replaced by a shared
/// atomic store (Redis, SQL) for multi-instance deployments without touching the endpoints.
/// </summary>
public interface IRequestQuotaService
{
    ValueTask<QuotaDecision> ConsumeAsync(string clientKey, string bucket, CancellationToken ct);
}
