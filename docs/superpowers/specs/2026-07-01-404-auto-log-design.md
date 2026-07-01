# 404 Auto-Log with Redirect Suggestions — Design

## Context

This is sub-project 3 of a 4-part roadmap for BT.RedirectManager:

1. API authorization fix (done)
2. Redirect hit-count analytics (done)
3. **404 auto-log with redirect suggestions** (this spec)
4. Domain/site-scoped redirects

Each sub-project gets its own spec → plan → implementation cycle. All four
are batched into a single `1.3.0` NuGet release. This document covers only
sub-project 3.

## Problem

Editors have no visibility into real 404s hitting their site. Broken
inbound links, old bookmarks, and content that moved without a redirect
rule all produce silent 404s today. We want to log genuine 404 responses
and let editors turn frequent ones into redirects with one click.

## Design correction made during brainstorming

The initial framing was "log any request that doesn't match a redirect
rule." That is wrong: not matching a redirect rule is the normal case for
the vast majority of legitimate page requests (a request for a real,
existing page never matches a redirect rule either). Logging on
"no redirect match" would flood the log with normal traffic.

The correct trigger is the actual downstream HTTP status code. In
`RedirectMiddleware.InvokeAsync` (`Middleware/RedirectMiddleware.cs`), the
method already ends with `await _next(context);` as its last statement when
nothing matched. The fix is to inspect `context.Response.StatusCode` **after**
that call completes — Umbraco's own routing/content-resolution pipeline runs
during `_next(context)` and will have set a real `404` if nothing (no page,
no redirect, no static file) resolved the request. Only log when the
response is genuinely a 404.

### Volume/abuse profile differs from hit-count analytics

Sub-project 2's `IRedirectHitTracker` is keyed by a small, bounded set of
existing redirect IDs — its in-memory dictionary can never grow past the
number of redirect rules that exist. A 404 tracker keyed by request path has
no such bound: a URL-scanning bot can generate unlimited distinct garbage
paths. This shapes two design decisions below (a cap on distinct paths
tracked between flushes, and a retention/cleanup policy for the DB table).

## Design

### Schema

New table, `RedirectManagerMissedRequests` (new model,
`Models/MissedRequest.cs`, following `RedirectEntry.cs`'s NPoco attribute
style):

```csharp
[TableName(MissedRequest.TableName)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class MissedRequest
{
    public const string TableName = "RedirectManagerMissedRequests";

    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Path")]
    [Length(2048)]
    public string Path { get; set; } = string.Empty;

    [Column("Domain")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? Domain { get; set; }

    [Column("HitCount")]
    [Constraint(Default = 1)]
    public int HitCount { get; set; } = 1;

    [Column("FirstSeenDate")]
    public DateTime FirstSeenDate { get; set; } = DateTime.UtcNow;

    [Column("LastSeenDate")]
    public DateTime LastSeenDate { get; set; } = DateTime.UtcNow;
}
```

`Path` is length-capped at 2048 (matches `RedirectEntry.OldUrl`/`NewUrl`) —
anything longer is truncated before tracking, since pathological/garbage
URLs from bots are the primary source of oversized paths. `Domain` is
nullable and unused by this sub-project — it exists now so sub-project 4
(domain-scoped redirects) doesn't need a second migration touching this
table. Only the request **path** is logged, not the query string: query
strings (especially cache-busting params) would otherwise turn one logical
broken URL into many distinct rows, defeating the point of aggregation.

### Migration

New step `CreateMissedRequestsTable` in
`Migrations/RedirectManagerMigrationPlan.cs`, following the exact existing
pattern (`#if NET10_0_OR_GREATER` / `#else` split, `TableExists` guard, new
GUID registered in `DefinePlan()`).

### Recording a miss

`RedirectMiddleware` (`Middleware/RedirectMiddleware.cs`), at the very end
of `InvokeAsync`, replaces:

```csharp
await _next(context);
```

with:

```csharp
await _next(context);

if (context.Response.StatusCode == StatusCodes.Status404NotFound && !ShouldSkipRedirect(path))
{
    _missedRequestTracker.RecordMiss(path);
}
```

`path` here is the same lowercased path already computed earlier in the
method (`context.Request.Path.Value?.ToLowerInvariant()`) — no new
normalization logic needed. `ShouldSkipRedirect` (the existing static method
that excludes `/umbraco`, `/api`, `/install`, `/app_plugins`, `/media`,
`/scripts`, `/css`, `/images`, `/fonts`) is reused as-is to avoid logging
static-asset noise, since it's already the exact list of "not a content
page" prefixes this package cares about.

### In-memory tracker with a distinct-path cap

