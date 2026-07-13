# Culture / Multi-Site Scoping — Design

## Context

This is sub-project 9 (the final one) of a 9-part roadmap for BT.RedirectManager,
drawn from `docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the
"appsettings config" item, which the user chose not to pursue):

1. Query string koruma (preserve query string) (done)
2. Geçerlilik tarihleri (valid from / until) (done)
3. Basit wildcard (`*`) eşleşme (done)
4. Audit alanları (CreatedBy / ModifiedBy) (done)
5. Health check endpoint (done)
6. Unit / entegrasyon testleri (done)
7. Çakışma / duplicate uyarısı (done)
8. Rate limiting (done)
9. **Culture / çoklu site kapsamı** (this spec)

This document covers only sub-project 9, corresponding to roadmap item 13
("Çoklu site / kültür (multi-site)").

## Problem

The package already supports scoping redirect rules by **domain**
(`RedirectEntry.Domain`, shipped earlier — a rule can target a specific
hostname or apply globally). What's missing is the other half of the
roadmap item: scoping by **culture/language**, so a single domain serving
multiple languages (e.g. via Umbraco's own Culture-and-Hostnames
configuration, or URL-segment-based language variants under one hostname)
can have redirect rules that only apply to one specific language, without
requiring a separate domain per language.

## Design

### New column: `RedirectEntry.Culture`

A nullable string (e.g. `"tr-TR"`, `"en-US"`), added via the same
migration pattern already used for every other optional column on this
table (`AddColumn<RedirectEntry>`, both the `AsyncMigrationBase` and
`MigrationBase` `#if NET10_0_OR_GREATER` variants). `null`/empty means
"applies to all cultures" (unchanged behavior, the default for every
existing rule).

### New service: `IRedirectCultureResolver`

```csharp
public interface IRedirectCultureResolver
{
    string? ResolveCulture(string? domain);
}
```

Implemented as a singleton that calls Umbraco's own
`Umbraco.Cms.Core.Services.IDomainService.GetAll(includeWildcards: false)`
— the exact same registry an admin configures via **Settings → Culture and
Hostnames** — to build a `DomainName → LanguageIsoCode` lookup, cached in
`IMemoryCache` for 30 seconds (same TTL convention already used by
`RedirectService`'s own active-entry caches). `ResolveCulture(domain)`
normalizes the input the same way `DomainNormalizer` does and looks up the
matching registered domain's culture; returns `null` if no domain/culture
binding is registered for that hostname (a single-culture site gets `null`
for every request, which — as described below — means only
culture-agnostic rules ever match, i.e. **zero behavior change** for sites
that don't use Culture and Hostnames).

**Verified compatibility:** `IDomainService.GetAll(bool)` and
`IDomain.DomainName`/`LanguageIsoCode` were confirmed via direct reflection
against both actual target assemblies (`Umbraco.Cms.Core` 13.9.2 and
17.1.0) to have an identical shape in both — a single, non-conditional
code path works for both target frameworks, unlike the `HealthCheck` base
class from an earlier sub-project, which did break across versions.

`false` is passed for `includeWildcards` deliberately: wildcard domain
entries represent a *content node's* default culture assignment (their
`DomainName` is a node ID, not a real hostname) and aren't meaningful for
matching against an incoming HTTP `Host` header.

### Scoping semantics: an additional filter, not a new fallback tier

Domain already has its own two-query fallback (try the domain-specific
match, then fall back to the global/no-domain match) inside
`RedirectService.GetByOldUrl`/`GetByOldUrlAndIsRegex`. Rather than
introducing a second, nested culture-fallback tier (which would multiply
into up to 4 queries per lookup), culture is folded in as **one additional
`WHERE` condition applied identically inside both of the existing
domain-tier queries**:

```sql
AND (Culture = @cultureParam OR Culture IS NULL OR Culture = '')
```

This means: whichever domain tier already matches (unchanged from today),
a candidate row must *also* satisfy the culture condition — either it has
no `Culture` set (always in scope), or its `Culture` matches the request's
resolved culture. If the request's resolved culture is `null` (no
domain/culture binding registered), only culture-agnostic rows pass —
symmetric with how an unresolved domain already only lets
global/no-domain rows through.

