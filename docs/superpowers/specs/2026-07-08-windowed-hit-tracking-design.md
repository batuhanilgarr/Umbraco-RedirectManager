# Windowed Hit Tracking (7-day / 30-day) — Design

## Context

Customer feedback (Gregor, 2026-07-08 email): the existing all-time `HitCount` on
each redirect (see `2026-07-01-hit-count-analytics-design.md`) doesn't tell an
editor whether a rule is still in active use *recently*. Gregor's stated need is
two-fold:

1. Find redirects that have gone quiet in the last 7/30 days, so temporary
   redirects (kept only until all old app versions that link into them are
   updated) can be retired.
2. Find redirects that aren't firing when they should — a sign of
   misconfiguration — by seeing near-zero recent activity on a rule that
   should be getting traffic.

The original hit-count-analytics spec explicitly deferred this
("Historical/time-series hit data ... would be a separate, larger feature").
This spec covers that separate feature, scoped to exactly what's needed:
7-day and 30-day rolling totals, not a full per-event history.

Out of scope per user decision (2026-07-08): storing redirect configuration
as Umbraco content ("Element Library") instead of the current DB table. The
customer raised this only as a hypothetical alternative, not a firm request,
and it is not being pursued.

## Incidental finding folded into this work

While verifying the existing hit-count feature against the customer's report
of "hits stay at 0," we traced a real (if narrow) reliability gap: on a
`RedirectHitFlushService.Flush()` failure (confirmed reproducible via a
transient SQL connection-pool issue during a cold-started SQL Server
container), the drained in-memory batch is logged as a warning and then
discarded — permanently losing that batch's hits with no retry. This spec
folds in a fix alongside the new daily-bucket table, since both touch the
same flush path.

## Design

### Schema: `RedirectManagerHitDaily`

New table, `Models/RedirectHitDaily.cs`:

```csharp
[TableName(RedirectHitDaily.TableName)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class RedirectHitDaily
{
    public const string TableName = "RedirectManagerHitDaily";

    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("Id")]
    public int Id { get; set; }

    [Column("RedirectId")]
    public int RedirectId { get; set; }

    [Column("HitDate")]
    public DateTime HitDate { get; set; }

    [Column("HitCount")]
    [Constraint(Default = 0)]
    public int HitCount { get; set; } = 0;
}
```

