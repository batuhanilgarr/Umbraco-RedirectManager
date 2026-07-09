# Update Notification Banner — Design

## Context

The plugin has shipped several releases (currently 1.6.0) and the maintainer
has observed that some installed sites are still running older versions.
NuGet packages can't be remotely force-updated — a site owner must run
`dotnet add package` / restore and redeploy themselves — so the only lever
available is making the fact that a newer version exists impossible to miss
inside the backoffice dashboard, where the site owner is already looking.

Decisions confirmed with user (2026-07-09):

- **Not a hard block.** A persistent, non-dismissible banner — visible every
  time the dashboard is opened while outdated — not a feature lock or a
  full-dashboard block.
- **Single message, no severity tiers.** No "critical vs. normal" update
  distinction; every available update gets the same banner.
- **NuGet.org is the source of truth**, queried directly — not the existing
  opt-in telemetry ping/response, since many sites never opt in to
  telemetry and would never see a banner if it depended on that channel.
- **Checked every 24 hours, cached** — the dashboard never waits on a live
  NuGet.org call; it reads a cached result.
- **Always on for every install, no appsettings toggle.** Unlike the
  telemetry ping, this feature sends no site data anywhere — it only reads
  a public NuGet.org listing — so there's no privacy reason to gate it
  behind opt-in, and the user explicitly does not want a new
  `RedirectManager:*` config section for it. This mirrors how the telemetry
  *ping* itself has no appsettings toggle either (see
  `RedirectTelemetryPinger.cs`'s file-level comment) — the precedent in
  this codebase is "no config section unless there's a real reason for
  one."

## Design

### `IRedirectVersionChecker` / `RedirectVersionChecker`

New singleton service, `Services/RedirectVersionChecker.cs`, structured
exactly like `RedirectTelemetryPinger` (24h in-memory throttle shared by two
callers, `IHttpClientFactory`, file-backed cache instead of
`IScopeProvider`/DB — same rationale as telemetry and the flush services:
not safe to touch ambient Umbraco scope from an independently-scheduled
`BackgroundService`).

```csharp
public interface IRedirectVersionChecker
{
    Task CheckIfDueAsync(CancellationToken cancellationToken);
    UpdateStatus GetStatus();
}

public record UpdateStatus(string CurrentVersion, string? LatestVersion, bool UpdateAvailable, DateTime? CheckedAtUtc);
```

- `CheckIfDueAsync`: if `DateTime.UtcNow - _lastCheckUtc < CheckInterval`
  (24h, `static readonly TimeSpan`, same pattern as
  `RedirectTelemetryPinger.PingInterval`), returns immediately. Otherwise
  queries NuGet.org's Search API:

  ```
  GET https://azuresearch-usnc.nuget.org/query?q=packageid:BT.RedirectManager&prerelease=false
  ```

  which returns the current listed (non-prerelease, non-unlisted) version
  for an exact package-id match — this is deliberately the Search API and
  not the flat-container `.../index.json` endpoint, because flat-container
  lists every version ever pushed including unlisted ones, while Search
  reflects what NuGet actually recommends installing. Parses
  `data[0].version` from the response. On success, writes the cache file
  (below) and updates `_lastCheckUtc`. On any failure (network error,
  non-success status, malformed JSON, empty `data`), logs a `Warning` and
  leaves both the cache file and `_lastCheckUtc` untouched — so it's
  retried on the next hourly tick rather than backing off further, and a
  permanently unreachable NuGet.org (air-gapped install) just means the
  banner never appears, with one warning logged per hourly attempt.

- `GetStatus()`: reads the cache file (fast, synchronous, no network),
  parses `CurrentVersion` via
  `typeof(RedirectVersionChecker).Assembly.GetName().Version` (same
  reflection approach as `RedirectTelemetryPinger.GetPluginVersion()`), and
  compares with `System.Version.TryParse`. If either version fails to
  parse, or the cache file doesn't exist yet (fresh install, first check
  hasn't landed), returns `UpdateAvailable = false` rather than throwing —
  the banner simply doesn't show until a successful check has run.

Cache file: `App_Data/RedirectManagerUpdateCheck/latest-version.json`
(new folder, separate from `App_Data/RedirectManagerTelemetry/` — this
data has nothing to do with the opt-in telemetry feature and shouldn't be
conflated with it), containing:

```json
{ "latestVersion": "1.7.0", "checkedAtUtc": "2026-07-09T12:00:00Z" }
```

### `RedirectVersionCheckService : BackgroundService`

New file, `Services/RedirectVersionCheckService.cs`, identical shape to
`RedirectTelemetryService`: hourly `PeriodicTimer`, each tick calls
`_versionChecker.CheckIfDueAsync(stoppingToken)`, which is a no-op unless
24h have actually elapsed. This means a fresh install gets its first real
check within the first hour of the app running, without waiting a full 24h.

### Controller endpoint

`Controllers/RedirectApiController.cs` gains one endpoint:

```csharp
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
```

Combines the "maybe trigger a refresh" and "read current status" steps in
one `GET`, unlike telemetry's separate `ping` (POST) / `status` (GET) split
— telemetry splits them because the ping *sends data* and needed an
explicit enable/disable gate around it; this endpoint only ever reads a
public listing and re-triggering it on every dashboard load is harmless
(throttled to a no-op by `CheckIfDueAsync` itself), so one endpoint is
simpler and there's nothing to split.

Same `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]` as the
rest of the controller (class-level attribute already covers it).

### Composer registration

In `Composers/RedirectManagerComposer.cs`, alongside the existing telemetry
block:

```csharp
builder.Services.AddSingleton<IRedirectVersionChecker, RedirectVersionChecker>();
builder.Services.AddHostedService<RedirectVersionCheckService>();
```

Reuses the already-registered `builder.Services.AddHttpClient()` — no new
named client needed, `IHttpClientFactory.CreateClient()` (default client)
is enough for a single unauthenticated public GET.

### Frontend — Lit dashboard (Umbraco 17/18)

`App_Plugins/RedirectManager/redirect-dashboard.js`:

- New reactive properties: `updateAvailable`, `currentVersion`,
  `latestVersion`.
- `connectedCallback()` gains a call to a new `loadUpdateStatus()`,
  alongside the existing `loadTelemetryStatus()`/`pingTelemetry()` calls.
- `loadUpdateStatus()` fetches `GET /umbraco/api/redirectmanager/update-status`
  via the existing `authFetch()` helper and sets the three properties from
  the response. Failures are caught and swallowed (`console.error` only) —
  exactly like `loadTelemetryStatus()` — so a failed check never breaks
  the dashboard.
- Render: a slim banner strip, always visible (no close button, no click
  handler that hides it, no persisted "dismissed" state) whenever
  `this.updateAvailable` is true, placed above the existing dashboard
  content — structurally simpler than the telemetry prompt's
  `.modal-overlay` (that one blocks interaction and has two action
  buttons; this one doesn't block anything and has none):

  ```html
  ${this.updateAvailable ? html`
      <div class="update-banner">
          Yeni sürüm mevcut: <strong>${this.latestVersion}</strong>
          (şu an ${this.currentVersion} kullanıyorsunuz).
          <code>dotnet add package BT.RedirectManager --version ${this.latestVersion}</code>
          <a href="https://www.nuget.org/packages/BT.RedirectManager" target="_blank" rel="noopener">NuGet'te görüntüle</a>
      </div>
  ` : ''}
  ```

  Exact copy/wording can be refined during implementation; the binding
  behavior (unconditional render while outdated, no dismiss path) is the
  part that's fixed by this design.

### Frontend — AngularJS dashboard (Umbraco 13)

Same wiring, mirrored: `redirect.controller.js` calls
`GET /umbraco/api/redirectmanager/update-status` on load and exposes
`vm.updateAvailable` / `vm.currentVersion` / `vm.latestVersion`;
`dashboard.html` gets an equivalent `<div class="update-banner" ng-if="vm.updateAvailable">`
block with the same content, following the same pattern as the existing
`ng-if="vm.showTelemetryPrompt"` block but without dismiss buttons.

## Error handling summary

| Failure | Behavior |
|---|---|
| NuGet.org unreachable / timeout | Logged as `Warning`, cache untouched, retried next hourly tick |
| NuGet.org returns non-success status | Same as above |
| Malformed/empty JSON response | Same as above |
| No cache file yet (fresh install, first hour) | `GetStatus()` returns `UpdateAvailable = false`; banner appears once the first successful check lands |
| `CurrentVersion`/`LatestVersion` fails `System.Version.TryParse` | Treated as no update available, not an error |
| Dashboard's own fetch to `update-status` fails | Caught client-side, banner just doesn't render this load |

In every failure path, the dashboard itself is unaffected — worst case is
simply no banner.

## Testing plan

- Unit tests for the version-comparison logic in `RedirectVersionChecker`
  (equal, older, newer, malformed/unparseable input on either side).
- Unit/integration test that a simulated NuGet.org failure (mocked
  `HttpMessageHandler` returning a non-success status or throwing) leaves
  the cache file untouched and doesn't throw out of `CheckIfDueAsync`.
- Manual check: temporarily lower the local build's effective version (or
  point the query at a package id with a known higher published version)
  and confirm the banner renders in both the Lit and AngularJS dashboards,
  with the `dotnet add package` command line showing the correct target
  version.
- Manual check: confirm the banner has no close/dismiss affordance and
  reappears on a full page reload while still outdated.

## Out of scope

- Any config toggle to disable the check (explicitly rejected by user —
  always on for every install).
- Severity tiers / critical-vs-normal distinction in the banner.
- Blocking dashboard functionality or locking features on outdated
  versions.
- Changing how the existing opt-in telemetry ping works — this is a fully
  separate, always-on, non-data-sending feature.
