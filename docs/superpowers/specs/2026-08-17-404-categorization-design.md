# 404 Log Categorization (Filter Chips + Bulk-Apply + Auto-Ruleset) — Design

## Context

Feature request received by email from a user of the package. Summary of
their problem: the 404 log mixes a small number of actionable entries into
thousands of scanner-noise rows. There's no way to tag what a 404 is, so
triage is all-or-nothing (create redirect / dismiss) with no memory of
decisions already made. "Ignore" currently hard-deletes the row
(`MissedRequestService.Delete` → SQL `DELETE`), which the user does not want
— they want the decision remembered, not the row erased.

This spec covers the full request in one pass (user explicitly opted out of
deferring bulk-apply or the auto-ruleset to a later version): a `Category`
field on each 404 entry, filter chips with counts, bulk-apply to a filtered
selection, and a simple regex ruleset that auto-tags obvious scanner/asset
noise at ingest time.

Applies to **both** dashboard UIs shipped with the package, since the
package targets Umbraco 13/17/18 (`net8.0`→Umbraco 13's AngularJS backoffice,
`net10.0`→Umbraco 17/18's Lit backoffice): the legacy AngularJS dashboard
(`dashboard.html` + `redirect.controller.js` + `redirect.resource.js`) and
the newer Lit dashboard (`redirect-dashboard.js`).

## Design

### 1. Data model

New column `Category` on `RedirectManagerMissedRequests`
(`Models/MissedRequest.cs`), `nvarchar`, not null, default `"Unclassified"`.
New enum `Models/MissedRequestCategory.cs`:

```csharp
public enum MissedRequestCategory
{
    Unclassified,
    MaliciousScanner,
    MissingAsset,
    RedirectNeeded,
    Gone,
    TypoMalformed,
    NeedsInvestigation
}
```

Stored as its `ToString()` name (readable in the DB, matches the existing
style of other string columns on this table) rather than an int, via a
plain `MissedRequest.Category` string property parsed/formatted at the
service boundary.

Migration: new `AddMissedRequestCategoryColumn` migration class (async +
sync variants, same `#if NET10_0_OR_GREATER` split as every other migration
in the file), following the `AddCultureColumn` pattern exactly — check
`ColumnExists` then `AddColumn<MissedRequest>(MissedRequest.TableName,
"Category")`, backfill existing rows to `"Unclassified"` via a raw
`UPDATE ... WHERE Category IS NULL` (needed because `AddColumn` alone
leaves existing rows null, not defaulted, under NPoco). Registered in
`RedirectManagerPackageMigrationPlan.DefinePlan()` after `AddCultureColumn`
with a new GUID.

### 2. Backend

`Services/MissedRequestService.cs`:

- `SetCategory(int id, MissedRequestCategory category)` — single-row update.
- `BulkSetCategory(IEnumerable<int> ids, MissedRequestCategory category)` —
  single `UPDATE ... WHERE Id IN (...)` (batched, not N single updates).
- `ClassifyOnIngest(string path)` — pure function, no DB access, returns a
  `MissedRequestCategory`. Called from wherever a new `MissedRequest` row is
  first created (the existing miss-recording path), so only **new** 404s
  get auto-tagged. Regex table:
  - `MaliciousScanner`: `\.php$`, `^/wp-`, `/\.env`, `/\.git/`,
    `^/(admin|phpmyadmin|xmlrpc\.php)` — deliberately narrow, matching only
    the patterns named in the request email, not a general WAF ruleset.
  - `MissingAsset`: path ends in `.js`, `.css`, `.map`, `.jpg`, `.jpeg`,
    `.png`, `.gif`, `.svg`, `.webp`, `.ico`, `.woff`, `.woff2`, `.ttf`.
  - Anything else stays `Unclassified` (no attempt to auto-detect
    `RedirectNeeded`/`Gone`/`TypoMalformed`/`NeedsInvestigation` — those
    require human judgment per the request email).
- Existing `Delete(id)` (hard delete) is left in place, untouched, as a
  service-level capability — just no longer wired to any dashboard button.

`Controllers/RedirectApiController.cs` — two new endpoints:

- `PATCH missed/{id}/category` — body `{ category: string }`.
- `PATCH missed/bulk-category` — body `{ ids: int[], category: string }`.

Existing `GET missed` list endpoint is unchanged (already returns full rows
including the new `Category` field via `MissedRequestDto`); category counts
for the filter chips are computed client-side from the already-loaded list,
matching how both dashboards already compute other client-side derived
state (e.g. `filteredMissedRequests`) — no new counts endpoint.

### 3. Frontend — shared behavior (implemented independently in each dashboard)

- **Filter chips** above the 404 table: one per `MissedRequestCategory`
  value, each showing a live count from the currently loaded rows,
  clicking toggles that category into an active-filter set (multi-select —
  e.g. viewing `Unclassified` + `NeedsInvestigation` together). No filter
  active = show all categories. This is an additional filter dimension
  alongside the existing path-search box, combined with AND logic.
- **Per-row category control**: a `<select>`-style dropdown per row
  (replacing the old "Dismiss" button) showing the current category;
  changing it calls `PATCH missed/{id}/category` and updates the row
  in-place.
- **Bulk-apply**: a checkbox column added to the 404 table (select-all
  header checkbox + per-row checkboxes, operating over the
  currently-filtered rows only). When ≥1 row is selected, an action bar
  appears above the table with a category dropdown + "Apply" button, which
  calls `PATCH missed/bulk-category` with the selected ids, then updates
  those rows in-place from the response (no full reload).
- The old `dismissMissedRequest()` / "Dismiss" button and its `DELETE
  missed/{id}` call are removed from both dashboards' 404 table UI.

**Lit (`redirect-dashboard.js`)**: new reactive state
(`missedCategoryFilter: Set`, `selectedMissedIds: Set`), new getters
(`missedCategoryCounts`, extending the existing `filteredMissedRequests`
getter to also apply the category filter), new handlers
(`onCategoryChange`, `onBulkCategoryApply`, `toggleMissedSelection`).

**AngularJS (`redirect.controller.js` + `dashboard.html` +
`redirect.resource.js`)**: equivalent `$scope` state and functions; new
`$http` calls added to `redirect.resource.js` alongside the existing
missed-request methods; template changes in `dashboard.html` for the chip
row, per-row `<select>`, and bulk-apply bar.

### 4. Testing

`Umbraco.RedirectManager.Tests`:

- `MissedRequestServiceTests`: `SetCategory` persists and round-trips;
  `BulkSetCategory` updates exactly the given ids and no others;
  `ClassifyOnIngest` unit tests for each regex bucket (representative
  paths from the request email: `/wp-login.php`, `/.env`, `/.git/config`,
  `/assets/app.js`, `/images/logo.png`, plus a human-meaningful path like
  `/old-product-page` staying `Unclassified`).
- `RedirectApiControllerTests` (if a controller-test pattern already
  exists in this suite — otherwise service-level coverage is sufficient,
  the controller methods are thin pass-throughs).
- `dotnet build` / `dotnet test` run against both `net8.0` and `net10.0`
  target frameworks before calling implementation done.
- No automated UI test (no existing frontend test harness in this repo for
  either dashboard); dashboard changes are verified by code review against
  the existing hand-rolled patterns in each file, consistent with how
  `2026-07-22-table-ux-improvements-design.md` was verified.

## Decisions confirmed with user (2026-08-17)

- Dismiss is fully replaced by category assignment (not kept alongside it)
  — the hard-delete `Delete()`/`DELETE` endpoint stays in the codebase
  unused rather than being removed, since deleting it isn't necessary to
  satisfy the request and keeps the change smaller.
- Bulk-apply is included in this version (not deferred), per explicit
  confirmation — the request email itself was ambiguous on this point
  ("bulk apply can be next step" in the intro, then listed under "Key
  behaviours" as "the core time-saver").
- The auto-ruleset is included in this version (email listed it as
  "Optional later"), scoped narrowly to the exact patterns the email named.
- Auto-classification applies only to newly ingested 404s going forward.
  Existing backlog rows keep whatever category they already have
  (`Unclassified` for all pre-existing rows, backfilled by the migration)
  and are not retroactively reclassified — avoids an unbounded/surprising
  bulk rewrite of historical data as a side effect of a schema migration.

## Out of scope

- Retroactive reclassification of existing 404 rows via the new regex
  ruleset.
- A UI for editing/configuring the regex ruleset — it's fixed in code.
- `410` disposition automation for the "Gone" category (email says "leave
  404, or 410 if supported" — no existing 410-on-404-log wiring in this
  package to hook into; categorizing as `Gone` is purely informational).
- Version bump, git tag, and NuGet/Marketplace publish are handled as a
  separate step after implementation and local verification (per
  [[project_roadmap_batch_release_goal]] convention), and require separate
  explicit confirmation before the tag-push step per
  [[reference_redirectmanager_publish_flow]].
