using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

// Mirrors RedirectHitFlushService's shape (standalone Database via
// FlushDatabaseFactory, in-memory tracker + periodic flush, MergeBack on
// failure) but stays fully independent of it — flushes only
// VariantBHitCount/VariantBLastHitDate for A/B-test redirects.
public class VariantBHitFlushService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    private readonly IVariantBHitTracker _hitTracker;
    private readonly IOptionsMonitor<ConnectionStrings> _connectionStrings;
    private readonly ILogger<VariantBHitFlushService> _logger;

    public VariantBHitFlushService(
        IVariantBHitTracker hitTracker,
        IOptionsMonitor<ConnectionStrings> connectionStrings,
        ILogger<VariantBHitFlushService> logger)
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
        if (drained.Count == 0)
        {
            return;
        }

        try
        {
            using var db = FlushDatabaseFactory.Create(_connectionStrings.CurrentValue);
            using var transaction = db.GetTransaction();

            foreach (var (redirectId, hit) in drained)
            {
                db.Execute(
                    $@"UPDATE {RedirectEntry.TableName}
                       SET VariantBHitCount = VariantBHitCount + @0,
                           VariantBLastHitDate = CASE WHEN VariantBLastHitDate IS NULL OR @1 > VariantBLastHitDate THEN @1 ELSE VariantBLastHitDate END
                       WHERE Id = @2",
                    hit.Count, hit.LastHitUtc, redirectId);
            }

            transaction.Complete();
        }
        catch (Exception ex)
        {
            _hitTracker.MergeBack(drained);
            _logger.LogWarning(ex, "Failed to flush variant-B hit counts for {Count} redirect(s)", drained.Count);
        }
    }
}
