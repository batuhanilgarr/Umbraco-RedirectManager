# Redirect Hit-Count Analytics — Design

## Context

This is sub-project 2 of a 4-part roadmap for BT.RedirectManager:

1. API authorization fix (done — see `2026-07-01-api-authorization-design.md`)
2. **Redirect hit-count analytics** (this spec)
3. 404 auto-log with redirect suggestions
4. Domain/site-scoped redirects

Each sub-project gets its own spec → plan → implementation cycle. All four
are batched into a single `1.3.0` NuGet release once complete. This document
covers only sub-project 2.

## Problem

Once a redirect rule exists, there is no way to tell whether it is actually
being used. Editors accumulate rules over years with no signal for which
ones are dead weight versus high-traffic. We want a per-redirect hit count
and last-hit timestamp, visible in the dashboard, added without slowing down
`RedirectMiddleware` — which currently runs on every single incoming HTTP
request and does a synchronous DB-scoped read for exact-match lookups.

## Design

### Schema

Add two columns to `RedirectManagerEntries` (`Models/RedirectEntry.cs`):

```csharp
[Column("HitCount")]
[Constraint(Default = 0)]
public int HitCount { get; set; } = 0;

[Column("LastHitDate")]
[NullSetting(NullSetting = NullSettings.Null)]
public DateTime? LastHitDate { get; set; }
```

`LastHitDate` is nullable — a rule that has never fired has no last-hit
value. `HitCount` defaults to 0.

### Migration

Follow the existing pattern in `Migrations/RedirectManagerMigrationPlan.cs`
(the `#if NET10_0_OR_GREATER` / `else` split between `AsyncMigrationBase` and
`MigrationBase`, and the `ColumnExists` guard used by
`AddIsRegexAndDescriptionColumns`). Add a new step,
`AddHitCountColumns`, registered in `DefinePlan()` with a new GUID, guarded
by `ColumnExists(RedirectEntry.TableName, "HitCount")` and
`ColumnExists(RedirectEntry.TableName, "LastHitDate")` checks, each adding
its column via `AddColumn<RedirectEntry>(...)` if missing.

### In-memory counter + periodic flush

**`IRedirectHitTracker`** (new, `Services/RedirectHitTracker.cs`), registered
as a **singleton**:

```csharp
public interface IRedirectHitTracker
{
    void RecordHit(int redirectId);
    IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll();
}
```

Backed by `ConcurrentDictionary<int, HitAccumulator>` where `HitAccumulator`
is a small mutable class with an `int Count` field updated via
`Interlocked.Increment` (matches the `int HitCount` column type — no
realistic 30-second window overflows `int`) and a `DateTime LastHitUtc` field
updated with a
plain assignment (a benign race on the timestamp under concurrent hits is
acceptable — worst case is a slightly stale display value, never wrong data
written to the DB, since the flush step takes a `Volatile.Read`/snapshot).

`RecordHit` does a single `ConcurrentDictionary.AddOrUpdate` — no I/O, no
locks beyond what the dictionary needs internally. This is the only thing
called from the hot path.

`DrainAll()` snapshots and removes all current keys in one pass (so hits
arriving during a flush land in freshly-created entries rather than being
lost), and returns what was drained for the caller to persist.

**`RedirectHitFlushService`** (new, `Services/RedirectHitFlushService.cs`), a
`BackgroundService` registered via `AddHostedService`. Every 30 seconds
(matching the existing regex-cache TTL in `RedirectService` for consistency),
it calls `DrainAll()` and, if non-empty, opens one `IScopeProvider` scope and
issues one `UPDATE` per drained redirect ID:

```sql
UPDATE RedirectManagerEntries
SET HitCount = HitCount + @0,
    LastHitDate = CASE WHEN LastHitDate IS NULL OR @1 > LastHitDate THEN @1 ELSE LastHitDate END
WHERE Id = @2
```

Two things this SQL shape buys us:

- `HitCount = HitCount + @delta` is an atomic increment, not an overwrite —
  this makes the design safe for load-balanced/multi-instance hosting, since
  each instance flushes only the delta it personally observed.
- The `CASE` guard on `LastHitDate` prevents an out-of-order flush from an
  older batch (e.g. from a different instance) from regressing a newer
  timestamp.

On `StopAsync` (app shutdown), the service makes one best-effort final flush
attempt. If a flush throws (e.g. DB temporarily unavailable), the exception
is logged and that batch's data is dropped — acceptable for analytics data,
consistent with the "hit count is not critical data" framing already used
for this sub-project.

### Middleware integration

`RedirectMiddleware` (`Middleware/RedirectMiddleware.cs`) takes
`IRedirectHitTracker` as a **constructor** dependency (singleton, like
`ILogger`, not a per-request `InvokeAsync` parameter like the scoped
`IRedirectService`). In each of the four status-code branches (301, 302,
404, 410) — both the exact-match branch and the regex-match branch — call
`_hitTracker.RecordHit(redirect.Id)` immediately before writing the
response. All four status codes count as hits (confirmed with user): the
count answers "how often does this rule fire," not "how much real traffic
did it redirect."

### API / DTO changes

Add `HitCount` and `LastHitDate` to `Models/RedirectEntryDto.cs`. Update the
four places in `Controllers/RedirectApiController.cs` that construct a
`RedirectEntryDto` manually (`GetAll`, `Get`, `Create`, `Update`) to map the
two new fields.

### Dashboard UI

Add a "Hits" column to both dashboards, showing the count with the last-hit
date as a tooltip (or "Never" if null):

- Lit dashboard (`App_Plugins/RedirectManager/redirect-dashboard.js`, used
  on Umbraco 17+/18): new `<th>Hits</th>` next to the existing 9 columns,
  new `<td>` rendering `redirect.hitCount` with a `title` attribute showing
  the formatted `redirect.lastHitDate` or "Never".
- AngularJS dashboard (`App_Plugins/RedirectManager/dashboard.html`, used on
  Umbraco 13): same column added to the existing `ng-repeat` row, using
  Angular's `date` filter for `lastHitDate`.

No sorting by hit count is added (the API's `GetAllFiltered` always orders
by `CreatedDate DESC` today) — out of scope, YAGNI.

### Composer registration

In `Composers/RedirectManagerComposer.cs`, add:

```csharp
builder.Services.AddSingleton<IRedirectHitTracker, RedirectHitTracker>();
builder.Services.AddHostedService<RedirectHitFlushService>();
```

## Decisions confirmed with user (2026-07-01)

- All four status codes (301/302/404/410) count as hits.
- No "reset stats" action in this sub-project — explicitly deferred (YAGNI).
- In-memory counter + periodic background flush chosen over per-request
  fire-and-forget writes or synchronous per-request writes, trading a small
  (≤30s) data-loss window on ungraceful restart for zero added latency and
  no extra per-request DB round-trips.

## Out of scope

- Historical/time-series hit data (e.g. hits per day) — this design only
  tracks a running total and last-hit timestamp, not a log of individual
  hits. A time-series view would be a separate, larger feature.
- Resetting hit stats from the UI.
- Sorting/filtering the dashboard by hit count.
- Configurable flush interval — hardcoded to 30 seconds, matching the
  existing regex-cache TTL constant already in `RedirectService`.
