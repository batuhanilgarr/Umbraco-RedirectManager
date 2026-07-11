# Health Check Endpoint — Design

## Context

This is sub-project 5 of a 9-part roadmap for BT.RedirectManager, drawn from
`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the "appsettings
config" item, which the user chose not to pursue):

1. Query string koruma (preserve query string) (done)
2. Geçerlilik tarihleri (valid from / until) (done)
3. Basit wildcard (`*`) eşleşme (done)
4. Audit alanları (CreatedBy / ModifiedBy) (done)
5. **Health check endpoint** (this spec)
6. Unit / entegrasyon testleri
7. Çakışma / duplicate uyarısı
8. Rate limiting
9. Culture / çoklu site kapsamı (multi-site scoping)

Each sub-project gets its own spec → plan → implementation cycle. This
document covers only sub-project 5.

## Problem

There's no way for a site owner/admin to quickly confirm the package's
database table is actually present and reachable — e.g. after a bad
upgrade, a manual DB restore, or a misconfigured connection string. The
original roadmap idea proposed either a custom API endpoint or Umbraco's
built-in health check dashboard; the user chose the latter.

## Design

### Approach: Umbraco's built-in Health Check dashboard

Umbraco CMS has a native backoffice feature at **Settings → Health Check**
that runs a series of pluggable checks and displays their status. A custom
check is a plain class inheriting
`Umbraco.Cms.Core.HealthChecks.HealthCheck`, decorated with
`[HealthCheck(Guid, Name, Description = ..., Group = ...)]`. Umbraco
auto-discovers these via its own type scanning — there is no manual
registration step (no composer wiring, no manifest entry) and no new
NuGet dependency, since `Umbraco.Cms.Core.HealthChecks` is already part of
the `Umbraco.Cms.Core` package this project already references on both
target frameworks (net8.0 → Umbraco 13.9.2, net10.0 → Umbraco 17.1.0+;
confirmed the `HealthCheck`/`HealthCheckAttribute`/`HealthCheckStatus`
types exist unchanged in both).

### New service method: `IRedirectService.CanAccessTable()`

```csharp
bool CanAccessTable();
```

Implemented in `RedirectService` as a lightweight, try/catch-wrapped
`SELECT COUNT(*) FROM RedirectManagerEntries` via the existing
`IScopeProvider`, returning `true` on success and `false` on any
exception (connection failure, missing table, permission issue, etc.).
This keeps all direct database access centralized in `RedirectService`
(the existing sole owner of `IScopeProvider` usage in this package) rather
than having the new health check class open its own scope — consistent
with how every other feature in this codebase reaches the database only
through this one service.

### New class: `Services/RedirectManagerHealthCheck.cs`

```csharp
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

    public override Task<IEnumerable<HealthCheckStatus>> GetStatus()
    {
        var accessible = _redirectService.CanAccessTable();

        var status = new HealthCheckStatus(
            accessible
                ? "The Redirect Manager database table is accessible."
                : "The Redirect Manager database table could not be accessed. This may mean the package's migration hasn't run yet, or the database connection is misconfigured.")
        {
            ResultType = accessible ? StatusResultType.Success : StatusResultType.Error
        };

        return Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { status });
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) =>
        throw new InvalidOperationException("This health check does not support any actions.");
}
```

`ExecuteAction` throws rather than doing anything, since this check
deliberately has no "Fix" button (see decisions below) — no `Actions` are
ever added to the returned `HealthCheckStatus`, so Umbraco's dashboard
never surfaces a button that could call `ExecuteAction` in the first
place; the throw is defensive, matching how a check with no actions would
naturally behave if ever miscalled.

### Text/localization

This package doesn't use `ILocalizedTextService`/`~/Config/Lang/` anywhere
today — every user-facing string across both dashboards and the API is a
hardcoded English literal. This health check's messages follow that same
existing convention (plain English strings directly in the C# code) rather
than introducing a new localization mechanism solely for this one feature.

### Group naming

`Group = "Data Integrity"` — one of Umbraco's own standard/pre-existing
group names already shown in the Health Check dashboard for built-in
checks, so this check appears alongside conceptually similar checks rather
than under an unfamiliar new group.

## Decisions confirmed with user (2026-07-11)

- Scope is narrowly the DB/table-accessibility check from the original
  roadmap idea — not extended to also warn about `MissedRequest` (404 log)
  table size, which was considered and explicitly rejected as out of
  scope for this sub-project.
- No "Fix" action/button — an inaccessible table means a migration hasn't
  run or the DB connection itself is broken, neither of which this
  in-process check can safely or usefully "fix" on the admin's behalf.
  The check only reports status.

## Out of scope

- Any check beyond DB/table accessibility (e.g. row-count warnings,
  configuration checks, orphaned-data checks).
- Any "Fix" action/button.
- Any custom API endpoint (e.g. `/umbraco/api/redirectmanager/health`) —
  superseded by the built-in Health Check dashboard integration.
- Localization of the health check's messages.
- Any appsettings-level configurability.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step.
