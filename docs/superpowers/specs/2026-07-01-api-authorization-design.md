# API Authorization Fix — Design

## Context

This is sub-project 1 of a 4-part roadmap for BT.RedirectManager:

1. **API authorization fix** (this spec)
2. Redirect hit-count analytics
3. 404 auto-log with redirect suggestions
4. Domain/site-scoped redirects

Each sub-project gets its own spec → plan → implementation cycle. This document
covers only sub-project 1.

## Problem

`RedirectApiController` (`Controllers/RedirectApiController.cs`) has no
`[Authorize]` attribute on any of its 11 endpoints
(`getall`, `get/{id}`, `create`, `update/{id}`, `delete/{id}`, `test`,
`bulk/delete`, `bulk/activate`, `bulk/deactivate`, `export`, `import`), all
routed under `/umbraco/api/redirectmanager`. Anyone who can reach the site can
read, create, modify, delete, or bulk-delete every redirect rule, and export
or overwrite the whole redirect table via CSV import — without ever logging
into the Umbraco backoffice.

## Design

Add a single class-level attribute to `RedirectApiController`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Web.Common.Authorization;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Route("umbraco/api/redirectmanager")]
public class RedirectApiController : Controller
```

`AuthorizationPolicies.BackOfficeAccess` requires any authenticated backoffice
user (no specific role/section restriction). Verified this type exists with
the same name and namespace in `Umbraco.Web.Common.dll` across Umbraco
13.9.2, 17.1.0, and 18.0.0-rc3 — no `#if` / multi-targeting branching needed.

### Why class-level, not per-action

All 11 endpoints are only ever called from the backoffice dashboard; none is
meant to be public. A single attribute covers all current and future actions
on this controller.

### Frontend impact

None. `App_Plugins/RedirectManager/redirect.resource.js` calls
`/umbraco/api/redirectmanager/*` via AngularJS `$http` from within the
backoffice SPA — same-origin requests, so the browser sends the Umbraco
backoffice auth cookie automatically. The CSV export link is a normal
same-origin navigation and behaves the same way. No JS changes required.

### 401 behavior

Umbraco's backoffice AngularJS app already has a global `$http` interceptor
that catches 401 responses and redirects to the login screen, the same way it
does for every other backoffice resource. No custom handling needed in this
package.

### Backward compatibility

This changes behavior for any caller that was hitting these endpoints without
a backoffice session (which was never a supported use case). No schema
changes, no breaking change for legitimate backoffice usage.

## Verification plan

No automated test project exists yet (tracked separately, out of scope here).
Manual verification for the implementation plan:

1. `curl` the `getall` endpoint with no cookies → expect `401`.
2. Log into the backoffice, open the Redirect Manager dashboard, confirm
   list/create/update/delete/test/bulk actions/export/import all still work.
3. Repeat against a site running Umbraco 13, and one running 17 or 18, since
   the package multi-targets both.

## Out of scope

- Per-role/section restriction (e.g., limiting to Administrators only) —
  explicitly declined in favor of "any backoffice user."
- Automated integration tests for this endpoint (no test project exists in
  this repo yet; adding one is a separate concern from this fix).
