# Domain/Site-Scoped Redirects — Design

## Context

This is sub-project 4, the last of a 4-part roadmap for BT.RedirectManager:

1. API authorization fix (done)
2. Redirect hit-count analytics (done)
3. 404 auto-log with redirect suggestions (done)
4. **Domain/site-scoped redirects** (this spec)

All four are batched into a single `1.3.0` NuGet release. This document
covers only sub-project 4.

This sub-project is deliberately last because it's the biggest change of the
four — it touches the core lookup logic (`IRedirectService.GetByOldUrl`) and
the API/DTO shape, unlike sub-projects 1-3, which were purely additive.

## Problem

The same `OldUrl` cannot currently map to different `NewUrl` values across
different sites in a multi-site Umbraco install. Every redirect is global.

## Design

### Domain representation: free-text, package-owned

`Domain` is a simple nullable string column on `RedirectEntry`, not an
integration with Umbraco's `IDomainService`/content-node domain bindings.
Rationale: integrating with Umbraco's native domain system would add a
dependency on how a given site's content-node domains and culture bindings
are configured, and would need to resolve the current request into "which
Umbraco site is this" via that infrastructure — real complexity for a
feature whose actual requirement is simpler: "let the same path redirect
differently per hostname." A free-text hostname string, matched directly
against the incoming request's `Host` header, is self-contained, requires
no new dependency, and behaves identically across Umbraco 13/17/18.

`Domain = null` (or empty string) means **global** — matches any incoming
host. This is what every existing redirect has today (the column doesn't
exist yet, so migrating in a nullable column defaults every existing row to
`NULL`), which is exactly the required backward-compatible behavior: no
existing redirect changes behavior after this ships.

### Schema

Add to `Models/RedirectEntry.cs`:

```csharp
[Column("Domain")]
[NullSetting(NullSetting = NullSettings.Null)]
[Length(255)]
public string? Domain { get; set; }
```

New migration step, following the existing pattern in
`Migrations/RedirectManagerMigrationPlan.cs` (`#if NET10_0_OR_GREATER` /
`#else` split, `ColumnExists` guard, new GUID in `DefinePlan()`).

### Domain detection in the middleware

`RedirectMiddleware` reads `context.Request.Host.Value`, lowercases it, and
strips the port if present (e.g. `example.com:8080` → `example.com`). It
does **not** strip a `www.` prefix — confirmed with user: Umbraco sites
typically manage `www` ↔ apex-domain redirection as its own binding/redirect
already, and silently treating them as the same value could surprise users
who expect an exact hostname match. If someone needs both `example.com` and
`www.example.com` scoped the same way, they create two redirect rows.

### Matching precedence: domain-specific beats global

For the same `OldUrl`, a redirect scoped to the requesting domain takes
precedence over a global (`Domain IS NULL`) redirect. Two lookup passes,
domain-specific first:

**Exact match** (`Services/RedirectService.cs`, `GetByOldUrl`), new
signature `GetByOldUrl(string oldUrl, string? domain)`:

1. `SELECT * FROM RedirectManagerEntries WHERE OldUrl = @0 AND Domain = @1 AND IsActive = 1 AND IsRegex = 0` (only run if `domain` is non-empty).
2. If no row, fall back to `SELECT * FROM RedirectManagerEntries WHERE OldUrl = @0 AND (Domain IS NULL OR Domain = '') AND IsActive = 1 AND IsRegex = 0`.

**Regex match** (`Middleware/RedirectMiddleware.cs`, `FindRegexRedirect`):
the underlying cached list (`GetActiveRegexEntries()`) stays a single global
list — 30-second `IMemoryCache` entry, unchanged, since every returned
`RedirectEntry` already carries its own `Domain` value and filtering an
in-memory list by domain is cheap. `FindRegexRedirect` iterates the cached
list twice: first pass only considers entries where
`entry.Domain == requestDomain`; if nothing matches, second pass considers
only entries where `Domain` is null/empty. This preserves the existing
regex cache architecture untouched while adding the precedence rule at the
point of use.

### Duplicate detection becomes domain-aware

`GetByOldUrlAndIsRegex(oldUrl, isRegex)` (used by `Create`/`Update` to
reject exact duplicates) gains a `domain` parameter and matches on
`OldUrl + IsRegex + Domain` instead of just `OldUrl + IsRegex`. This is what
makes the feature actually usable: the same `OldUrl` can now exist once
globally and once per distinct domain, but not twice for the same domain
(or twice globally).

### API / DTO

Add nullable `Domain` to `RedirectEntryDto`, `CreateRedirectEntryDto`, and
`UpdateRedirectEntryDto` (`Models/RedirectEntryDto.cs`).
`RedirectApiController`'s existing `ToDto(RedirectEntry r)` helper (added
during sub-project 2's code-review fix) gains the `Domain` mapping — this is
the only DTO call site that needs touching, since sub-project 2 already
consolidated the previous 6 duplicated construction sites into that one
helper.

### Dashboard UI

Both dashboards (Lit for Umbraco 17+/18, AngularJS for Umbraco 13) get:

- A "Domain (optional)" free-text field in the Add/Edit modal, placed after
  "New URL", with helper text explaining blank = all domains.
- A "Domain" column in the list table, positioned after "New URL" and
  before "Notes", showing the value or "All domains" when blank.

## Decisions confirmed with user (2026-07-01)

- Free-text `Domain` string, not Umbraco `IDomainService` integration.
- `www.` is not stripped/normalized — exact hostname match only.
- Domain-specific redirects take precedence over global ones for the same
  `OldUrl`.

## Out of scope

- Any UI autocomplete/dropdown of Umbraco's actually-configured site domains
  — plain free-text input only, in this first cut.
- Wildcard or pattern-based domain matching (e.g. `*.example.com`) — exact
  hostname string match only.
- Migrating/backfilling a `Domain` value onto any existing redirect — every
  existing row stays `NULL` (global) after the migration runs, which is the
  correct, intended default.
- Making `MissedRequest.Domain` (added proactively in sub-project 3) actually
  get populated or used for domain-scoped 404 suggestions — that nullable
  column exists for future use only; wiring it up is not part of this
  sub-project's scope, since the original roadmap only asked for
  domain-scoping on redirects, not on the 404 log.