**`IMissedRequestTracker`** (new, `Services/MissedRequestTracker.cs`),
singleton, same shape as `IRedirectHitTracker` from sub-project 2 (a
`ConcurrentDictionary`-backed accumulator drained on a timer), with one
addition: a hard cap on distinct paths tracked per window (**5,000**).

```csharp
public interface IMissedRequestTracker
{
    void RecordMiss(string path);
    IReadOnlyDictionary<string, (int Count, DateTime FirstSeenUtc, DateTime LastSeenUtc)> DrainAll();
}
```

When `RecordMiss` is called for a path not already in the dictionary and the
dictionary is already at the 5,000-entry cap, the call is a no-op (the miss
is silently dropped for that flush window; already-tracked paths keep
incrementing normally). This is **not** a security/DoS mitigation — actual
abuse mitigation belongs at the infrastructure/WAF layer, out of scope for
this package — it exists purely to keep this package's own memory and
database growth bounded under a URL-scanning bot, consistent with treating
this data as best-effort analytics rather than a durable audit log.

### Flush: upsert, not pure update

Unlike `RedirectHitFlushService` (which only ever updates rows that already
exist), a missed path may not have a row yet. **`MissedRequestFlushService`**
(new, `Services/MissedRequestFlushService.cs`, a `BackgroundService` on the
same 30-second interval) uses a try-update-then-insert pattern per drained
path within one `IScopeProvider` scope — safe across every DB provider
Umbraco supports (SQL Server, SQLite), without relying on
provider-specific `UPSERT`/`ON CONFLICT` syntax:

```sql
UPDATE RedirectManagerMissedRequests
SET HitCount = HitCount + @0, LastSeenDate = @1
WHERE Path = @2
```

If the `UPDATE` affects 0 rows, follow with:

```sql
INSERT INTO RedirectManagerMissedRequests (Path, HitCount, FirstSeenDate, LastSeenDate)
VALUES (@0, @1, @2, @2)
```

A narrow race (two instances both getting 0-row updates for a brand-new
path and both inserting) is possible in load-balanced hosting, producing a
duplicate row that self-corrects on the next flush cycle once one row's
`UPDATE` starts matching — acceptable for the same "analytics, not critical
data" reason already established in sub-project 2.

### Retention

The same `MissedRequestFlushService` tracks the last time it ran a cleanup
pass (an in-memory `DateTime` field) and, once per 24 hours (not every 30
seconds), executes:

```sql
DELETE FROM RedirectManagerMissedRequests WHERE LastSeenDate < @0
```

with `@0` = `DateTime.UtcNow.AddDays(-90)`. Confirmed with user: automatic
90-day cleanup, no manual "clear" action needed in this sub-project.

### Dashboard UI

Both dashboards (`App_Plugins/RedirectManager/redirect-dashboard.js` for
Umbraco 17+/18, `App_Plugins/RedirectManager/dashboard.html` +
`redirect.controller.js` for Umbraco 13) gain a second view reached by a
simple tab toggle at the top of the existing page (reusing the page's
existing header/shell/styles, not a new dashboard registration):

- **"Redirects" tab**: the existing table (unchanged).
- **"404 Log" tab**: a new table listing `Path`, `HitCount`, `FirstSeenDate`,
  `LastSeenDate`, sorted by `HitCount` descending, with a **"Create
  Redirect"** button per row that opens the existing create-redirect modal
  with `OldUrl` pre-filled from `Path`.

New API endpoints on `RedirectApiController`:
- `GET /umbraco/api/redirectmanager/missed` — list missed requests (same
  auth as every other endpoint on this controller, inherited from the
  sub-project-1 class-level `[Authorize]`).
- `DELETE /umbraco/api/redirectmanager/missed/{id:int}` — dismiss a single
  entry from the list (needed so editors can clear entries they've decided
  not to act on, without waiting 90 days) — this is the one small addition
  beyond the original ask, included because a list with no way to dismiss
  individual noisy entries (e.g. a bot's one-off garbage path) would be
  frustrating; everything else stays exactly as scoped.

## Decisions confirmed with user (2026-07-01)

- Automatic 90-day retention cleanup, no manual clear button needed.
- Trigger corrected during design: log actual `404` responses (checked
  after `await _next(context)`), not "any unmatched redirect lookup."

## Out of scope

- Any UI/logic for domain-scoping the 404 log by the `Domain` column — that
  column exists now only to avoid a second migration; sub-project 4 will
  design how it's populated and used.
- Rate limiting or IP-based blocking of scanning bots — infrastructure
  concern, not this package's job.
- Exporting the 404 log to CSV (unlike the existing redirect export) — not
  requested.
- A manual "clear all" action — superseded by automatic 90-day retention,
  though per-row dismiss (`DELETE .../missed/{id}`) is included.
