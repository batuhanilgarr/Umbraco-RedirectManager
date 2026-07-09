# Update Notification Banner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a persistent, non-dismissible banner in both backoffice dashboards (Lit for Umbraco 17/18, AngularJS for Umbraco 13) whenever a newer version of BT.RedirectManager is published on NuGet.org, so site owners see it every time they open the dashboard until they upgrade. Always on for every install — no appsettings toggle.

**Architecture:** A new singleton `RedirectVersionChecker` queries NuGet.org's public Search API for the latest listed version, throttled to once per 24 hours, with the result cached to a JSON file under `App_Data/RedirectManagerUpdateCheck/`. A new `RedirectVersionCheckService` (`BackgroundService`) triggers the check hourly (a no-op unless 24h have actually elapsed — same shape as the existing `RedirectTelemetryService`/`RedirectTelemetryPinger` pair). A new `GET update-status` endpoint on `RedirectApiController` reads the cache and also fires a non-blocking refresh. Both dashboards call this endpoint on load and render an unconditional banner (no close button, no dismissed state) whenever the installed version is behind.

**Tech Stack:** ASP.NET Core (`BackgroundService`, DI, `IHttpClientFactory`), plain JSON file persistence (no DB table — no migration needed), Lit (Umbraco 17+/18 dashboard), AngularJS (Umbraco 13 dashboard).

Reference spec: `docs/superpowers/specs/2026-07-09-update-notification-design.md`

**Files touched:**
- Create: `Services/RedirectVersionChecker.cs`
- Create: `Services/RedirectVersionCheckService.cs`
- Modify: `Composers/RedirectManagerComposer.cs`
- Modify: `Controllers/RedirectApiController.cs`
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js` (Lit)
- Modify: `App_Plugins/RedirectManager/redirect.resource.js`, `redirect.controller.js`, `dashboard.html`, `redirect.css` (AngularJS)
- Modify: `Umbraco.RedirectManager.csproj`, `README.md` (version bump + docs)

---

### Task 1: Add `IRedirectVersionChecker` / `RedirectVersionChecker`

**Files:**
- Create: `Services/RedirectVersionChecker.cs`

- [ ] **Step 1: Write the service**

```csharp
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
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Services/RedirectVersionChecker.cs
git commit -m "$(cat <<'EOF'
feat: add RedirectVersionChecker for NuGet.org update checks

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add `RedirectVersionCheckService` background trigger

**Files:**
- Create: `Services/RedirectVersionCheckService.cs`

- [ ] **Step 1: Write the background service**

```csharp
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
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Services/RedirectVersionCheckService.cs
git commit -m "$(cat <<'EOF'
feat: add hourly background trigger for the update-availability check

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Register the new services in the Composer

**Files:**
- Modify: `Composers/RedirectManagerComposer.cs`

- [ ] **Step 1: Add the registration**

Current (lines 30-33):

```csharp
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<IRedirectTelemetrySettingsStore, RedirectTelemetrySettingsStore>();
        builder.Services.AddSingleton<IRedirectTelemetryPinger, RedirectTelemetryPinger>();
        builder.Services.AddHostedService<RedirectTelemetryService>();
```

Replace with:

```csharp
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<IRedirectTelemetrySettingsStore, RedirectTelemetrySettingsStore>();
        builder.Services.AddSingleton<IRedirectTelemetryPinger, RedirectTelemetryPinger>();
        builder.Services.AddHostedService<RedirectTelemetryService>();

        builder.Services.AddSingleton<IRedirectVersionChecker, RedirectVersionChecker>();
        builder.Services.AddHostedService<RedirectVersionCheckService>();
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Composers/RedirectManagerComposer.cs
git commit -m "$(cat <<'EOF'
feat: register RedirectVersionChecker and its background trigger

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Add the `update-status` API endpoint

**Files:**
- Modify: `Controllers/RedirectApiController.cs`

- [ ] **Step 1: Add the field and constructor parameter**

Current (lines 18-35):

```csharp
    private readonly IRedirectService _redirectService;
    private readonly IMissedRequestService _missedRequestService;
    private readonly IRedirectTelemetryPinger _telemetryPinger;
    private readonly IRedirectTelemetrySettingsStore _telemetrySettingsStore;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectApiController(
        IRedirectService redirectService,
        IMissedRequestService missedRequestService,
        IRedirectTelemetryPinger telemetryPinger,
        IRedirectTelemetrySettingsStore telemetrySettingsStore)
    {
        _redirectService = redirectService;
        _missedRequestService = missedRequestService;
        _telemetryPinger = telemetryPinger;
        _telemetrySettingsStore = telemetrySettingsStore;
    }
```

