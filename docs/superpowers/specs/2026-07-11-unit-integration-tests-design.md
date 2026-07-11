# Unit / Integration Tests — Design

## Context

This is sub-project 6 of a 9-part roadmap for BT.RedirectManager, drawn from
`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the "appsettings
config" item, which the user chose not to pursue):

1. Query string koruma (preserve query string) (done)
2. Geçerlilik tarihleri (valid from / until) (done)
3. Basit wildcard (`*`) eşleşme (done)
4. Audit alanları (CreatedBy / ModifiedBy) (done)
5. Health check endpoint (done)
6. **Unit / entegrasyon testleri** (this spec)
7. Çakışma / duplicate uyarısı
8. Rate limiting
9. Culture / çoklu site kapsamı (multi-site scoping)

Each sub-project gets its own spec → plan → implementation cycle. This
document covers only sub-project 6.

## Problem

This repository has zero test infrastructure. Five feature sub-projects
have already shipped on top of `RedirectService`/`RedirectMiddleware`/
`RedirectApiController` with no automated safety net — every "manual
verification" section in every prior plan exists specifically because
there was nothing else to fall back on. The original roadmap idea named
three targets: `RedirectService` (Create/Update/Delete/GetByOldUrl/regex
cache), `RedirectMiddleware` (skip paths, exact/regex redirect, 404/410),
and the API layer (Create/Update validation, duplicate check, CSV
import/export).

## Design

### Scope decisions (confirmed with user)

- **Test framework:** xUnit — the de facto standard for modern .NET
  libraries and the same framework Umbraco's own core test suite uses.
- **Mocking:** NSubstitute — used to fake `IRedirectService` and the
  handful of other interface dependencies these classes take, rather than
  hand-written fake classes.
- **Depth:** pure-logic testing only. `RedirectService`'s own SQL queries
  (the actual `IScopeProvider`/NPoco access) are explicitly **not** tested
  in this sub-project — there is no in-memory-SQLite test harness for
  `IScopeProvider`/`IScope` here. Every class tested in this sub-project is
  tested either as a pure function (`WildcardPatternBuilder`,
  `DomainNormalizer`) or with its dependencies substituted
  (`RedirectMiddleware`'s `IRedirectService` parameter,
  `RedirectApiController`'s constructor dependencies) — nothing in this
  sub-project talks to a real or fake database.
- **CSV import/export** is explicitly out of scope for this round (would
  require simulating `IFormFile`/raw request bodies, a separate, larger
  effort).

### New project: `Umbraco.RedirectManager.Tests`

A new sibling project at `Umbraco.RedirectManager.Tests/` (repo root,
alongside `Umbraco.RedirectManager.csproj`), targeting **`net10.0` only**
(a test project isn't packed/shipped, so it doesn't need to multi-target
the way the main library does — it references whichever build of the main
assembly matches its own single TFM). Package references:
`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`,
`NSubstitute`, plus a `ProjectReference` to `../Umbraco.RedirectManager.csproj`.

Folder structure mirrors the main project's source layout:

```
Umbraco.RedirectManager.Tests/
  Umbraco.RedirectManager.Tests.csproj
  Services/
    WildcardPatternBuilderTests.cs
    DomainNormalizerTests.cs
  Middleware/
    RedirectMiddlewareTests.cs
  Controllers/
    RedirectApiControllerTests.cs
