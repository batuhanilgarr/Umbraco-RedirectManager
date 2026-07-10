# Valid From / Valid Until (Scheduled Redirects) — Design

## Context

This is sub-project 2 of a 9-part roadmap for BT.RedirectManager, drawn from
`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the "appsettings
config" item, which the user chose not to pursue):

1. Query string koruma (preserve query string) (done — v1.7.0+ branch merged)
2. **Geçerlilik tarihleri (valid from / until)** (this spec)
3. Basit wildcard (`*`) eşleşme
4. Audit alanları (CreatedBy / ModifiedBy)
5. Health check endpoint
6. Unit / entegrasyon testleri
7. Çakışma / duplicate uyarısı
8. Rate limiting
9. Culture / çoklu site kapsamı (multi-site scoping)

Each sub-project gets its own spec → plan → implementation cycle. This
document covers only sub-project 2.

## Problem

Editors running time-boxed campaigns or temporary page moves have no way to
schedule a redirect rule to start or stop automatically. Today they must
manually toggle `IsActive` on the exact day, or leave a stale rule active
indefinitely.

## Design

### Schema

`RedirectEntry` (`Models/RedirectEntry.cs`) gains two new nullable columns:

```csharp
[Column("ValidFrom")]
[NullSetting(NullSetting = NullSettings.Null)]
public DateTime? ValidFrom { get; set; }

[Column("ValidUntil")]
[NullSetting(NullSetting = NullSettings.Null)]
public DateTime? ValidUntil { get; set; }
```

Both default to `NULL` (unbounded) — fully backward compatible, matching
the existing nullable-`DateTime?` pattern used by `LastHitDate`. Stored as
UTC, consistent with every other `DateTime` in this entity
(`CreatedDate`/`UpdatedDate`/`LastHitDate` are all `DateTime.UtcNow`).

### Migration

New step `AddValidityWindowColumns` in
`Migrations/RedirectManagerMigrationPlan.cs`, following the exact existing
pattern (`#if NET10_0_OR_GREATER` / `#else` split, `TableExists` guard, one
`ColumnExists` guard per column, new GUID registered in `DefinePlan()`).

### Matching semantics

A rule is only eligible to match a request when **all** of the following
hold: `IsActive == true` AND (`ValidFrom IS NULL OR ValidFrom <= now`) AND
(`ValidUntil IS NULL OR ValidUntil >= now`). A rule outside its window is
treated identically to `IsActive = false` — it's skipped entirely at match
time, falling through to the next candidate rule (e.g. a global rule when a
domain-scoped one is out of its window) or to Umbraco's normal 404 handling
if nothing else matches. `now` is `DateTime.UtcNow`, passed as a SQL
parameter from C# rather than relying on a DB-specific `NOW()`/`GETUTCDATE()`
function, for portability across the SQL Server and SQLite providers Umbraco
supports.

### Where the filter applies

**Exact-match queries** (`RedirectService.GetByOldUrl`, both the
domain-specific and global branches): add
`AND (ValidFrom IS NULL OR ValidFrom <= @N) AND (ValidUntil IS NULL OR ValidUntil >= @N)`
to the existing `WHERE ... AND IsActive = 1 AND IsRegex = 0` clause, in the
same position `IsActive = 1` already occupies.

**Regex entries** (`RedirectService.GetActiveRegexEntries`): same two
conditions added to the cached query's `WHERE IsActive = 1 AND IsRegex = 1`
clause.

**Known, accepted limitation:** `GetActiveRegexEntries()` is cached in
`IMemoryCache` for 30 seconds. A regex rule crossing its `ValidFrom` or
`ValidUntil` boundary purely due to the clock ticking (not because of an
edit through the API, which already calls `InvalidateRegexCache()`) will
only be picked up on the next cache refresh — up to 30 seconds of staleness
at the boundary. This mirrors the cache's existing staleness characteristics
for any other time-sensitive state and is not a new problem introduced by
this feature.

