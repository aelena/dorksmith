using Dorksmith.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Logging;

/// <summary>Deletes search-log rows older than the configured retention, at startup and then periodically.</summary>
public sealed class RetentionCleanupService(ISearchLogStore store, IOptionsMonitor<SearchLogOptions> options, TimeProvider time, ILogger<RetentionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnRanToCompletion);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.CleanupIntervalMinutes)), time, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var days = options.CurrentValue.RetentionDays;
        if (days <= 0) return 0;
        var cutoff = time.GetUtcNow().AddDays(-days);
        try
        {
            var removed = await store.PurgeOlderThanAsync(cutoff, ct);
            if (removed > 0) logger.LogInformation("Search-log retention: removed {Count} entries older than {Cutoff:O}", removed, cutoff);
            return removed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Search-log retention cleanup failed");
            return 0;
        }
    }
}
