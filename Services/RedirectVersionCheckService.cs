using Microsoft.Extensions.Hosting;

namespace Umbraco.RedirectManager.Services;

// Periodic trigger for the always-on update-availability check — actual
// check/throttle logic lives in RedirectVersionChecker (a singleton),
// shared with the dashboard-open trigger in RedirectApiController so both
// paths respect the same 24-hour-per-site throttle.
public class RedirectVersionCheckService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IRedirectVersionChecker _versionChecker;

    public RedirectVersionCheckService(IRedirectVersionChecker versionChecker)
    {
        _versionChecker = versionChecker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            await _versionChecker.CheckIfDueAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