**NOT touched:**
- `GetAllFiltered`/`GetAll` (used by the dashboard's list view) — these must
  continue returning every row regardless of validity window, so editors can
  see and edit scheduled/expired rules (surfaced via the new "Scheduled"/
  "Expired" badge described below).
- `GetByOldUrlAndIsRegex` (the duplicate-check used by create/update) — a
  rule that hasn't started yet, or has already expired, still occupies its
  `OldUrl`/`Domain` slot for duplicate-detection purposes, exactly as an
  `IsActive = false` rule already does today (this method has never filtered
  on `IsActive` either).
- The `Test` endpoint (`GET .../test?path=`) — left unchanged, consistent
  with the same endpoint being out of scope in the two prior sub-projects.

### API

`CreateRedirectEntryDto`, `UpdateRedirectEntryDto`, and `RedirectEntryDto`
(`Models/RedirectEntryDto.cs`) each gain:

```csharp
public DateTime? ValidFrom { get; set; }
public DateTime? ValidUntil { get; set; }
```

Plain passthrough in `RedirectService.Create`/`Update` and the controller's
`ToDto`, the same way `PreserveQueryString` was wired through in the prior
sub-project. No server-side validation that `ValidUntil >= ValidFrom` — that
check lives client-side only (see UI section), consistent with this
package's existing validation being entirely client-side (e.g. the "Variant
B URL is required" check).

### Dashboard UI

Both dashboards gain two fields in the add/edit modal, placed after the
Domain field: "Valid from" and "Valid until", each an `<input
type="datetime-local">` (Lit) / equivalent AngularJS-bound datetime-local
input (AngularJS dashboard). Both render in the browser's local time zone;
conversion to/from the stored UTC value happens at the JS layer:

- **Populating the field from a loaded redirect:** convert the UTC ISO
  string to a `datetime-local`-compatible local-time string.
- **Saving:** convert the local-time value back to a UTC ISO string before
  sending it in the create/update payload.
- Leaving a field blank means `null` (unbounded on that side) — not
  "now"/"epoch".

This mirrors the existing convention in this codebase where stored
`DateTime` values are UTC and the dashboard already converts for *display*
via `new Date(x).toLocaleString()` (e.g. `lastHitDate`, `firstSeenDate|
lastSeenDate` in the 404 log) — this sub-project extends that same
UTC-stored/local-displayed convention to an *editable* field for the first
time.

**Client-side validation:** if both fields are set and `ValidUntil` is
earlier than `ValidFrom`, block save with an inline error message ("Valid
until must be after Valid from"), same style as the existing "Variant B URL
is required when A/B test is enabled" check in both dashboards' save
functions.

**List badge:** in both dashboards' list table, next to the existing
Active/Inactive status indicator, compute (client-side, from the already
-present `validFrom`/`validUntil` fields, no new DTO field) and render:
- **"Scheduled"** when `validFrom` is set and in the future.
- **"Expired"** when `validUntil` is set and in the past.
- Neither badge when the rule is currently within its window (or has no
  window configured).

Computed against `new Date()` at render time — since this is a live
comparison against the browser's clock rather than a value fetched once
from the server, the badge naturally stays accurate without needing a
re-fetch as time passes (unlike the underlying list data, which is a
point-in-time snapshot until the user refreshes/re-filters).

## Decisions confirmed with user (2026-07-10)

- Out-of-window rules are skipped entirely at match time (treated like
  `IsActive = false`), not converted to an automatic 410.
- Date fields use `datetime-local` (date **and** time), not date-only.
- The dashboard list gets a computed "Scheduled"/"Expired" badge, not just
  raw dates confined to the edit modal.

## Out of scope

- Any server-side validation of `ValidUntil >= ValidFrom` — client-side only,
  matching this package's existing validation style.
- Exposing the validity window in the `Test` endpoint's response.
- Any change to `GetAllFiltered`/`GetAll` — the dashboard list continues to
  show every row regardless of schedule state.
- Any appsettings-level configurability — explicitly excluded from this
  roadmap batch, same as sub-project 1.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step.
