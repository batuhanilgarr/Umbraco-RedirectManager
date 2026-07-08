using System.Security.Cryptography;
using System.Text;
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

        lock (FlushCoordinator.Lock)
        {
            // Each path gets its own scope/transaction rather than one shared scope for
            // the whole batch: an upsert race on one path (see UpsertOne) can throw and
            // needs to retry in a fresh scope, and isolating scopes also means a failure
            // on one path can't roll back or swallow every other path in the same window.
            foreach (var (path, miss) in drained)
            {
                try
                {
                    UpsertOne(path, miss);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to flush missed-request count for {Path}", path);
                }
            }

            if (cleanupDue)
            {
                try
                {
                    using var scope = _scopeProvider.CreateScope();
                    scope.Database.Execute(
                        $"DELETE FROM {MissedRequest.TableName} WHERE LastSeenDate < @0",
                        DateTime.UtcNow - RetentionPeriod);
                    scope.Complete();
                    _lastCleanupUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to run missed-request retention cleanup");
                }
            }
        }
    }

    private void UpsertOne(string path, (int Count, DateTime FirstSeenUtc, DateTime LastSeenUtc) miss)
    {
        var truncatedPath = path.Length > MaxPathLength ? path.Substring(0, MaxPathLength) : path;
        var pathHash = ComputeHash(truncatedPath);

        using var scope = _scopeProvider.CreateScope();

        var rowsAffected = scope.Database.Execute(
            $@"UPDATE {MissedRequest.TableName}
               SET HitCount = HitCount + @0, LastSeenDate = @1
               WHERE PathHash = @2",
            miss.Count, miss.LastSeenUtc, pathHash);

        if (rowsAffected == 0)
        {
            try
            {
                scope.Database.Execute(
                    $@"INSERT INTO {MissedRequest.TableName} (Path, PathHash, HitCount, FirstSeenDate, LastSeenDate)
                       VALUES (@0, @1, @2, @3, @4)",
                    truncatedPath, pathHash, miss.Count, miss.FirstSeenUtc, miss.LastSeenUtc);
            }
            catch (Exception)
            {
                // Another instance (or another thread's flush) inserted the same new
                // path between our UPDATE and INSERT. The unique index on PathHash
                // makes this throw instead of silently creating a duplicate row.
                // Retry as an update, in a fresh scope since this scope's transaction
                // may no longer be usable after a failed statement.
                using var retryScope = _scopeProvider.CreateScope();
                retryScope.Database.Execute(
                    $@"UPDATE {MissedRequest.TableName}
                       SET HitCount = HitCount + @0, LastSeenDate = @1
                       WHERE PathHash = @2",
                    miss.Count, miss.LastSeenUtc, pathHash);
                retryScope.Complete();
                return;
            }
        }

        scope.Complete();
    }

    private static string ComputeHash(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
