using Microsoft.Extensions.Hosting;

namespace Umbraco.RedirectManager.Services;

// Whether the site owner has opted in to telemetry — set either from the
// dashboard's first-load consent prompt or its "Anonymous usage data"
// toggle, never from appsettings.json, since asking every site owner to
// hand-edit config to opt in meant effectively nobody would. Stored as a
// plain file (absent = not yet decided, treated as disabled) rather than a
// DB row, so reading it never touches Umbraco's IScopeProvider — this is
// read from RedirectTelemetryPinger, which can be invoked from an
// independently-scheduled BackgroundService where ambient DB scope isn't
// safe (see RedirectHitFlushService for the incident that established that
// constraint).
//
// HasDecided() vs IsEnabled(): the file's absence specifically means "never
// asked" (the dashboard should show the consent prompt), which is distinct
// from an explicit decline (file present, contents "false" — consent was
// asked and refused, never prompt again).
public interface IRedirectTelemetrySettingsStore
{
    bool HasDecided();
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

public class RedirectTelemetrySettingsStore : IRedirectTelemetrySettingsStore
{
    private readonly string _path;

    public RedirectTelemetrySettingsStore(IHostEnvironment hostEnvironment)
    {
        _path = Path.Combine(hostEnvironment.ContentRootPath, "App_Data", "RedirectManagerTelemetry", "enabled.txt");
    }

    public bool HasDecided()
    {
        return File.Exists(_path);
    }

    public bool IsEnabled()
    {
        if (!File.Exists(_path))
        {
            return false;
        }

        return bool.TryParse(File.ReadAllText(_path).Trim(), out var enabled) && enabled;
    }

    public void SetEnabled(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, enabled ? "true" : "false");
    }
}
