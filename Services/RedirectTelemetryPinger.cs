using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Configuration;

namespace Umbraco.RedirectManager.Services;

// Opt-in "is this plugin still installed, and where" ping — disabled by
// default, enabled only via the dashboard's toggle (IRedirectTelemetrySettingsStore),
// never via appsettings.json (site owners won't hand-edit config, so an
// appsettings-only opt-in would mean nobody ever actually turns it on).
// When enabled, sends a small payload (a locally-generated, non-identifying
// SiteId; the plugin and Umbraco versions; and the requesting host's domain,
// captured automatically — never typed in by the user) to a self-hosted
// endpoint. No cookies, IPs, page content, or redirect data are ever sent —
// see the README's Telemetry section for the exact payload shape.
//
// A singleton (not a BackgroundService itself) so the 24-hour throttle is
// shared across BOTH triggers that call PingIfDueAsync: the periodic
// background timer (RedirectTelemetryService) and the dashboard's own
// "I was just opened" trigger (RedirectApiController) — whichever fires
// first in a given 24h window sends the ping; the other is a no-op check.
//
// Deliberately does NOT touch Umbraco's IScopeProvider/IKeyValueService —
// this session's other background services (RedirectHitFlushService etc.)
// showed that's not safe from an independently-scheduled BackgroundService,
// and this pinger can be invoked from that same kind of context. The SiteId
// and enabled flag are instead persisted to plain files under App_Data,
// which have no such ambient-state concerns.
public interface IRedirectTelemetryPinger
{
    Task PingIfDueAsync(string? domain, CancellationToken cancellationToken);
}

public class RedirectTelemetryPinger : IRedirectTelemetryPinger
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromHours(24);

    // The maintainer's telemetry collection endpoint. Not user-configurable
    // (site owners only see the on/off toggle) — REDIRECTMANAGER_TELEMETRY_ENDPOINT_OVERRIDE
    // / _APIKEY_OVERRIDE below exist only for the maintainer's own local
    // Docker testing and are undocumented for end users.
    private const string ProductionEndpointUrl = "https://redirectmanager-telemetry.azurewebsites.net/api/ping";
    private const string ProductionApiKey = "62db2a1ff665cab5c5fb2458b3d3dbd719eba57ebda92d2f431a6fc0d094d095";

    private readonly IRedirectTelemetrySettingsStore _settingsStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IUmbracoVersion _umbracoVersion;
    private readonly ILogger<RedirectTelemetryPinger> _logger;
    private DateTime _lastPingUtc = DateTime.MinValue;

    public RedirectTelemetryPinger(
        IRedirectTelemetrySettingsStore settingsStore,
        IHttpClientFactory httpClientFactory,
        IHostEnvironment hostEnvironment,
        IUmbracoVersion umbracoVersion,
        ILogger<RedirectTelemetryPinger> logger)
    {
        _settingsStore = settingsStore;
        _httpClientFactory = httpClientFactory;
        _hostEnvironment = hostEnvironment;
        _umbracoVersion = umbracoVersion;
        _logger = logger;
    }

    public async Task PingIfDueAsync(string? domain, CancellationToken cancellationToken)
    {
        if (!_settingsStore.IsEnabled())
        {
            return;
        }

        if (DateTime.UtcNow - _lastPingUtc < PingInterval)
        {
            return;
        }

        var endpointUrl = Environment.GetEnvironmentVariable("REDIRECTMANAGER_TELEMETRY_ENDPOINT_OVERRIDE") ?? ProductionEndpointUrl;
        var apiKey = Environment.GetEnvironmentVariable("REDIRECTMANAGER_TELEMETRY_APIKEY_OVERRIDE") ?? ProductionApiKey;

        try
        {
            var payload = new
            {
                siteId = GetOrCreateSiteId(),
                domain,
                pluginVersion = GetPluginVersion(),
                umbracoVersion = _umbracoVersion.SemanticVersion.ToString()
            };

            var client = _httpClientFactory.CreateClient(nameof(RedirectTelemetryPinger));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
            {
                Content = JsonContent.Create(payload)
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("X-Api-Key", apiKey);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _lastPingUtc = DateTime.UtcNow;
            }
            else
            {
                _logger.LogWarning("Redirect Manager telemetry ping failed with status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Redirect Manager telemetry ping");
        }
    }

    private string GetOrCreateSiteId()
    {
        var path = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", "RedirectManagerTelemetry", "site-id.txt");

        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (Guid.TryParse(existing, out _))
            {
                return existing;
            }
        }

        var newId = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, newId);
        return newId;
    }

    private static string GetPluginVersion()
    {
        return typeof(RedirectTelemetryPinger).Assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