For the wildcard/regex tiers (which fetch their *entire* active-entry list
from cache and filter with LINQ in `RedirectMiddleware`, not a targeted SQL
query), the equivalent filter is a small `IsCultureInScope(candidateCulture,
requestCulture)` static helper alongside the existing
`r.Domain == domain` / `string.IsNullOrEmpty(r.Domain)` checks already
used there — same two-tier domain fallback, with the culture check ANDed
in at each tier, mirroring exactly how domain and culture combine in the
exact-match SQL path.

### `RedirectService` signature changes

`GetByOldUrl`, `GetByOldUrlAndIsRegex`, and `FindOverlappingExactRules`
each gain a `string? culture` parameter (mirroring their existing `string?
domain` parameter). `Create`/`Update` persist `dto.Culture` (trimmed,
`null` if blank — no dedicated normalizer class needed, unlike `Domain`'s
port-stripping/IPv6-aware `DomainNormalizer`, since culture codes have no
equivalent structural quirks to handle).

`GetActiveRegexEntries()`/`GetActiveWildcardEntries()` keep their existing
signatures unchanged (they already return the full active-entry set
regardless of domain, with domain filtering happening in the middleware —
culture filtering slots into that same existing LINQ filtering step, not
into the cached DB query itself).

`FindOverlappingExactRules`'s domain-in-scope check
(`string.IsNullOrEmpty(c.Domain) || string.IsNullOrEmpty(normalizedDomain)
|| string.Equals(...)`) gets an identical additional clause for culture,
so a broad rule scoped to one culture is never flagged as overlapping an
exact rule scoped to a different culture.

### `RedirectMiddleware` wiring

`InvokeAsync` resolves `culture` once per request, right next to the
existing `domain` resolution:

```csharp
var domain = DomainNormalizer.Normalize(context.Request.Host.Value);
var culture = _cultureResolver.ResolveCulture(domain);
```

`culture` is then threaded through all three match tiers (exact, wildcard,
regex) exactly as `domain` already is. `RedirectMiddleware` gains one more
constructor dependency, `IRedirectCultureResolver`.

### DTOs and dashboards

`CreateRedirectEntryDto`/`UpdateRedirectEntryDto`/`RedirectEntryDto` all
gain a `Culture` field. `RedirectApiController`'s duplicate-check
(`GetByOldUrlAndIsRegex`) and overlap-warning
(`FindOverlappingExactRules`) calls pass it through.

Both dashboards (Lit and AngularJS) get a new optional "Culture" text
input in the create/edit form and a new "Culture" column in the list
table — an exact mirror of how the existing "Domain" field is presented
(free-text input, helper text along the lines of "Leave blank to apply to
all cultures", no dropdown/language-picker — consistent with `Domain`
also being a plain free-text field rather than a site-picker).

## Decisions confirmed with user (2026-07-13)

- Full `IDomainService` integration (not a URL-segment convention, and not
  a tag-only/no-live-filtering approach) — verified feasible across both
  target Umbraco versions via direct assembly reflection before
  committing to this approach.
- Culture is an *additional* filter layered onto the existing domain
  fallback, not a second independent fallback tier — avoids a
  combinatorial explosion of SQL queries.
- Free-text "Culture" input in both dashboards, mirroring `Domain`'s
  existing UX exactly — no dynamic language dropdown.

## Out of scope

- Any deeper integration with Umbraco's actual published-content culture
  variants (`IPublishedContent.Cultures`, `UmbracoContext.PublishedRequest`)
  — this only reads Umbraco's *domain/hostname → culture* registry
  (`IDomainService`), the same mechanism the roadmap itself calls out
  ("Umbraco'nun site binding'i ile entegre düşünmek"), not the full
  content-variant resolution pipeline (which additionally isn't reliably
  available this early in the pipeline, since this middleware runs in
  `PrePipeline`, before Umbraco's own routing/content resolution).
- A culture dropdown/picker populated from `ILocalizationService`'s
  registered languages — plain free-text input, matching `Domain`.
- Per-rule combined "SiteId" concept beyond Domain+Culture (the roadmap
  mentions `Culture` *or* `SiteId` as alternatives; Domain (already
  shipped) plus this new Culture field together cover the intent without
  introducing a third, redundant scoping dimension).
- CSV import/export changes to include the new `Culture` column — out of
  scope for this round, consistent with CSV import/export being
  out-of-scope in the earlier unit-tests sub-project too.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step.
