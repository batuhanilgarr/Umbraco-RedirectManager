# Table UX Improvements (Sticky Header, Sorting, 404→Redirect Row Removal) — Design

## Context

Feature request received by email from a user of the package, covering three
independent UX papercuts across the dashboard tables:

1. Frozen/sticky table headers so column titles stay visible while scrolling
   deep down a long list.
2. Click-to-sort columns.
3. In the 404/missed-requests table, when a redirect is created for a 404
   row, that row should be reflected as resolved (in this design: removed
   from the list immediately).

This spec covers all three, applied consistently across **both** dashboard
UIs shipped with the package: the legacy AngularJS dashboard
(`App_Plugins/RedirectManager/dashboard.html` + `redirect.controller.js` +
`redirect.css`) and the newer Lit-based dashboard
(`App_Plugins/RedirectManager/redirect-dashboard.js`, Umbraco 15+ backoffice
extension). Both dashboards currently render the same 4 hand-rolled
`<table>`s (no `uui-table`/`umb-table` component in use): redirects list,
404/missed-requests log, top-redirects stats, stale-redirects stats. Neither
dashboard has any existing sort or sticky-header behavior; the `MissedRequestDto`
has no "already has a redirect" indicator.

## Design

### 1. Sticky header

Add `position: sticky; top: 0` (plus a background color, since sticky cells
need an opaque background to occlude scrolled content) to the `th` rule in
both stylesheets:

- Lit: inside the component's `static styles = css\`...\`` block in
  `redirect-dashboard.js`, on the existing `th` selector.
- AngularJS: in `redirect.css`, on the existing `.redirect-table th` (or
  equivalent) selector.

Because `border-collapse: collapse` clips sticky cell borders/shadows at the
scroll boundary, both tables switch to `border-collapse: separate;
border-spacing: 0` and keep visual row separation via `border-bottom` on
`td`/`th` (already present) rather than relying on collapsed double borders.
This applies to all 4 tables in each dashboard — one shared CSS rule per
file, no per-table special-casing needed since all 4 use the same
`<table>`/`<th>` structure.

No JS changes for this part.

### 2. Click-to-sort columns

Each sortable `<th>` becomes clickable (cursor pointer, hover affordance)
and shows a small direction indicator (▲ ascending / ▼ descending) only on
the currently active sort column.

**Lit side:** one shared helper added to `redirect-dashboard.js`:

```js
sortRows(rows, column, direction, type) {
  const sign = direction === 'asc' ? 1 : -1;
  return [...rows].sort((a, b) => {
    let av = a[column], bv = b[column];
    if (type === 'date') { av = new Date(av).getTime(); bv = new Date(bv).getTime(); }
    if (type === 'number') { av = Number(av); bv = Number(bv); }
    if (typeof av === 'string') return sign * av.localeCompare(bv);
    return sign * (av > bv ? 1 : av < bv ? -1 : 0);
  });
}
```

Each table gets its own small `{ column, direction }` sort-state field
(e.g. `this.redirectsSortState`, `this.missedSortState`, etc., defaulting to
no active sort = original API order), and its own tiny getter that runs
`sortRows` over the underlying array before rendering
(`get sortedRedirects()`, `get sortedMissedRequests()`, etc.), so the
underlying data arrays themselves are never mutated. Clicking a `th` calls a
shared `onSortClick(stateProp, column, type)` handler: same column clicked
again toggles direction; a different column resets to ascending.

**AngularJS side:** equivalent plain-JS sort function added to
`redirect.controller.js` (same comparison logic, no shared module needed
since AngularJS controllers aren't composed the way Lit components are),
with `$scope.sortState` per table and `ng-click` handlers on each `<th>`
calling a shared `$scope.sortBy(stateProp, column, type)`; templates read
through a `orderBy`-free plain getter/computed array (computed in the
controller, not via Angular's built-in `orderBy` filter, to keep sort
direction/column indicator logic identical to the Lit implementation).

Columns considered "sortable": every column with a well-defined scalar value
per row (URL/path, domain, status code, hit count, dates, active/inactive).
No column is excluded — all columns across all 4 tables become sortable, per
the approved design.

### 3. Removing a 404 row once its redirect is created

No backend or DTO changes. Purely client-side, mirroring the existing
`dismissMissedRequest` local-filter pattern already used in both dashboards:

- **Lit:** in `saveRedirect()` (`redirect-dashboard.js`), after a successful
  create response, compare the created entry's `oldUrl` (domain-aware, same
  normalization already used for comparing paths elsewhere in the file)
  against each item in `this.missedRequests`; any match is filtered out of
  the array before re-render.
- **AngularJS:** identical filter added at the equivalent point in the
  controller's redirect-save success handler.

This only affects the in-memory list for the current session — if the same
path 404s again later, it can reappear in a future load, which is expected
(the 404 log is a raw hit log, not a resolved-state table) and was the
option the user explicitly chose over adding a persisted `HasRedirect` field
on the backend.

## Decisions confirmed with user (2026-07-22)

- 404 rows are removed from the list client-side on redirect creation, not
  flagged with a persisted "has redirect" backend field — avoids a
  `MissedRequestDto`/service change for a cosmetic, session-scoped concern.
- All columns in all 4 tables become sortable (no column exclusions).
- Sorting is client-side only (data is already fully loaded per tab; no
  backend `sort`/`order` query param added).

## Out of scope

- Persisting "has redirect" state across page reloads/backend queries.
- Server-side sorting or pagination.
- Multi-column sort.
- Any change to `uui-table`/`umb-table` — both dashboards keep their
  existing hand-rolled `<table>` markup.
- Version bump, git tag, and NuGet/Marketplace publish are handled as a
  separate step after implementation and local verification (per
  [[project_roadmap_batch_release_goal]] convention already used for this
  package's other recent sub-projects).
