# Health Check Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let site owners confirm from Umbraco's built-in Settings → Health Check dashboard that the Redirect Manager database table exists and is queryable — no new API endpoint, no new NuGet dependency.

**Architecture:** Add `IRedirectService.CanAccessTable()` (a lightweight, try/catch-wrapped `SELECT COUNT(*)` via the existing `IScopeProvider`, consistent with every other DB access in this package being centralized in `RedirectService`). Add a new `RedirectManagerHealthCheck` class inheriting `Umbraco.Cms.Core.HealthChecks.HealthCheck`, decorated with `[HealthCheck(...)]`. Umbraco auto-discovers this class via its own type scanning — no composer registration, no manifest entry needed.

**Tech Stack:** `Umbraco.Cms.Core.HealthChecks` (already part of the `Umbraco.Cms.Core` package this project references on both target frameworks — no new dependency), NPoco via `IScopeProvider` (unchanged).

Reference spec: `docs/superpowers/specs/2026-07-11-health-check-design.md`

This is sub-project 5 of 9 in the current roadmap batch. No version bump/release happens here — that is a separate step once all 9 sub-projects are done.

---

### Task 1: Add `CanAccessTable()` to the service layer

**Files:**
- Modify: `Services/IRedirectService.cs`
- Modify: `Services/RedirectService.cs`

Both files are changed together in this task (rather than split across two tasks) because an interface method without its implementation would leave the project in a non-building state — each task in this plan should leave the project building cleanly on its own.

- [ ] **Step 1: Add the method signature to `IRedirectService`**

Current (full file):

```csharp
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectService
{
    IEnumerable<RedirectEntry> GetAll();
    IEnumerable<RedirectEntry> GetAllFiltered(string? query, int? statusCode, bool? isActive, bool? isRegex);
    RedirectEntry? GetById(int id);
    RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null);
    RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null);
    IEnumerable<RedirectEntry> GetActiveRegexEntries();
    IEnumerable<RedirectEntry> GetActiveWildcardEntries();
    RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
}
```

Replace with:

```csharp
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectService
{
    IEnumerable<RedirectEntry> GetAll();
    IEnumerable<RedirectEntry> GetAllFiltered(string? query, int? statusCode, bool? isActive, bool? isRegex);
    RedirectEntry? GetById(int id);
    RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null);
    RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null);
    IEnumerable<RedirectEntry> GetActiveRegexEntries();
    IEnumerable<RedirectEntry> GetActiveWildcardEntries();
    RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
    bool CanAccessTable();
}
```

- [ ] **Step 2: Add the implementation to `RedirectService`, right after `GetHitWindowCounts`/`HitWindowRow`**

Current:

```csharp
    private sealed class HitWindowRow
    {
        public int RedirectId { get; set; }
        public int Last7 { get; set; }
        public int Last30 { get; set; }
    }

    public RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName)
```

Replace with:

```csharp
    private sealed class HitWindowRow
    {
        public int RedirectId { get; set; }
        public int Last7 { get; set; }
        public int Last30 { get; set; }
    }

    // Used by RedirectManagerHealthCheck (Umbraco's Settings > Health Check
    // dashboard) to confirm the package's table exists and is queryable --
    // e.g. after a bad upgrade, a manual DB restore, or a misconfigured
    // connection string. Deliberately swallows every exception and reports
    // false rather than letting the health check dashboard itself fault,
    // since any DB-level failure (missing table, broken connection, missing
    // permissions) all mean the same thing here: "not accessible."
    public bool CanAccessTable()
    {
        try
        {
            using var scope = _scopeProvider.CreateScope();
            scope.Database.ExecuteScalar<int>($"SELECT COUNT(*) FROM {RedirectEntry.TableName}");
            scope.Complete();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName)
```

- [ ] **Step 3: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit**

```bash
git add Services/IRedirectService.cs Services/RedirectService.cs
git commit -m "$(cat <<'EOF'
feat: add CanAccessTable to IRedirectService for the upcoming health check

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add the `RedirectManagerHealthCheck` class

**Correction found while implementing this task (2026-07-11):** the first
implementer subagent discovered, and this was independently confirmed via
reflection against the actual referenced assemblies, that
`Umbraco.Cms.Core.HealthChecks.HealthCheck`'s member contract is NOT
identical between the two Umbraco versions this package targets:

- **net8.0 / Umbraco 13.9.2:** `abstract Task<IEnumerable<HealthCheckStatus>> GetStatus()` and `abstract HealthCheckStatus ExecuteAction(HealthCheckAction)` — both must be overridden, exactly as the original spec assumed.
- **net10.0 / Umbraco 17.1.0:** `GetStatus()` no longer exists at all. It's replaced by `virtual Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()` (not abstract). `ExecuteAction(HealthCheckAction)` still exists but is now `virtual` (not abstract) rather than required, and a new `virtual Task<HealthCheckStatus> ExecuteActionAsync(HealthCheckAction)` also exists.

A single non-conditional class body cannot satisfy both TFMs (overriding a
method that doesn't exist on one TFM is a compile error, not a warning).
The fix is a `#if NET10_0_OR_GREATER` / `#else` split around just the
`GetStatus`/`GetStatusAsync` override — the same multi-targeting
convention this codebase already uses in
`Migrations/RedirectManagerMigrationPlan.cs` for its own
sync/async-API-shape differences between these two Umbraco versions. The
status-building logic is factored into a small private `BuildStatus()`
helper shared by both conditional overrides, so the actual check logic
isn't duplicated. `ExecuteAction` needs no `#if` — its signature is
identical (if differently virtual/abstract) on both TFMs, so a single
override compiles and behaves the same either way.

