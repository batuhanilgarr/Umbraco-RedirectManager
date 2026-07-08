using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectHitFlushService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan HitDailyRetentionPeriod = TimeSpan.FromDays(35);

    private readonly IRedirectHitTracker _hitTracker;
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<RedirectHitFlushService> _logger;
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public RedirectHitFlushService(
        IRedirectHitTracker hitTracker,
        IScopeProvider scopeProvider,
        ILogger<RedirectHitFlushService> logger)
    {
        _hitTracker = hitTracker;
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
        var drained = _hitTracker.DrainAll();
        var cleanupDue = DateTime.UtcNow - _lastCleanupUtc >= CleanupInterval;

        if (drained.Count == 0 && !cleanupDue)
        {
            return;
        }

        if (drained.Count > 0)
        {
            var today = DateTime.UtcNow.Date;

            try
            {
                using var scope = _scopeProvider.CreateScope();

                foreach (var (redirectId, hit) in drained)
                {
                    scope.Database.Execute(
                        $@"UPDATE {RedirectEntry.TableName}
                           SET HitCount = HitCount + @0,
                               LastHitDate = CASE WHEN LastHitDate IS NULL OR @1 > LastHitDate THEN @1 ELSE LastHitDate END
                           WHERE Id = @2",
                        hit.Count, hit.LastHitUtc, redirectId);

                    UpsertDailyBucket(scope, redirectId, hit.Count, today);
                }

                scope.Complete();
            }
            catch (Exception ex)
            {
                // Whole batch shares one transaction, so on any failure none of
                // it committed — safe to merge the entire drained snapshot back
                // into the tracker for the next flush attempt to retry.
                _hitTracker.MergeBack(drained);
                _logger.LogWarning(ex, "Failed to flush redirect hit counts for {Count} redirect(s)", drained.Count);
            }
        }

        if (cleanupDue)
        {
            try
            {
                using var scope = _scopeProvider.CreateScope();
                scope.Database.Execute(
                    $"DELETE FROM {RedirectHitDaily.TableName} WHERE HitDate < @0",
                    DateTime.UtcNow.Date - HitDailyRetentionPeriod);
                scope.Complete();
                _lastCleanupUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run redirect hit-daily retention cleanup");
            }
        }
    }

    private static void UpsertDailyBucket(IScope scope, int redirectId, int count, DateTime hitDate)
    {
        var rowsAffected = scope.Database.Execute(
            $@"UPDATE {RedirectHitDaily.TableName}
               SET HitCount = HitCount + @0
               WHERE RedirectId = @1 AND HitDate = @2",
            count, redirectId, hitDate);

        if (rowsAffected > 0)
        {
            return;
        }

        try
        {
            scope.Database.Execute(
                $@"INSERT INTO {RedirectHitDaily.TableName} (RedirectId, HitDate, HitCount)
                   VALUES (@0, @1, @2)",
                redirectId, hitDate, count);
        }
        catch (Exception)
        {
            // Another instance's flush inserted the same (RedirectId, HitDate)
            // bucket between our UPDATE and INSERT. Retry as an update now
            // that the row exists (same race-recovery pattern as
            // MissedRequestFlushService.UpsertOne).
            scope.Database.Execute(
                $@"UPDATE {RedirectHitDaily.TableName}
                   SET HitCount = HitCount + @0
                   WHERE RedirectId = @1 AND HitDate = @2",
                count, redirectId, hitDate);
        }
    }
}
