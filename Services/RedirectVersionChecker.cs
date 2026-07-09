using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Umbraco.RedirectManager.Services;

// Always-on "is a newer version published?" check against NuGet.org's public
// Search API — no site data is sent, only a public package listing is read,
// so (unlike the opt-in telemetry ping) this has no on/off toggle: every
// install checks, and the dashboard shows a persistent, non-dismissible
// banner when outdated. See
// docs/superpowers/specs/2026-07-09-update-notification-design.md.
//
// A singleton (not a BackgroundService itself) so the 24-hour throttle is
// shared across BOTH triggers that call CheckIfDueAsync: the periodic
// background timer (RedirectVersionCheckService) and the dashboard's own
// "I was just opened" trigger (RedirectApiController.GetUpdateStatus).
//
// Deliberately does NOT touch Umbraco's IScopeProvider/IKeyValueService —
// same rationale as RedirectTelemetryPinger: not safe to touch ambient
// Umbraco scope from an independently-scheduled BackgroundService. The
// cached result is instead persisted to a plain file under App_Data.
public interface IRedirectVersionChecker
{
    Task CheckIfDueAsync(CancellationToken cancellationToken);
    UpdateStatus GetStatus();
}

public record UpdateStatus(string CurrentVersion, string? LatestVersion, bool UpdateAvailable, DateTime? CheckedAtUtc);

public class RedirectVersionChecker : IRedirectVersionChecker
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    // NuGet.org's public Search API, filtered to an exact package-id match
    // with prerelease excluded. Used instead of the flat-container
    // .../index.json endpoint because Search reflects the current *listed*
    // version (what NuGet actually recommends), while flat-container lists
    // every version ever pushed, including unlisted/deprecated ones.
    private const string SearchApiUrl = "https://azuresearch-usnc.nuget.org/query?q=packageid:BT.RedirectManager&prerelease=false";

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<RedirectVersionChecker> _logger;
    private DateTime _lastCheckUtc = DateTime.MinValue;

    public RedirectVersionChecker(
        IHttpClientFactory httpClientFactory,
        IHostEnvironment hostEnvironment,
        ILogger<RedirectVersionChecker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task CheckIfDueAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastCheckUtc < CheckInterval)
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(RedirectVersionChecker));
            using var response = await client.GetAsync(SearchApiUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Redirect Manager update check failed with status {StatusCode}", response.StatusCode);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                _logger.LogWarning("Redirect Manager update check: NuGet search returned no results for BT.RedirectManager");
                return;
            }

            var latestVersion = data[0].GetProperty("version").GetString();
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return;
            }

            WriteCache(latestVersion);
            _lastCheckUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for a newer Redirect Manager version");
        }
    }

    public UpdateStatus GetStatus()
    {
        var currentVersion = GetPluginVersion();
        var cache = ReadCache();

        if (cache == null || string.IsNullOrWhiteSpace(cache.LatestVersion))
        {
            return new UpdateStatus(currentVersion, null, false, cache?.CheckedAtUtc);
        }

        var updateAvailable = Version.TryParse(currentVersion, out var current)
            && Version.TryParse(cache.LatestVersion, out var latest)
            && latest > current;

        return new UpdateStatus(currentVersion, cache.LatestVersion, updateAvailable, cache.CheckedAtUtc);
    }

    private string GetCachePath()
    {
        return Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", "RedirectManagerUpdateCheck", "latest-version.json");
    }

    private void WriteCache(string latestVersion)
    {
        var path = GetCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new VersionCheckCache(latestVersion, DateTime.UtcNow), CacheJsonOptions);
        File.WriteAllText(path, json);
    }

    private VersionCheckCache? ReadCache()
    {
        var path = GetCachePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<VersionCheckCache>(json, CacheJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Redirect Manager update-check cache");
            return null;
        }
    }

    // AssemblyVersion is always 4-part (e.g. 1.6.0.0 for a csproj <Version>1.6.0</Version>),
    // while NuGet versions here are 3-part — truncate so System.Version comparison
    // against the NuGet-reported LatestVersion lines up exactly.
    private static string GetPluginVersion()
    {
        var version = typeof(RedirectVersionChecker).Assembly.GetName().Version;
        return version == null ? "0.0.0" : new Version(version.Major, version.Minor, version.Build).ToString();
    }

    private record VersionCheckCache(string LatestVersion, DateTime CheckedAtUtc);
}
