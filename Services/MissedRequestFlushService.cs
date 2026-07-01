using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class MissedRequestFlushService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);
    private const int MaxPathLength = 2048;

    private readonly IMissedRequestTracker _tracker;
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<MissedRequestFlushService> _logger;
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public MissedRequestFlushService(
        IMissedRequestTracker tracker,
        IScopeProvider scopeProvider,
        ILogger<MissedRequestFlushService> logger)
    {
        _tracker = tracker;
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            Flush();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Flush();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Flush()
    {
        var drained = _tracker.DrainAll();
        var cleanupDue = DateTime.UtcNow - _lastCleanupUtc >= CleanupInterval;

        if (drained.Count == 0 && !cleanupDue)
        {
            return;
        }

        try
        {
            using var scope = _scopeProvider.CreateScope();

            foreach (var (path, miss) in drained)
            {
                var truncatedPath = path.Length > MaxPathLength ? path.Substring(0, MaxPathLength) : path;

                var rowsAffected = scope.Database.Execute(
                    $@"UPDATE {MissedRequest.TableName}
                       SET HitCount = HitCount + @0, LastSeenDate = @1
                       WHERE Path = @2",
                    miss.Count, miss.LastSeenUtc, truncatedPath);

                if (rowsAffected == 0)
                {
                    scope.Database.Execute(
                        $@"INSERT INTO {MissedRequest.TableName} (Path, HitCount, FirstSeenDate, LastSeenDate)
                           VALUES (@0, @1, @2, @3)",
                        truncatedPath, miss.Count, miss.FirstSeenUtc, miss.LastSeenUtc);
                }
            }

            if (cleanupDue)
            {
                scope.Database.Execute(
                    $"DELETE FROM {MissedRequest.TableName} WHERE LastSeenDate < @0",
                    DateTime.UtcNow - RetentionPeriod);
                _lastCleanupUtc = DateTime.UtcNow;
            }

            scope.Complete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush missed-request log for {Count} path(s)", drained.Count);
        }
    }
}