`HitDate` stores a UTC date with the time component zeroed (`DateTime.UtcNow.Date`
at flush time — see below). A unique index on `(RedirectId, HitDate)` backs the
upsert, following the same pattern as `MissedRequest.PathHash`'s unique index
(exact NPoco attribute syntax for a composite index to be confirmed during
implementation — `RedirectId`+`HitDate` are small fixed-width columns, so the
SQL Server index-key-size concern that forced `MissedRequest` to hash its
`Path` column doesn't apply here).

No relation to `RedirectManagerEntries` is declared at the DB level
(no FK), consistent with how the rest of this schema is modeled.

### Migration

New step in `Migrations/RedirectManagerMigrationPlan.cs`
(`CreateRedirectHitDailyTable`), following the existing
`AsyncMigrationBase`/`MigrationBase` `#if NET10_0_OR_GREATER` split, guarded
by `TableExists(RedirectHitDaily.TableName) == false`, registered in
`DefinePlan()` with a new GUID.

### Flush integration

`RedirectHitFlushService.Flush()` already drains
`(redirectId, Count, LastHitUtc)` tuples every 30 seconds. Extend the same
transaction/scope to also upsert the daily bucket:

```sql
UPDATE RedirectManagerHitDaily
SET HitCount = HitCount + @0
WHERE RedirectId = @1 AND HitDate = @2
```

falling back to an `INSERT` when the `UPDATE` affects 0 rows (same
"UPDATE-then-INSERT-on-zero-rows" upsert pattern already used in
`MissedRequestFlushService.UpsertOne`). All redirects drained in one flush
pass use the same `DateTime.UtcNow.Date` bucket — the flush interval is 30
seconds, so a batch spanning a midnight boundary is a negligible edge case
(at most a few seconds of one redirect's hits landing in the "wrong" day).

This SQL runs in the same scope/transaction as the existing
`RedirectManagerEntries.HitCount` update, so both succeed or both roll back
together — no risk of the two counters (all-time vs. daily) drifting apart
due to a partial failure.

### Flush failure hardening (the incidental fix)

`IRedirectHitTracker` gets one new method:

```csharp
void MergeBack(IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> data);
```

Implemented via the same `AddOrUpdate` pattern as `RecordHit`, but adding a
whole `(Count, LastHitUtc)` pair at once: on collision with an accumulator
that already has new hits (recorded *during* the failed flush attempt), sum
the counts and keep the later of the two `LastHitUtc` values.

`RedirectHitFlushService.Flush()`'s `catch` block calls
`_hitTracker.MergeBack(drained)` before logging the warning, instead of
letting `drained` fall on the floor. Since the whole batch runs in one
transaction (see above), a failure means *nothing* in the batch was
committed — merging the entire drained snapshot back is safe and doesn't
double-count anything. The next successful flush (30 seconds later, or
whenever the transient DB issue clears) picks up the merged-back data
along with anything recorded in between.

This is the same treatment `RedirectHitFlushService.StopAsync`'s
best-effort final flush gets — on shutdown, a `MergeBack` failure just means
the process is exiting anyway, so it's a no-op in practice, but it keeps the
two code paths consistent rather than special-casing shutdown.

### Retention

`RedirectHitFlushService` gains the same cleanup-on-interval pattern already
used in `MissedRequestFlushService` (`_lastCleanupUtc` field, 24-hour
`CleanupInterval`, checked/run inside `Flush()`):

```sql
DELETE FROM RedirectManagerHitDaily WHERE HitDate < @0
```

with a 35-day retention window (30-day reporting need + a small buffer).
Shorter than `MissedRequest`'s 90-day retention since this table's use case
only ever needs a 30-day lookback — no reason to keep more.

### API / DTO changes

`Models/RedirectEntryDto.cs`: add `Hits7d` and `Hits30d` (both `int`).

`Services/IRedirectService.cs` / `RedirectService.cs`: new method

```csharp
IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
```

implemented as one aggregate query over `RedirectManagerHitDaily`:

```sql
SELECT RedirectId,
       SUM(CASE WHEN HitDate >= @0 THEN HitCount ELSE 0 END) AS Last7,
       SUM(CASE WHEN HitDate >= @1 THEN HitCount ELSE 0 END) AS Last30
FROM RedirectManagerHitDaily
WHERE HitDate >= @1
GROUP BY RedirectId
```

(`@0` = 7-day cutoff, `@1` = 30-day cutoff — a single scan since 7-day rows
are a subset of the 30-day range). Called once per `GetAll`/`GetAllFiltered`
request in `RedirectApiController`, then merged into the DTO list by
`RedirectId` (redirects with no matching row default to `0`/`0`) — no N+1
queries.

### Dashboard UI

Both dashboards get two new columns next to the existing all-time "Hits"
column:

- Lit dashboard (`App_Plugins/RedirectManager/redirect-dashboard.js`,
  Umbraco 17/18): `<th>7d</th>` / `<th>30d</th>`, rendering
  `redirect.hits7d` / `redirect.hits30d`.
- AngularJS dashboard (`App_Plugins/RedirectManager/dashboard.html`,
  Umbraco 13): same two columns in the existing `ng-repeat` row.

No sorting or filtering by these columns in this round (confirmed with
user) — plain numeric display only, consistent with how the all-time `Hits`
column already behaves.

## Decisions confirmed with user (2026-07-08)

- Scope: 7-day and 30-day windows only, daily-bucket granularity (not
  per-event, not hourly).
- Element Library / content-tree storage idea from the customer email: not
  pursued, no design or reply note needed.
- Dashboard: numeric columns only, no sort/filter by recency in this round.
- Flush-failure data loss (found while investigating the customer's "hits
  stay at 0" report) gets fixed as part of this same work, since it shares
  the flush code path.

## Out of scope

- Sorting/filtering the dashboard by 7d/30d hits (e.g. a "0 hits in 30
  days" quick filter) — flagged as a natural follow-up but not built now.
- Per-event/hourly hit history.
- Any change to how the all-time `HitCount`/`LastHitDate` columns work.
- Element Library / content-as-config-store storage model.