Replace with:

```csharp
    private readonly IRedirectService _redirectService;
    private readonly IMissedRequestService _missedRequestService;
    private readonly IRedirectTelemetryPinger _telemetryPinger;
    private readonly IRedirectTelemetrySettingsStore _telemetrySettingsStore;
    private readonly IRedirectVersionChecker _versionChecker;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectApiController(
        IRedirectService redirectService,
        IMissedRequestService missedRequestService,
        IRedirectTelemetryPinger telemetryPinger,
        IRedirectTelemetrySettingsStore telemetrySettingsStore,
        IRedirectVersionChecker versionChecker)
    {
        _redirectService = redirectService;
        _missedRequestService = missedRequestService;
        _telemetryPinger = telemetryPinger;
        _telemetrySettingsStore = telemetrySettingsStore;
        _versionChecker = versionChecker;
    }
```

- [ ] **Step 2: Add the endpoint**

Current (lines 68-75), the end of `DisableTelemetry` immediately followed by `GetAll`:

```csharp
    [HttpPost("telemetry/disable")]
    public IActionResult DisableTelemetry()
    {
        _telemetrySettingsStore.SetEnabled(false);
        return Ok(new { enabled = false });
    }

    [HttpGet("getall")]
```

Replace with:

```csharp
    [HttpPost("telemetry/disable")]
    public IActionResult DisableTelemetry()
    {
        _telemetrySettingsStore.SetEnabled(false);
        return Ok(new { enabled = false });
    }

    // Fired by the dashboard on load. Reads the cached update-check result
    // (never blocks on a live NuGet.org call) and also fires a non-blocking
    // refresh via CheckIfDueAsync, which is a no-op unless 24h have elapsed
    // since the last successful check — same throttle used by the hourly
    // background trigger (RedirectVersionCheckService).
    [HttpGet("update-status")]
    public IActionResult GetUpdateStatus()
    {
        _ = _versionChecker.CheckIfDueAsync(CancellationToken.None);
        var status = _versionChecker.GetStatus();

        return Ok(new
        {
            currentVersion = status.CurrentVersion,
            latestVersion = status.LatestVersion,
            updateAvailable = status.UpdateAvailable,
            checkedAtUtc = status.CheckedAtUtc
        });
    }

    [HttpGet("getall")]
```

- [ ] **Step 3: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit**

```bash
git add Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: add GET update-status endpoint

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Wire the banner into the Lit dashboard (Umbraco 17/18)

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add the three new reactive properties**

Current (lines 25-29):

```javascript
        telemetryEnabled: { type: Boolean },
        telemetryLoading: { type: Boolean },
        telemetryDecided: { type: Boolean },
        showTelemetryPrompt: { type: Boolean }
    };
```

Replace with:

```javascript
        telemetryEnabled: { type: Boolean },
        telemetryLoading: { type: Boolean },
        telemetryDecided: { type: Boolean },
        showTelemetryPrompt: { type: Boolean },
        updateAvailable: { type: Boolean },
        currentVersion: { type: String },
        latestVersion: { type: String }
    };
