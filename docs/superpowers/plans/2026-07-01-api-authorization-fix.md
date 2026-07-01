# API Authorization Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lock down every `/umbraco/api/redirectmanager/*` endpoint so only authenticated Umbraco backoffice users can reach them, closing an unauthenticated-access hole that currently allows anyone to read, create, modify, delete, bulk-delete, export, or overwrite (via CSV import) every redirect rule.

**Architecture:** Add a single class-level `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]` attribute to `RedirectApiController`. No new files, no schema changes, no frontend changes — the backoffice dashboard already sends its auth cookie automatically on same-origin requests.

**Tech Stack:** ASP.NET Core (`Microsoft.AspNetCore.Authorization`), Umbraco CMS backoffice authorization (`Umbraco.Cms.Web.Common.Authorization.AuthorizationPolicies`).

Reference spec: `docs/superpowers/specs/2026-07-01-api-authorization-design.md`

---

### Task 1: Add the `[Authorize]` attribute to `RedirectApiController`

**Files:**
- Modify: `Controllers/RedirectApiController.cs:1-13`

- [ ] **Step 1: Edit the using statements and class declaration**

Current content (lines 1-13):

```csharp
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Controllers;

[ApiController]
[Route("umbraco/api/redirectmanager")]
public class RedirectApiController : Controller
```

Replace with:

```csharp
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Route("umbraco/api/redirectmanager")]
public class RedirectApiController : Controller
```

Nothing else in the file changes — every action method inherits the class-level `[Authorize]`.

- [ ] **Step 2: Build both target frameworks to confirm it compiles**

Run:
```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors, for both `net8.0` and `net10.0` (the existing NU1902/NU1903 vulnerability warnings are unrelated and expected to remain).

- [ ] **Step 3: Confirm no other file references `RedirectApiController` in a way that assumes anonymous access**

Run:
```bash
grep -rn "RedirectApiController" --include="*.cs" . | grep -v /obj/
```

Expected output: only `Controllers/RedirectApiController.cs` itself (the class declaration line). If anything else references it, stop and re-read that file before continuing — the plan assumes this controller is not consumed anywhere else in-process.

- [ ] **Step 4: Commit**

```bash
git add Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
fix: require backoffice authentication on redirect manager API

/umbraco/api/redirectmanager/* had no [Authorize] attribute, allowing
unauthenticated read/write/delete/bulk-delete/export/import of every
redirect rule. AuthorizationPolicies.BackOfficeAccess is present under
the same name in Umbraco 13.9.2, 17.1.0, and 18.0.0-rc3, so no
multi-targeting branch is needed.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Manual verification against a real backoffice session — DEFERRED

There is no automated test project in this repo and no runnable Umbraco host
inside it (the `docker/` folder only runs a local BaGet NuGet feed for
package distribution, not a website). The user confirmed on 2026-07-01 that
no local Umbraco test site currently exists to run this against.

**This task is not executed as part of this implementation pass.** The steps
below are documented so the user (or a future pass, once a test site exists)
can run them before this change ships in the batched `1.3.0` release. Build
correctness for this change is instead covered by Task 1 Step 2
(`dotnet build` across both target frameworks) and by the spec/code review
gates in this task's execution.

**Files:** none (verification only, no code changes)

- [ ] **Step 1: Push the built package to the local BaGet feed**

```bash
docker compose -f docker/docker-compose.yml up -d
./scripts/push-to-feed.sh
```

Expected: script ends with `==> Tamamlandı. Paket sunucuda.` and no errors.

- [ ] **Step 2: Install/update the package in a test Umbraco site**

In a separate Umbraco 13, 17, or 18 site project pointed at the local feed
(`http://localhost:5555/v3/index.json`), run:

```bash
dotnet add package BT.RedirectManager --source http://localhost:5555/v3/index.json
```

Then run that site (`dotnet run`) so it's reachable, e.g., at
`https://localhost:44300`.

- [ ] **Step 3: Confirm unauthenticated requests are rejected**

With the test site running and **without** logging into the backoffice in
the browser used for curl (no cookies attached):

```bash
curl -i https://localhost:44300/umbraco/api/redirectmanager/getall -k
```

Expected: `HTTP/1.1 401 Unauthorized` (previously this returned `200 OK`
with the full redirect list).

- [ ] **Step 4: Confirm authenticated backoffice usage still works**

Log into the test site's Umbraco backoffice in a browser, open the Redirect
Manager dashboard, and confirm:
- The redirect list loads (`getall`).
- Creating a new redirect works (`create`).
- Editing an existing redirect works (`update/{id}`).
- The test-path tool returns a match (`test`).
- Bulk activate/deactivate/delete on selected rows works (`bulk/*`).
- CSV export downloads a file (`export`).
- CSV import processes a file (`import`).

Expected: all of the above succeed exactly as before this change — the only
behavior change is rejecting unauthenticated requests.

- [ ] **Step 5: Repeat Steps 3-4 against a second test site on the other major Umbraco version**

If Step 2-4 were run against a 13.x site, repeat against a 17.x or 18.x site
(or vice versa), since the package multi-targets `net8.0` (Umbraco 13) and
`net10.0` (Umbraco 17/18) and `AuthorizationPolicies.BackOfficeAccess` must
behave identically on both.

---

### Task 3: Push the fix commit (no release yet)

The user has decided to batch all 4 roadmap sub-projects into a single
`1.3.0` release published after the last one lands, rather than publishing
after each fix. This plan therefore stops at pushing the commit — no version
bump, no tag, no NuGet publish here.

**Files:** none

- [ ] **Step 1: Push the Task 1 commit to `main`**

```bash
git push origin main
```

- [ ] **Step 2: Confirm CI (if any) is green**

```bash
gh run list --limit 3
```

Expected: no failing runs triggered by this push. (`publish-nuget.yml` only
triggers on `v*.*.*` tags or manual dispatch, so this push alone will not
publish anything — confirmed by design in this cycle.)

---

## Out of scope for this plan

- Redirect hit-count analytics, 404 auto-log, and domain-scoped redirects —
  each is a separate sub-project with its own spec + plan, to follow this one.
- Version bump, git tag, and NuGet publish — deferred until all 4 sub-projects
  are complete, then released together as `1.3.0` (per user decision on
  2026-07-01, superseding the per-fix release pattern used for 1.2.33).
- Adding an automated test project to the repo — noted as a gap in the spec,
  not addressed here.
- Restricting access to a specific role/section beyond "any authenticated
  backoffice user" — explicitly declined during design.
