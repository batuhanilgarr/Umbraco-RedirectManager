# Audit Fields (CreatedBy / ModifiedBy) — Design

## Context

This is sub-project 4 of a 9-part roadmap for BT.RedirectManager, drawn from
`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the "appsettings
config" item, which the user chose not to pursue):

1. Query string koruma (preserve query string) (done)
2. Geçerlilik tarihleri (valid from / until) (done)
3. Basit wildcard (`*`) eşleşme (done)
4. **Audit alanları (CreatedBy / ModifiedBy)** (this spec)
5. Health check endpoint
6. Unit / entegrasyon testleri
7. Çakışma / duplicate uyarısı
8. Rate limiting
9. Culture / çoklu site kapsamı (multi-site scoping)

Each sub-project gets its own spec → plan → implementation cycle. This
document covers only sub-project 4.

## Problem

Editors and site owners running SEO/compliance audits have no way to see
who created or last changed a redirect rule. All existing package history
is anonymous.

## Design

### Schema

`RedirectEntry` (`Models/RedirectEntry.cs`) gains two new nullable string
columns, following the existing `Domain`/`Description` style:

```csharp
[Column("CreatedBy")]
[NullSetting(NullSetting = NullSettings.Null)]
[Length(255)]
public string? CreatedBy { get; set; }

[Column("ModifiedBy")]
[NullSetting(NullSetting = NullSettings.Null)]
[Length(255)]
public string? ModifiedBy { get; set; }
```

Both default to `NULL` — fully backward compatible; existing rows show no
audit trail rather than a fabricated one.

### Migration

New step `AddAuditFieldColumns` in
`Migrations/RedirectManagerMigrationPlan.cs`, following the exact existing
pattern (`#if NET10_0_OR_GREATER` / `#else` split, `TableExists` guard, one
`ColumnExists` guard per column, new GUID registered in `DefinePlan()`).

### Where the acting user's name comes from

`RedirectApiController` gains a new constructor dependency,
`IBackOfficeSecurityAccessor` (`Umbraco.Cms.Core.Security`), already
available in both target Umbraco versions (13.9.2 and 17.1.0+). A private
helper resolves the current backoffice user's display name:

```csharp
private string? GetCurrentUserName() =>
    _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Name;
```

This is called once per relevant controller action and passed down as a
plain method parameter — **not** read from the request body — to the
service layer. Audit data must reflect who is actually authenticated on
the request, not whatever a client claims; since every endpoint on this
controller is already gated by
`[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]`, this value
is expected to be non-null on every real request, but the code treats it
as nullable defensively (e.g. a future non-interactive/system caller).

### Service layer signature changes

`IRedirectService`/`RedirectService` methods that mutate rows each gain a
new trailing parameter for the acting user's name:

```csharp
RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName);
RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName);
int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName);
```

`Delete`/`BulkDelete` are unchanged — a deleted row has no audit trail to
update.

**`Create`:** both `CreatedBy` and `ModifiedBy` are set to `actorName` (a
freshly created row's "last modified by" is trivially "whoever created
it").

**`Update`:** only `ModifiedBy` is set to `actorName`; `CreatedBy` is left
untouched (read from the existing row, never overwritten).

**`BulkSetActive`:** sets `ModifiedBy = actorName` for every affected row,
in the same `UPDATE ... SET IsActive = @0, UpdatedDate = @1 WHERE Id IN
(...)` statement that already updates `UpdatedDate` — consistent with the
principle that this bulk action is itself a modification.

### CSV import

`RedirectApiController.ImportCsv` already calls `_redirectService.Create`
and `_redirectService.Update` internally for each row. It resolves
`GetCurrentUserName()` once before the row loop and passes it through to
every `Create`/`Update` call in that loop — the person who ran the import
is recorded as the actor for every row it touches, with no special-casing
needed beyond passing the two now-required parameters.

### API

`RedirectEntryDto` (the read/response DTO) gains:

```csharp
public string? CreatedBy { get; set; }
public string? ModifiedBy { get; set; }
```

`CreateRedirectEntryDto`/`UpdateRedirectEntryDto` (request DTOs) do **not**
gain these fields — they are never client-supplied, so there is nothing
for a client to send. `ToDto` passthrough-maps the two new entity fields.

### Dashboard UI

No new list column (the table is already wide). Both dashboards instead
add a `title` attribute to each table row (`<tr title="...">` in AngularJS,
`` <tr title=${...}> `` in Lit) summarizing the audit trail, e.g.:

```
Created by Jane Doe on Jul 1, 2026 · Last modified by John Smith on Jul 8, 2026
```

If `CreatedBy`/`ModifiedBy` is null (pre-existing row from before this
feature), that clause is omitted rather than showing a placeholder like
"Unknown", e.g. just `Created on Jul 1, 2026` or, if both name fields are
null, `Created on Jul 1, 2026 · Last modified on Jul 8, 2026`.

A row-level `title` is deliberately used instead of a per-cell one: this
package's existing `Old URL`/`New URL` cells already carry their own
`title` (showing the untruncated URL on hover, since those cells are
width-capped). Per HTML/browser behavior, a more specific (inner) element's
`title` wins over a less specific (outer) one when both are present at the
hovered point, so hovering directly over Old URL/New URL keeps showing the
existing untruncated-URL tooltip, while hovering any other cell in the row
(Domain, Match, Active, Hits, etc.) shows the new audit tooltip. This
requires no new markup elements and applies uniformly regardless of which
column layout either dashboard uses.

A small helper computes this string in both dashboards (mirroring the
existing `getLastHitTitle`/`getMissedRequestTitle` helper pattern already
used in the Lit dashboard, and following the equivalent `vm.get*` naming
convention already used in the AngularJS controller for helpers like
`vm.getScheduleBadge`).

## Decisions confirmed with user (2026-07-10)

- Store the backoffice user's display **name** (string), not their numeric
  user ID — simpler, survives user deletion, no extra lookup needed to
  render it.
- CSV import records the importing user as the actor for every
  created/updated row, using the same `Create`/`Update` code path (and
  therefore the same audit semantics) as the dashboard's Add/Edit modal.
- `BulkSetActive` updates `ModifiedBy`, consistent with it already updating
  `UpdatedDate`.
- No new dashboard table column — audit info surfaces via a row-level
  `title` tooltip instead, to avoid widening an already-wide table.
- All dashboard-facing strings (tooltip text, any future UI copy) are in
  English, matching this package's existing UI convention on both
  dashboards.

## Out of scope

- Any UI to browse/filter by `CreatedBy`/`ModifiedBy` (e.g. a "created by"
  filter dropdown) — only the tooltip display described above.
- Any change to `Delete`/`BulkDelete` (no audit trail needed for removed
  rows).
- Any change to `GetByOldUrlAndIsRegex` (duplicate-check) or
  `GetAllFiltered`/`GetAll`'s query shape beyond the two new columns simply
  being included in `SELECT *`.
- Any change to the `Test` endpoint.
- Falling back to a numeric user ID display if the name is somehow
  unavailable — the tooltip simply omits the name clause in that case.
- Any appsettings-level configurability.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step.
