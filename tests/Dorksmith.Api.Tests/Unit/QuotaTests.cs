using Dorksmith.Api.Configuration;
using Dorksmith.Api.RateLimiting;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Tests.Unit;

public class QuotaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);

    private static (InMemoryRequestQuotaService Svc, TestServices.FakeTimeProvider Clock) Create(int permit = 3, int windowMinutes = 60, bool enabled = true)
    {
        var clock = new TestServices.FakeTimeProvider(T0);
        var monitor = new StaticMonitor(new RateLimitingOptions { Enabled = enabled, PermitLimit = permit, WindowMinutes = windowMinutes });
        return (new InMemoryRequestQuotaService(monitor, clock), clock);
    }

    [Fact]
    public async Task Allows_up_to_permit_then_rejects_with_reset_at_window_end()
    {
        var (svc, _) = Create(permit: 3);
        var d1 = await svc.ConsumeAsync("k", "generate", default);
        var d2 = await svc.ConsumeAsync("k", "generate", default);
        var d3 = await svc.ConsumeAsync("k", "generate", default);
        var d4 = await svc.ConsumeAsync("k", "generate", default);

        Assert.True(d1.Allowed); Assert.Equal(2, d1.Remaining);
        Assert.True(d3.Allowed); Assert.Equal(0, d3.Remaining);
        Assert.False(d4.Allowed); Assert.Equal(0, d4.Remaining);
        Assert.Equal(3, d4.Limit);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 11, 0, 0, TimeSpan.Zero), d4.ResetAtUtc);
        Assert.Equal(45 * 60, d4.RetryAfterSeconds(T0));
    }

    [Fact]
    public async Task Window_rolls_over_with_the_clock()
    {
        var (svc, clock) = Create(permit: 1);
        Assert.True((await svc.ConsumeAsync("k", "generate", default)).Allowed);
        Assert.False((await svc.ConsumeAsync("k", "generate", default)).Allowed);
        clock.Advance(TimeSpan.FromMinutes(46));
        Assert.True((await svc.ConsumeAsync("k", "generate", default)).Allowed);
    }

    [Fact]
    public async Task Keys_and_buckets_are_independent()
    {
        var (svc, _) = Create(permit: 1);
        Assert.True((await svc.ConsumeAsync("a", "generate", default)).Allowed);
        Assert.True((await svc.ConsumeAsync("b", "generate", default)).Allowed);
        Assert.True((await svc.ConsumeAsync("a", "other", default)).Allowed);
        Assert.False((await svc.ConsumeAsync("a", "generate", default)).Allowed);
    }

    [Fact]
    public async Task Disabled_limiter_is_unlimited_and_not_enforced()
    {
        var (svc, _) = Create(permit: 1, enabled: false);
        for (var i = 0; i < 10; i++)
        {
            var d = await svc.ConsumeAsync("k", "generate", default);
            Assert.True(d.Allowed);
            Assert.False(d.Enforced);
        }
    }

    private sealed class StaticMonitor(RateLimitingOptions value) : IOptionsMonitor<RateLimitingOptions>
    {
        public RateLimitingOptions CurrentValue => value;
        public RateLimitingOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<RateLimitingOptions, string?> listener) => null;
    }
}
