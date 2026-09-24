using Dorksmith.Api.Configuration;
using Dorksmith.Api.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Tests.Unit;

public class SearchLogTests
{
    private static SqliteSearchLogStore Create(string path = ":memory:")
        => new(Options.Create(new SearchLogOptions { SqlitePath = path }), new Env(), NullLogger<SqliteSearchLogStore>.Instance);

    private static SearchLogEntry Entry(string id, DateTimeOffset at, string? key = "abc") => new(
        id, at, key, "Hmac", "domain", "public-documents", "google", "example.com", "{\"fileTypes\":[\"pdf\"]}", 6, 200, 12, "2026-09-24", "browser");

    [Fact]
    public async Task Append_and_read_round_trip()
    {
        using var store = Create();
        var at = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
        await store.AppendAsync(Entry("01A", at), default);
        await store.AppendAsync(Entry("01B", at.AddMinutes(1), key: null), default);

        Assert.Equal(2, await store.CountAsync(default));
        var recent = await store.ReadRecentAsync(10, default);
        Assert.Equal(["01B", "01A"], recent.Select(e => e.Id));
        var a = recent[1];
        Assert.Equal(at, a.OccurredAtUtc);
        Assert.Equal("abc", a.ClientKey);
        Assert.Equal("public-documents", a.Intent);
        Assert.Equal("{\"fileTypes\":[\"pdf\"]}", a.OptionsJson);
        Assert.Equal(6, a.VariantCount);
        Assert.Equal(12, a.RequestDurationMs);
        Assert.Equal("browser", a.UserAgentFamily);
        Assert.Null(recent[0].ClientKey);
    }

    [Fact]
    public async Task Purge_removes_only_entries_older_than_cutoff()
    {
        using var store = Create();
        var now = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
        await store.AppendAsync(Entry("old", now.AddDays(-40)), default);
        await store.AppendAsync(Entry("edge", now.AddDays(-30).AddSeconds(1)), default);
        await store.AppendAsync(Entry("new", now), default);

        var removed = await store.PurgeOlderThanAsync(now.AddDays(-30), default);
        Assert.Equal(1, removed);
        Assert.Equal(["new", "edge"], (await store.ReadRecentAsync(10, default)).Select(e => e.Id));
    }

    [Fact]
    public async Task Retention_service_uses_configured_days_and_clock()
    {
        using var store = Create();
        var now = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
        await store.AppendAsync(Entry("old", now.AddDays(-8)), default);
        await store.AppendAsync(Entry("new", now.AddDays(-6)), default);
        var svc = new RetentionCleanupService(store, new Monitor(new SearchLogOptions { RetentionDays = 7 }), new TestServices.FakeTimeProvider(now), NullLogger<RetentionCleanupService>.Instance);
        Assert.Equal(1, await svc.RunOnceAsync(default));
        Assert.Equal(1, await store.CountAsync(default));

        var keepForever = new RetentionCleanupService(store, new Monitor(new SearchLogOptions { RetentionDays = 0 }), new TestServices.FakeTimeProvider(now.AddYears(5)), NullLogger<RetentionCleanupService>.Instance);
        Assert.Equal(0, await keepForever.RunOnceAsync(default));
    }

    [Fact]
    public async Task File_database_is_created_with_migrations_and_reopened()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dorksmith-log-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "state", "test.db");
        try
        {
            using (var store = Create(path))
                await store.AppendAsync(Entry("x", DateTimeOffset.UtcNow), default);
            Assert.True(File.Exists(path));
            using (var reopened = Create(path))
            {
                Assert.Equal(1, await reopened.CountAsync(default));
                Assert.True(await reopened.PingAsync(default));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Chrome/120", "browser")]
    [InlineData("curl/8.10.1", "cli")]
    [InlineData("python-requests/2.31", "cli")]
    [InlineData("Googlebot/2.1", "bot")]
    [InlineData("SomethingElse/1.0", "other")]
    [InlineData("", null)]
    public void User_agent_family_is_coarse(string ua, string? expected)
        => Assert.Equal(expected, SearchLogWriter.UserAgentFamily(ua));

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class Monitor(SearchLogOptions value) : IOptionsMonitor<SearchLogOptions>
    {
        public SearchLogOptions CurrentValue => value;
        public SearchLogOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<SearchLogOptions, string?> listener) => null;
    }
}