**Files:**
- Create: `Services/RedirectManagerHealthCheck.cs`

- [ ] **Step 1: Write the health check class**

```csharp
using Umbraco.Cms.Core.HealthChecks;

namespace Umbraco.RedirectManager.Services;

[HealthCheck(
    "E7C4A912-6B3D-4F81-9E05-8A2C7D619B34",
    "Redirect table accessible",
    Description = "Confirms the Redirect Manager database table exists and can be queried.",
    Group = "Data Integrity")]
public class RedirectManagerHealthCheck : HealthCheck
{
    private readonly IRedirectService _redirectService;

    public RedirectManagerHealthCheck(IRedirectService redirectService)
    {
        _redirectService = redirectService;
    }

    // Shared by both the net10.0+ GetStatusAsync override and the net8.0
    // GetStatus override below, so the actual check logic exists in exactly
    // one place despite the two TFMs requiring different override names.
    private HealthCheckStatus BuildStatus()
    {
        var accessible = _redirectService.CanAccessTable();

        return new HealthCheckStatus(
            accessible
                ? "The Redirect Manager database table is accessible."
                : "The Redirect Manager database table could not be accessed. This may mean the package's migration hasn't run yet, or the database connection is misconfigured.")
        {
            ResultType = accessible ? StatusResultType.Success : StatusResultType.Error
        };
    }

#if NET10_0_OR_GREATER
    // Umbraco.Cms.Core 17.1.0+'s HealthCheck base class replaced the
    // abstract GetStatus() from 13.9.2 with a virtual GetStatusAsync() --
    // GetStatus() no longer exists to override on this TFM.
    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync() =>
        Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { BuildStatus() });
#else
    public override Task<IEnumerable<HealthCheckStatus>> GetStatus() =>
        Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { BuildStatus() });
#endif

    // This check has no "Fix" action -- an inaccessible table means a
    // migration hasn't run or the DB connection itself is broken, neither of
    // which this in-process check can safely or usefully "fix" on the
    // admin's behalf (see design spec's "Decisions" section). BuildStatus()
    // above never adds any Actions to the returned HealthCheckStatus, so
    // Umbraco's dashboard never renders a button that could call this in the
    // first place; the throw is defensive, matching how a check with no
    // actions would naturally behave if ever miscalled. No #if needed here:
    // ExecuteAction's signature is identical on both TFMs (just virtual
    // rather than abstract on net10.0+), so one override satisfies both.
    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) =>
        throw new InvalidOperationException("This health check does not support any actions.");
}
```

Note: Umbraco auto-discovers `[HealthCheck]`-attributed classes via its own
type scanning at startup — there is no composer registration step and no
`umbraco-package.json`/manifest entry needed for this class to appear on
the Settings → Health Check dashboard.

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Services/RedirectManagerHealthCheck.cs
git commit -m "$(cat <<'EOF'
feat: add RedirectManagerHealthCheck to Umbraco's Settings > Health Check dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Manual verification — DEFERRED (documented, not executed)

Same constraint as every prior sub-project in this repo: no automated test project, no runnable Umbraco host in this repo, no local test site currently available. This documents what to run manually before this sub-project is considered done.

**Files:** none

- [ ] **Step 1 (deferred): Push to the local BaGet feed and install into a test site**

```bash
docker compose -f docker/docker-compose.yml up -d
./scripts/push-to-feed.sh
```

Then update the package in a test Umbraco site and start it.

- [ ] **Step 2 (deferred): Confirm the check appears on the dashboard**

Log into the backoffice, go to **Settings → Health Check**, and confirm a
"Data Integrity" group section shows a "Redirect table accessible" check
with a Success status (green) on a normal, healthy install.

- [ ] **Step 3 (deferred): Confirm the check reports failure when the table is genuinely inaccessible**

Temporarily rename or drop the `RedirectManagerEntries` table directly in
the database (or point the connection string at a database that doesn't
have it), reload the Health Check dashboard, and confirm the check now
shows an Error status with the "could not be accessed" message. Restore
the table/connection string afterward.

- [ ] **Step 4 (deferred): Confirm there is no "Fix" button**

On both the healthy and unhealthy states from Steps 2-3, confirm the
dashboard shows no rectify/fix button for this specific check (unlike
some of Umbraco's own built-in checks, which do have one).

---

## Out of scope for this plan

- Any check beyond DB/table accessibility.
- Any "Fix" action/button.
- Any custom API endpoint (e.g. `/umbraco/api/redirectmanager/health`).
- Localization of the health check's messages.
- Any appsettings-level configurability.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step outside this plan.
