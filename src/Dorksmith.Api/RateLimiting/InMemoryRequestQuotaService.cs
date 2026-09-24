using System.Collections.Concurrent;
using Dorksmith.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.RateLimiting;

/// <summary>
/// Fixed-window counter per (bucket, client key). Suitable for local development and a single production
/// instance; counters reset when the process restarts. Uses <see cref="TimeProvider"/> for testability.
/// </summary>
public sealed class InMemoryRequestQuotaService(IOptionsMonitor<RateLimitingOptions> options, TimeProvider time) : IRequestQuotaService
{
    private sealed class Window { public long Start; public int Count; }

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private long _lastSweepTicks;

    public ValueTask<QuotaDecision> ConsumeAsync(string clientKey, string bucket, CancellationToken ct)
    {
        var o = options.CurrentValue;
        var now = time.GetUtcNow();
        if (!o.Enabled) return ValueTask.FromResult(QuotaDecision.Unlimited(now));

        var windowLength = TimeSpan.FromMinutes(o.WindowMinutes);
        var windowStart = now.Ticks - now.Ticks % windowLength.Ticks;
        var resetAt = new DateTimeOffset(windowStart + windowLength.Ticks, TimeSpan.Zero);

        var w = _windows.GetOrAdd(bucket + "|" + clientKey, _ => new Window { Start = windowStart });
        bool allowed;
        int remaining;
        lock (w)
        {
            if (w.Start != windowStart) { w.Start = windowStart; w.Count = 0; }
            allowed = w.Count < o.PermitLimit;
            if (allowed) w.Count++;
            remaining = Math.Max(0, o.PermitLimit - w.Count);
        }

        SweepIfNeeded(now, windowStart);
        return ValueTask.FromResult(new QuotaDecision(allowed, o.PermitLimit, remaining, resetAt));
    }

    private void SweepIfNeeded(DateTimeOffset now, long currentWindowStart)
    {
        if (_windows.Count < 5_000) return;
        var last = Interlocked.Read(ref _lastSweepTicks);
        if (now.Ticks - last < TimeSpan.TicksPerMinute) return;
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, last) != last) return;
        foreach (var (key, w) in _windows)
            if (w.Start != currentWindowStart) _windows.TryRemove(key, out _);
    }
}