```

- [ ] **Step 2: Add the `.update-banner` style**

Current (lines 231-234):

```javascript
        .notif-success { background: #f0fdf4; border-color: #bbf7d0; color: #166534; }
        .notif-error   { background: #fef2f2; border-color: #fecaca; color: #991b1b; }
        .notif-info    { background: #eff6ff; border-color: #bfdbfe; color: #1e40af; }

```

Replace with:

```javascript
        .notif-success { background: #f0fdf4; border-color: #bbf7d0; color: #166534; }
        .notif-error   { background: #fef2f2; border-color: #fecaca; color: #991b1b; }
        .notif-info    { background: #eff6ff; border-color: #bfdbfe; color: #1e40af; }

        .update-banner {
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: 8px;
            padding: 10px 14px;
            margin-bottom: 14px;
            border-radius: 6px;
            border: 1px solid #c7d2fe;
            background: #eef2ff;
            color: #3730a3;
            font-size: 12px;
        }

        .update-banner code {
            padding: 2px 6px;
            background: #fff;
            border: 1px solid #e0e7ff;
            border-radius: 4px;
            font-size: 11px;
            font-family: 'Monaco', 'Courier New', monospace;
        }

        .update-banner a {
            color: #3730a3;
            font-weight: 600;
            text-decoration: underline;
        }

```

- [ ] **Step 3: Initialize the new properties in the constructor**

Current (lines 637-659):

```javascript
    constructor() {
        super();
        this.redirects = [];
        this.loading = true;
        this.showModal = false;
        this.editingRedirect = null;
        this.formData = this.getEmptyFormData();
        this.query = '';
        this.statusFilter = '';
        this.activeFilter = '';
        this.regexFilter = '';
        this.selectedIds = [];
        this.importInProgress = false;
        this.messageText = '';
        this.messageType = 'info';
        this.activeTab = 'redirects';
        this.missedRequests = [];
        this.missedLoading = false;
        this.telemetryEnabled = false;
        this.telemetryLoading = false;
        this.telemetryDecided = true;
        this.showTelemetryPrompt = false;
    }
```

Replace with:

```javascript
    constructor() {
        super();
        this.redirects = [];
        this.loading = true;
        this.showModal = false;
        this.editingRedirect = null;
        this.formData = this.getEmptyFormData();
        this.query = '';
        this.statusFilter = '';
        this.activeFilter = '';
        this.regexFilter = '';
        this.selectedIds = [];
        this.importInProgress = false;
        this.messageText = '';
        this.messageType = 'info';
        this.activeTab = 'redirects';
        this.missedRequests = [];
        this.missedLoading = false;
        this.telemetryEnabled = false;
        this.telemetryLoading = false;
        this.telemetryDecided = true;
        this.showTelemetryPrompt = false;
        this.updateAvailable = false;
        this.currentVersion = '';
        this.latestVersion = '';
    }
```

- [ ] **Step 4: Call `loadUpdateStatus()` from `connectedCallback` and define it**

Current (lines 730-743):

```javascript
    connectedCallback() {
        super.connectedCallback();
        this.ensureModalStylesLoaded();
        this.loadRedirects();
        this.loadMissedRequests();
        this.loadStats();
        this.loadTelemetryStatus();
        this.pingTelemetry();
    }

    /** Opt-in usage ping (no-op if telemetry is disabled/unconfigured server-side); never blocks dashboard load. */
    pingTelemetry() {
        this.authFetch('/umbraco/api/redirectmanager/telemetry/ping', { method: 'POST' }).catch(() => {});
    }
```

Replace with:

```javascript
    connectedCallback() {
        super.connectedCallback();
        this.ensureModalStylesLoaded();
        this.loadRedirects();
        this.loadMissedRequests();
        this.loadStats();
        this.loadTelemetryStatus();
        this.pingTelemetry();
        this.loadUpdateStatus();
    }

    /** Opt-in usage ping (no-op if telemetry is disabled/unconfigured server-side); never blocks dashboard load. */
    pingTelemetry() {
        this.authFetch('/umbraco/api/redirectmanager/telemetry/ping', { method: 'POST' }).catch(() => {});
    }

    /** Always-on update-availability check (no opt-in — no site data is sent, only a public NuGet.org listing is read). */
    async loadUpdateStatus() {
        try {
            const response = await this.authFetch('/umbraco/api/redirectmanager/update-status');
            if (response.ok) {
                const result = await response.json();
                this.updateAvailable = !!result.updateAvailable;
                this.currentVersion = result.currentVersion || '';
                this.latestVersion = result.latestVersion || '';
            }
        } catch (error) {
            console.error('Failed to load update status:', error);
        }
    }
```

- [ ] **Step 5: Render the banner**

Current (lines 1204-1215), the page header block immediately followed by the status legend comment:

```javascript
            <!-- Page header -->
            <div class="page-header">
                <div>
                    <h1>Redirect Manager</h1>
                    <p>Centrally manage URL redirects for your site.</p>
                </div>
                <button class="btn btn-primary" @click=${() => this.openAddModal()}>
                    + Add redirect
                </button>
            </div>

            <!-- Status legend -->
```

Replace with:

```javascript
            <!-- Page header -->
            <div class="page-header">
                <div>
                    <h1>Redirect Manager</h1>
                    <p>Centrally manage URL redirects for your site.</p>
                </div>
                <button class="btn btn-primary" @click=${() => this.openAddModal()}>
                    + Add redirect
                </button>
            </div>

            <!-- Update-available banner: unconditional while outdated, no dismiss/close affordance -->
            ${this.updateAvailable ? html`
                <div class="update-banner">
                    Yeni sürüm mevcut: <strong>${this.latestVersion}</strong>
                    (şu an ${this.currentVersion} kullanıyorsunuz).
                    <code>dotnet add package BT.RedirectManager --version ${this.latestVersion}</code>
                    <a href="https://www.nuget.org/packages/BT.RedirectManager" target="_blank" rel="noopener">NuGet'te görüntüle</a>
                </div>
            ` : ''}

            <!-- Status legend -->
```

- [ ] **Step 6: Build to confirm it compiles (syntax-level, via the MSBuild copy target)**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` — this copies `App_Plugins` to the output but doesn't type-check JS; a manual visual check happens in Task 8.

- [ ] **Step 7: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "$(cat <<'EOF'
feat: show update-available banner in the Lit dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Wire the banner into the AngularJS dashboard (Umbraco 13)

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect.resource.js`
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`
- Modify: `App_Plugins/RedirectManager/dashboard.html`
- Modify: `App_Plugins/RedirectManager/redirect.css`

- [ ] **Step 1: Add the resource call**

Current (`redirect.resource.js` lines 53-59):

```javascript
            enableTelemetry: function () {
                return $http.post(baseUrl + "telemetry/enable", null);
            },
            disableTelemetry: function () {
                return $http.post(baseUrl + "telemetry/disable", null);
            }
        };
```

Replace with:

```javascript
            enableTelemetry: function () {
                return $http.post(baseUrl + "telemetry/enable", null);
            },
            disableTelemetry: function () {
                return $http.post(baseUrl + "telemetry/disable", null);
            },
            getUpdateStatus: function () {
                return $http.get(baseUrl + "update-status");
            }
        };
```

- [ ] **Step 2: Add controller state and `loadUpdateStatus`**

Current (`redirect.controller.js` lines 17-20):

```javascript
        vm.telemetryEnabled = false;
        vm.telemetryLoading = false;
        vm.telemetryDecided = true;
        vm.showTelemetryPrompt = false;
```

Replace with:

```javascript
        vm.telemetryEnabled = false;
        vm.telemetryLoading = false;
        vm.telemetryDecided = true;
        vm.showTelemetryPrompt = false;
        vm.updateAvailable = false;
        vm.currentVersion = '';
        vm.latestVersion = '';
```

Current (`redirect.controller.js` lines 233-239):

```javascript
        vm.loadTelemetryStatus = function () {
            redirectResource.getTelemetryStatus().then(function (response) {
                vm.telemetryEnabled = !!response.data.enabled;
                vm.telemetryDecided = !!response.data.decided;
                vm.showTelemetryPrompt = !vm.telemetryDecided;
            });
        };
```

Replace with:

```javascript
        vm.loadTelemetryStatus = function () {
            redirectResource.getTelemetryStatus().then(function (response) {
                vm.telemetryEnabled = !!response.data.enabled;
                vm.telemetryDecided = !!response.data.decided;
                vm.showTelemetryPrompt = !vm.telemetryDecided;
            });
        };

        // Always-on update-availability check (no opt-in — no site data is
        // sent, only a public NuGet.org listing is read).
        vm.loadUpdateStatus = function () {
            redirectResource.getUpdateStatus().then(function (response) {
                vm.updateAvailable = !!response.data.updateAvailable;
                vm.currentVersion = response.data.currentVersion || '';
                vm.latestVersion = response.data.latestVersion || '';
            });
        };
```

- [ ] **Step 3: Call `loadUpdateStatus()` alongside the other load calls**

Current (`redirect.controller.js` lines 267-273):

```javascript
        vm.loadRedirects();
        vm.loadMissedRequests();
        vm.loadStats();
        vm.loadTelemetryStatus();

        // Opt-in usage ping (no-op if telemetry is disabled/unconfigured server-side); never blocks dashboard load.
        redirectResource.pingTelemetry().catch(function () { });
```

Replace with:

```javascript
        vm.loadRedirects();
        vm.loadMissedRequests();
        vm.loadStats();
        vm.loadTelemetryStatus();
        vm.loadUpdateStatus();

        // Opt-in usage ping (no-op if telemetry is disabled/unconfigured server-side); never blocks dashboard load.
        redirectResource.pingTelemetry().catch(function () { });
```

- [ ] **Step 4: Render the banner**

Current (`dashboard.html` lines 58-62):

```html
        <umb-box-content>
            <h1 style="margin:0 0 4px;font-size:20px;font-weight:600;color:#1b264f;letter-spacing:-0.01em;">Bitiz Redirect Manager</h1>
            <p style="margin:0 0 16px;font-size:13px;color:#888;line-height:1.5;">Centrally manage URL redirects for your site.</p>

            <!-- Status legend (compact) -->
```

Replace with:

```html
        <umb-box-content>
            <h1 style="margin:0 0 4px;font-size:20px;font-weight:600;color:#1b264f;letter-spacing:-0.01em;">Bitiz Redirect Manager</h1>
            <p style="margin:0 0 16px;font-size:13px;color:#888;line-height:1.5;">Centrally manage URL redirects for your site.</p>

            <!-- Update-available banner: unconditional while outdated, no dismiss/close affordance -->
            <div class="update-banner" ng-if="vm.updateAvailable">
                Yeni sürüm mevcut: <strong>{{vm.latestVersion}}</strong>
                (şu an {{vm.currentVersion}} kullanıyorsunuz).
                <code>dotnet add package BT.RedirectManager --version {{vm.latestVersion}}</code>
                <a href="https://www.nuget.org/packages/BT.RedirectManager" target="_blank" rel="noopener">NuGet'te görüntüle</a>
            </div>

            <!-- Status legend (compact) -->
```

- [ ] **Step 5: Add the `.update-banner` style to the shared CSS file**

Current (end of `redirect.css`):

```css
@media (max-width: 768px) {
    .redirect-status-legend { flex-direction: column; gap: 8px; }
}
```

Replace with:

```css
@media (max-width: 768px) {
    .redirect-status-legend { flex-direction: column; gap: 8px; }
}

.update-banner {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 8px;
    padding: 10px 14px;
    margin-bottom: 14px;
    border-radius: 6px;
    border: 1px solid #c7d2fe;
    background: #eef2ff;
    color: #3730a3;
    font-size: 12px;
}

.update-banner code {
    padding: 2px 6px;
    background: #fff;
    border: 1px solid #e0e7ff;
    border-radius: 4px;
    font-size: 11px;
    font-family: 'Monaco', 'Courier New', monospace;
}

.update-banner a {
    color: #3730a3;
    font-weight: 600;
    text-decoration: underline;
}
```

- [ ] **Step 6: Commit**

```bash
git add App_Plugins/RedirectManager/redirect.resource.js App_Plugins/RedirectManager/redirect.controller.js App_Plugins/RedirectManager/dashboard.html App_Plugins/RedirectManager/redirect.css
git commit -m "$(cat <<'EOF'
feat: show update-available banner in the AngularJS dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Version bump and docs

**Files:**
- Modify: `Umbraco.RedirectManager.csproj`
- Modify: `README.md`

- [ ] **Step 1: Bump the version**

Current (`Umbraco.RedirectManager.csproj` line 11):

```xml
    <Version>1.6.0</Version>
```

Replace with:

```xml
    <Version>1.7.0</Version>
```

- [ ] **Step 2: Document the feature in the README feature list**

Current (`README.md` line 27, immediately before the "Backoffice-secured API" bullet):

```markdown
- **Built-in test tool**: Test a path before saving to confirm which redirect rule will match.
- **Backoffice-secured API**: All redirect-management endpoints require an authenticated Umbraco backoffice session with a valid bearer token.
```

Replace with:

```markdown
- **Built-in test tool**: Test a path before saving to confirm which redirect rule will match.
- **Update notifications**: The dashboard checks NuGet.org once every 24 hours and shows a persistent banner whenever a newer version is available — always on, no configuration, no site data sent.
- **Backoffice-secured API**: All redirect-management endpoints require an authenticated Umbraco backoffice session with a valid bearer token.
```

- [ ] **Step 3: Build to confirm the version-sync MSBuild target still runs cleanly**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
grep '"version"' App_Plugins/RedirectManager/umbraco-package.json
```

Expected: build succeeds, and the `umbraco-package.json` line now reads
`"version": "1.7.0"` (synced automatically by the existing
`UpdateUmbracoPackageVersion` MSBuild target).

- [ ] **Step 4: Commit**

```bash
git add Umbraco.RedirectManager.csproj README.md App_Plugins/RedirectManager/umbraco-package.json
git commit -m "$(cat <<'EOF'
chore: bump to 1.7.0 and document update notification banner

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Manual verification against a live Umbraco site

The local test infrastructure is up (`redirectmanager-nuget` BaGet feed on
`localhost:5555`, `sql2022` SQL Server on `localhost:1533`, test site at
`/Users/bhan/Desktop/u18/MyProject`).

**Files:** none (verification only)

- [ ] **Step 1: Confirm the local NuGet feed and SQL Server containers are up**

```bash
docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Ports}}"
```

Expected: both `redirectmanager-nuget` and `sql2022` listed as running. If
not: `docker compose -f docker/docker-compose.yml up -d` from the repo root.

- [ ] **Step 2: Push the new package version to the local feed**

```bash
./scripts/push-to-feed.sh
```

Expected: build succeeds, pack succeeds, and the script reports the package
was pushed to `http://localhost:5555/v3/index.json`.

- [ ] **Step 3: Update the test site's package reference and restore**

```bash
cd /Users/bhan/Desktop/u18/MyProject
dotnet add package BT.RedirectManager --version 1.7.0
dotnet build -c Debug
```

Expected: restore picks up `1.7.0` from the local feed, build succeeds.

- [ ] **Step 4: Manually pre-seed the update-check cache to force the banner on (since the local feed's "latest version" won't be visible to the real NuGet.org Search API)**

The real NuGet.org Search API only knows about versions actually published
there (currently up to `1.6.0` until Task 9 ships `1.7.0`), so a natural
check against the local test site won't show a real "outdated" state yet.
Confirm the mechanism works end-to-end by writing the cache file directly:

```bash
mkdir -p /Users/bhan/Desktop/u18/MyProject/App_Data/RedirectManagerUpdateCheck
cat > /Users/bhan/Desktop/u18/MyProject/App_Data/RedirectManagerUpdateCheck/latest-version.json <<'EOF'
{"latestVersion":"9.9.9","checkedAtUtc":"2026-07-09T00:00:00Z"}
EOF
```

- [ ] **Step 5: Start the test site and confirm the banner renders**

Start the site (e.g. `dotnet run` from `/Users/bhan/Desktop/u18/MyProject`,
or via the IDE), open the backoffice at **Settings → Redirect Manager**, and
confirm: a persistent, non-closable banner reading "Yeni sürüm mevcut: 9.9.9
(şu an 1.7.0 kullanıyorsunuz)" with the `dotnet add package` command line
and a NuGet link. Reload the page — the banner must reappear (no
dismiss/persisted-hide behavior).

- [ ] **Step 6: Confirm the "no update available" state**

Overwrite the cache with a version equal to the installed one, restart the
site (in-memory `_lastCheckUtc` throttle doesn't matter here since the
endpoint reads the cache file directly):

```bash
cat > /Users/bhan/Desktop/u18/MyProject/App_Data/RedirectManagerUpdateCheck/latest-version.json <<'EOF'
{"latestVersion":"1.7.0","checkedAtUtc":"2026-07-09T00:00:00Z"}
EOF
```

Reload the dashboard. Expected: banner is gone.

- [ ] **Step 7: Confirm resilience when NuGet.org is unreachable**

Delete the cache file entirely and remove any pre-seeded state, then check
the site's logs after a dashboard load — this exercises the real
`CheckIfDueAsync` path hitting the actual NuGet.org Search API:

```bash
rm -f /Users/bhan/Desktop/u18/MyProject/App_Data/RedirectManagerUpdateCheck/latest-version.json
```

Reload the dashboard, then check the newest file in
`/Users/bhan/Desktop/u18/MyProject/umbraco/Logs/` for either a successful
check (no warning, cache file reappears with `"1.6.0"` — the real current
NuGet.org latest at time of testing) or a graceful `Warning`-level log entry
if the container has no outbound internet access. Either way, confirm the
dashboard itself loaded without error.

- [ ] **Step 8: Record the result**

Report back: did all of the above match expectations? Any deviation means
returning to Tasks 1-7 rather than proceeding to Task 9.

---

### Task 9: Publish — NuGet.org and Umbraco Marketplace

**Files:** none (release only)

This step is only executed after explicit go-ahead at the time, since it
pushes a public, effectively irreversible release — confirm with the user
immediately before running Step 2.

- [ ] **Step 1: Confirm working tree is clean and all prior tasks are committed**

```bash
git status --short
git log --oneline -10
```

Expected: no uncommitted changes; the last several commits are the ones from
Tasks 1-7.

- [ ] **Step 2: Tag and push — triggers the existing GitHub Actions publish workflow**

```bash
git tag v1.7.0
git push origin main
git push origin v1.7.0
```

`.github/workflows/publish-nuget.yml` triggers on `v*.*.*` tag pushes: it
builds, packs, and pushes to `https://api.nuget.org/v3/index.json` via NuGet
Trusted Publishing (OIDC) — no manual `dotnet nuget push` to the real
registry needed.

- [ ] **Step 3: Watch the workflow run**

```bash
gh run watch --exit-status
```

(or check the Actions tab on GitHub). Expected: the `Publish to NuGet`
workflow completes successfully.

- [ ] **Step 4: Confirm the package is live on NuGet.org**

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/bt.redirectmanager/index.json"
```

Expected: `"1.7.0"` present in the `versions` array (may take a few minutes
to appear after the workflow finishes).

- [ ] **Step 5: Umbraco Marketplace**

No separate manual publish step exists for this repo —
`umbraco-marketplace.json` is already packed into the `.nupkg` and the
package already carries the `umbraco-marketplace` tag. Marketplace
discovers and updates listings from NuGet.org automatically — once Step 4
confirms `1.7.0` is live, no further action is needed unless the listing
doesn't refresh within a day or two.

---

### Task 10: Deprecate pre-1.7.0 versions on NuGet.org

**Files:** none (nuget.org account action only — cannot be automated from
this repo; requires the user's own authenticated NuGet.org session)

Only run this after Task 9's Step 4 has confirmed `1.7.0` is live. This
makes every older version show a deprecation warning in Visual Studio's
NuGet Package Manager, `dotnet list package --deprecated`, and the
NuGet.org listing page — the complementary channel discussed earlier in
this session for reaching site owners who *do* periodically check their
dependencies (the dashboard banner only reaches people who already upgraded
past 1.6.0).

Versions to deprecate (every version published before this feature ships,
confirmed via `https://api.nuget.org/v3-flatcontainer/bt.redirectmanager/index.json`
on 2026-07-09):

```
1.2.31, 1.2.32, 1.2.33, 1.3.1, 1.4.0, 1.5.0, 1.6.0
```

- [ ] **Step 1: Sign in to nuget.org and open the package's manage page**

Navigate to `https://www.nuget.org/packages/BT.RedirectManager/1.6.0/Manage`
(repeat per version, or use the package's "Manage Package" page, which
lists all versions with a deprecate action per row).

- [ ] **Step 2: For each version listed above, mark it deprecated**

Reason: **Legacy** (this version is outdated). Message: `"Please upgrade to
1.7.0 or later — see https://github.com/batuhanilgarr/Umbraco-RedirectManager
for release notes."` Do not set an "alternate package" (there is no
different replacement package — same package, newer version, which NuGet's
deprecation UI doesn't model directly; the message covers this).

- [ ] **Step 3: Confirm the warning appears**

```bash
dotnet list /Users/bhan/Desktop/u18/MyProject/MyProject.csproj package --deprecated
```

(Only meaningful if that test project is pinned to an old version — this is
a spot-check of the mechanism, not required to pass since the test site is
expected to already be on 1.7.0 from Task 8.) Alternatively, visually
confirm the deprecation banner on
`https://www.nuget.org/packages/BT.RedirectManager/1.6.0`.

- [ ] **Step 4: Report back**

Confirm all 7 versions show the deprecation warning on their NuGet.org
pages.

---

## Out of scope for this plan

- Any appsettings.json config toggle to disable the check — explicitly
  rejected by the user; always on for every install.
- Severity tiers / critical-vs-normal distinction in the banner.
- Blocking dashboard functionality or locking features on outdated
  versions.
- Reaching site owners who never update past a pre-telemetry version and
  never look at NuGet.org/Visual Studio's package manager — no technical
  channel reaches them (discussed and accepted earlier in this session).
- Changing how the existing opt-in telemetry ping works.