```

### `WildcardPatternBuilderTests`

Pure unit tests against `WildcardPatternBuilder.BuildRegexPattern(string)`
(a public static method, no dependencies). Cases: a single `*` produces an
anchored capturing pattern; literal regex metacharacters (`.`, `+`) on
either side of the `*` are escaped, not interpreted; a pattern with no `*`
at all falls back to a literal anchored exact-match pattern; the produced
pattern actually matches/rejects the expected paths when compiled and run
against `Regex.IsMatch` (i.e. tests assert on match behavior, not just the
pattern string, since the string's exact escaped form is an implementation
detail — matching behavior is the real contract).

### `DomainNormalizerTests`

Pure unit tests against `DomainNormalizer.Normalize(string?)`. Cases:
mixed-case input lowercases; a trailing `:port` is stripped; an IPv6
literal in brackets (e.g. `[::1]:8080`) is not corrupted by the port-strip
logic; `null`/empty/whitespace input normalizes to `null`.

### `RedirectMiddlewareTests`

Constructs a real `RedirectMiddleware` instance with:
- `RequestDelegate next` — a no-op delegate (`_ => Task.CompletedTask`),
  or one that sets a marker/status code so tests can assert whether the
  pipeline actually continued past this middleware.
- `ILogger<RedirectMiddleware>` — `NullLogger<RedirectMiddleware>.Instance`.
- `IRedirectHitTracker`, `IVariantBHitTracker`, `IMissedRequestTracker` —
  NSubstitute fakes (`Substitute.For<T>()`), asserted against with
  `.Received()` where the test cares whether a hit/miss was recorded.

`IRedirectService` is NOT a constructor dependency — it's passed directly
into `InvokeAsync(HttpContext, IRedirectService)` per call, so each test
supplies its own NSubstitute fake configured with `.GetByOldUrl(...)`,
`.GetActiveRegexEntries()`, `.GetActiveWildcardEntries()` returning
whatever fixture data that test needs — no shared mutable state between
tests.

`HttpContext` is a real `DefaultHttpContext` (from
`Microsoft.AspNetCore.Http`) with `Request.Path`/`Request.Host`/
`Request.QueryString` set per test; assertions read
`context.Response.StatusCode` and `context.Response.Headers.Location`.

Cases:
- A request path under a skip prefix (e.g. `/umbraco/...`) calls `next`
  and never queries `redirectService` at all.
- An exact-match active 301 rule sets status 301 and the `Location`
  header to `NewUrl`.
- An exact-match active 404/410 rule sets that status code and writes the
  expected body text, and records a hit via `IRedirectHitTracker`.
- A rule with `PreserveQueryString = true` appends the incoming query
  string to the `Location` header, merging with `&` when the target
  already has its own query string.
- A path matching only after `ToggleTrailingSlash` (e.g. rule for
  `/sayfa`, request for `/sayfa/`) still resolves as an exact match.
- A wildcard-matching rule (from `GetActiveWildcardEntries()`) is used
  when no exact match exists, with `*` in `NewUrl` correctly substituted
  by the matched segment.
- A regex-matching rule (from `GetActiveRegexEntries()`) is used when
  neither exact nor wildcard matched.
- When nothing matches and the downstream pipeline (`next`) ends with a
  404 status, `IMissedRequestTracker.RecordMiss` is called with the
  request path; when downstream doesn't produce a 404, it is not called.

Out of scope for this test class: `ResolveRedirectTarget`'s A/B-test
cookie-assignment randomness (`Random.Shared`) — deterministic assertions
on which variant a fresh visitor lands on would require injecting a fake
random source, which `RedirectMiddleware` doesn't currently support and
which this sub-project doesn't add. Only the already-cookie-assigned path
(`assignment == "A"` / `"B"` via a pre-set request cookie) is
deterministic and testable without changes to production code.

### `RedirectApiControllerTests`

Constructs a real `RedirectApiController` with all six constructor
dependencies (`IRedirectService`, `IMissedRequestService`,
`IRedirectTelemetryPinger`, `IRedirectTelemetrySettingsStore`,
`IRedirectVersionChecker`, `IBackOfficeSecurityAccessor`) as NSubstitute
fakes — only `IRedirectService` is configured per-test; the others are
unconfigured fakes satisfying the constructor, since `Create`/`Update`
don't touch them (`IBackOfficeSecurityAccessor.BackOfficeSecurity`
defaults to `null` on an unconfigured NSubstitute fake, which
`GetCurrentUserName()` already handles via its own `?.` null-conditional
chain — no special setup needed for that dependency in these tests).

Cases, on both `Create` and `Update`:
- Empty/whitespace `OldUrl` → `BadRequest` with "Old URL is required".
- `StatusCode` 301/302 with empty `NewUrl` → `BadRequest` with "New URL is
  required for redirect status codes".
- `IsRegex = true` with an unparsable pattern (e.g. an unbalanced `(`) →
  `BadRequest` with "Invalid regex pattern" (exercises `ValidateRedirect`
  indirectly through the public action, not by calling the private method
  directly).
- `NewUrl` not starting with `/`, `http://`, or `https://` → `BadRequest`
  with "New URL must start with '/' or 'http(s)://'".
- A duplicate exists (`GetByOldUrlAndIsRegex` returns a non-null entry) →
  `Conflict`.
- **`Update`-only:** the "duplicate" found by `GetByOldUrlAndIsRegex` has
  the same `Id` as the row being updated → NOT a conflict (this is the
  "editing itself" case — `Update`'s duplicate check is
  `duplicate != null && duplicate.Id != id`, `Create` has no such
  exception since there's no existing `id` yet).
- **`Update`-only:** `_redirectService.Update(...)` returns `null` (row
  doesn't exist) → `NotFound`.
- A fully valid request with no duplicate → `Ok` wrapping the mapped DTO,
  and `_redirectService.Create`/`Update` was actually called with the
  expected arguments (verified via NSubstitute's `.Received()`).

Out of scope for this test class: the A/B-testing-specific validation
branches in `ValidateRedirect` (Variant B URL format/weight, regex+A/B
incompatibility) — these are reachable but not exercised in this pass, to
keep the initial test suite focused on the roadmap's original three
headline validations (required fields, format, duplicate) rather than
exhaustively enumerating every branch of every helper on the first pass.

## Decisions confirmed with user (2026-07-11)

- xUnit, not NUnit.
- NSubstitute, not hand-written fake classes.
- Pure-logic/substituted-dependency testing only — no SQLite/in-memory DB
  harness for `RedirectService` itself in this sub-project.
- Include `RedirectApiController`'s `Create`/`Update` validation and
  duplicate-check tests (not deferred to a later round).
- CSV import/export logic is out of scope for this round.

## Out of scope

- `RedirectService`'s own database-backed methods (`Create`, `Update`,
  `Delete`, `GetByOldUrl`, the regex/wildcard entry caches) — no DB test
  harness is introduced in this sub-project.
- CSV import/export parsing (`RedirectApiController.ImportCsv`,
  `ParseCsvLine`, `GetCol`, `RedirectCsvWriter`).
- The A/B-test cookie-assignment random-selection path in
  `RedirectMiddleware.ResolveRedirectTarget`.
- The A/B-test-specific validation branches in
  `RedirectApiController.ValidateRedirect` (Variant B URL/weight
  validation, regex+A/B incompatibility).
- Any CI/CD pipeline wiring to run these tests automatically on push/PR —
  this sub-project only adds the test project and its tests; hooking it
  into a CI workflow is a separate concern not requested here.
- Any appsettings-level configurability.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step.
