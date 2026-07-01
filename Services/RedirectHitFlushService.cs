using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectHitFlushService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    private readonly IRedirectHitTracker _hitTracker;
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<RedirectHitFlushService> _logger;

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
        if (drained.Count == 0)
        {
            return;
        }

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
            }

            scope.Complete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush redirect hit counts for {Count} redirect(s)", drained.Count);
        }
    }
}
