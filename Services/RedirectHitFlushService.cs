using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NPoco;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectHitFlushService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan HitDailyRetentionPeriod = TimeSpan.FromDays(35);

    private readonly IRedirectHitTracker _hitTracker;
    private readonly IOptionsMonitor<ConnectionStrings> _connectionStrings;
    private readonly ILogger<RedirectHitFlushService> _logger;
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public RedirectHitFlushService(
        IRedirectHitTracker hitTracker,
        IOptionsMonitor<ConnectionStrings> connectionStrings,
        ILogger<RedirectHitFlushService> logger)
    {
        _hitTracker = hitTracker;
        _connectionStrings = connectionStrings;
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
                using var db = FlushDatabaseFactory.Create(_connectionStrings.CurrentValue);
                using var transaction = db.GetTransaction();

                foreach (var (redirectId, hit) in drained)
                {
                    db.Execute(
                        $@"UPDATE {RedirectEntry.TableName}
                           SET HitCount = HitCount + @0,
                               LastHitDate = CASE WHEN LastHitDate IS NULL OR @1 > LastHitDate THEN @1 ELSE LastHitDate END
                           WHERE Id = @2",
                        hit.Count, hit.LastHitUtc, redirectId);

                    UpsertDailyBucket(db, redirectId, hit.Count, today);
                }

                transaction.Complete();
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
                using var db = FlushDatabaseFactory.Create(_connectionStrings.CurrentValue);
                using var transaction = db.GetTransaction();
                db.Execute(
                    $"DELETE FROM {RedirectHitDaily.TableName} WHERE HitDate < @0",
                    DateTime.UtcNow.Date - HitDailyRetentionPeriod);
                transaction.Complete();
                _lastCleanupUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run redirect hit-daily retention cleanup");
            }
        }
    }

    private static void UpsertDailyBucket(Database db, int redirectId, int count, DateTime hitDate)
    {
        var rowsAffected = db.Execute(
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
            db.Execute(
                $@"INSERT INTO {RedirectHitDaily.TableName} (RedirectId, HitDate, HitCount)
                   VALUES (@0, @1, @2)",
                redirectId, hitDate, count);
        }
        catch (Exception)
        {
            // Another instance's flush inserted the same (RedirectId, HitDate)
            // bucket between our UPDATE and INSERT. Retry as an update now
            // that the row exists.
            //
            // Unlike MissedRequestFlushService.UpsertOne's retry (which opens a
            // FRESH standalone Database because that transaction may no longer be
            // usable after a failed statement), this retry deliberately reuses
            // the SAME `db`/transaction passed in from Flush(). The whole
            // flush batch — the entries-table HitCount update and every
            // redirect's daily-bucket upsert — must stay in one transaction so
            // it commits or rolls back together; that's what lets the outer
            // catch in Flush() safely call _hitTracker.MergeBack(drained) for
            // the FULL batch on any failure, not just the item that failed.
            //
            // This relies on SQL Server's default XACT_ABORT OFF session
            // setting: a unique-constraint violation from the INSERT above
            // aborts only that one statement, not the whole transaction, so
            // `db`'s transaction is still usable for this retry UPDATE and
            // for the rest of the batch's statements once the loop continues.
            db.Execute(
                $@"UPDATE {RedirectHitDaily.TableName}
                   SET HitCount = HitCount + @0
                   WHERE RedirectId = @1 AND HitDate = @2",
                count, redirectId, hitDate);
        }
    }
}
