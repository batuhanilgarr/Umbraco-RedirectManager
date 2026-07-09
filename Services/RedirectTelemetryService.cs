using Microsoft.Extensions.Hosting;

namespace Umbraco.RedirectManager.Services;

// Periodic trigger for the opt-in telemetry ping — actual send/throttle
// logic lives in RedirectTelemetryPinger (a singleton), shared with the
// dashboard-open trigger in RedirectApiController so both paths respect
// the same 24-hour-per-site throttle.
public class RedirectTelemetryService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IRedirectTelemetryPinger _pinger;

    public RedirectTelemetryService(IRedirectTelemetryPinger pinger)
    {
        _pinger = pinger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            await _pinger.PingIfDueAsync(domain: null, stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
