# Simple Wildcard (`*`) Matching — Design

## Context

This is sub-project 3 of a 9-part roadmap for BT.RedirectManager, drawn from
`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the "appsettings
config" item, which the user chose not to pursue):

1. Query string koruma (preserve query string) (done)
2. Geçerlilik tarihleri (valid from / until) (done)
3. **Basit wildcard (`*`) eşleşme** (this spec)
4. Audit alanları (CreatedBy / ModifiedBy)
5. Health check endpoint
6. Unit / entegrasyon testleri
7. Çakışma / duplicate uyarısı
8. Rate limiting
9. Culture / çoklu site kapsamı (multi-site scoping)

Each sub-project gets its own spec → plan → implementation cycle. This
document covers only sub-project 3.

## Problem

Editors who don't know regex syntax have no easy way to redirect a whole
group of paths (e.g. `/blog/*` → `/haberler/*`) without learning the
existing `IsRegex` feature's raw regex patterns and capture-group (`$1`)
substitution syntax.

## Design

### No schema change

A rule is auto-detected as a "wildcard rule" when `IsRegex == false` and its
`OldUrl` contains exactly one literal `*` character — no new column, no new
toggle. This matches the original roadmap idea exactly and keeps the
feature's own complexity proportionate to "simple."

### Pattern translation

Given an `OldUrl` like `/blog/*`, split it at the single `*` into a prefix
and suffix, `Regex.Escape` each side (so a literal `.`, `+`, etc. in the
URL is treated as a literal character, not a regex metacharacter — the
entire point of this feature being usable without regex knowledge), and
join with a capturing `(.*)`, anchored at both ends:

```
^{Regex.Escape(prefix)}(.*){Regex.Escape(suffix)}$
```

Anchoring with `^...$` is necessary here (unlike hand-written regex rules,
which rely on the user to anchor if they want to) because `Regex.IsMatch`
without anchors matches anywhere in the input — an unanchored `/blog/(.*)`
would incorrectly match a path like `/some-other/blog/x` too.

`NewUrl`'s single `*` (if present — a rule may redirect every match to one
fixed destination with no `*` in `NewUrl` at all) is replaced with `$1`
before calling `regex.Replace`, exactly mirroring how the existing `IsRegex`
feature already uses `.Replace(path, newUrl)` with a user-supplied `$1`.

### Matching in `RedirectMiddleware`

A new step is inserted in `InvokeAsync` between the existing exact-match
block and the existing regex-match block:

1. Exact match (`GetByOldUrl`) — unchanged.
2. **New: wildcard match** (`FindWildcardRedirect`/`FindWildcardMatchIn`,
   mirroring the existing `FindRegexRedirect`/`FindRegexMatchIn` structure
   exactly): domain-specific candidates checked first, then global
   (`Domain` is null/empty) candidates, using a new
   `RedirectService.GetActiveWildcardEntries()` — same `IMemoryCache`-based,
   30-second-TTL caching pattern as `GetActiveRegexEntries()`, same
   `ValidFrom`/`ValidUntil` window filtering, but selecting
   `IsActive = 1 AND IsRegex = 0 AND OldUrl LIKE '%*%'` instead. Compiled
   `Regex` objects (from the translated pattern, not the raw `OldUrl`) are
   cached in a separate `ConcurrentDictionary<string, Regex>`
   (`WildcardRegexCache`), keyed by the raw `OldUrl` text, mirroring the
   existing `RegexCache` dictionary's lifetime/invalidation model exactly
   (no explicit invalidation needed — a since-edited-away pattern's cached
   `Regex` object simply becomes unused).
3. Regex match (`FindRegexRedirect`) — unchanged, still only reached if
   neither exact nor wildcard matched.

The wildcard-match block's 301/302/404/410 handling is a straight copy of
the existing regex-match block's switch statement (a third near-identical
copy, consistent with the file's existing style, which already has two).
`PreserveQueryString`/`AppendPreservedQueryString` and the
`ValidFrom`/`ValidUntil` window apply to wildcard rules automatically, since
they flow through the same `AppendPreservedQueryString` call and the same
`GetActiveWildcardEntries()` SQL filtering that already gates
`GetActiveRegexEntries()`. A/B testing (`VariantBUrl`) is **not** available
for wildcard rules, for the same reason it's already unavailable for regex
rules — `ResolveRedirectTarget` (the A/B resolution logic) is only called
from the exact-match branch. Trailing-slash toggling
(`ToggleTrailingSlash`) also does **not** apply to wildcard matching, again
matching how regex rules already don't get it — both are pre-existing,
accepted characteristics of the non-exact-match paths, not something this
feature changes.

### Cache invalidation rename

`RedirectService`'s private `InvalidateRegexCache()` (called from `Create`,
`Update`, `Delete`, `BulkDelete`, `BulkSetActive`) is renamed to
`InvalidateMatchCaches()` and now clears both the existing
`ActiveRegexCacheKey` and the new `ActiveWildcardCacheKey` `IMemoryCache`
entries. This is a private, single-file rename (five call sites, all in
`RedirectService.cs`) — no public API surface changes.

### Validation

Client-side only (matching this package's existing validation posture —
see the two prior sub-projects' specs): if `OldUrl` or `NewUrl` contains
more than one `*`, block save with an inline error ("Old URL/New URL can
only contain one wildcard (`*`)"), in both dashboards' save functions, same
style as the existing "Variant B URL is required" check. No server-side
validation is added.

### Test endpoint

Unlike the two prior sub-projects (which explicitly left the `Test`
endpoint out of scope), this one **updates** `GET .../test?path=` to also
check wildcard entries — inserted between its existing exact-match check
and its existing regex-entries loop, using the same translated-pattern
`Regex` construction described above (the endpoint already builds its own
`Regex` objects independently of the middleware's caches, so this follows
that existing local pattern rather than sharing `WildcardRegexCache`).
`matchType` in the response becomes `"Wildcard"` for a wildcard-matched
rule. This is a deliberate deviation from the "leave Test alone" precedent:
this feature's whole audience is people who don't want to reason about
regex, and they need Test to accurately reflect what the live site will do
with a `/blog/*`-style rule, or the tool actively misleads exactly the
users it's meant to help.

### Dashboard UI

Both dashboards get two small, additive changes:

1. **Hint text.** The Old URL field's description gains a wildcard tip
   (e.g. "The path to redirect from. Tip: use `*` to match anything (e.g.
   `/blog/*`)."), shown regardless of the `IsRegex` toggle state (since
   wildcard only applies when `IsRegex` is off, but the field is shared).
   The New URL field's description gains a matching tip about using `*` to
   reference the captured value.
2. **List pill.** The existing "Exact"/"Regex" `type-pill` in both
   dashboards' list tables gains a third state: **"Wildcard"** — computed
   client-side (`!redirect.isRegex && redirect.oldUrl.includes('*')`, no
   new DTO field, mirroring how the "Scheduled"/"Expired" badge was
   computed client-side in the prior sub-project) — with its own CSS color
   variant (`.type-pill.wildcard`), added alongside the existing
   `.type-pill.regex` rule in both dashboards' stylesheets (`redirect.css`
   for AngularJS, the inline `static styles` block for Lit).

No changes to CSV export/import, `RedirectStatsBuilder`, or the duplicate-
check (`GetByOldUrlAndIsRegex`) — a wildcard rule is, for all of those
purposes, just a normal `IsRegex = false` row whose `OldUrl` happens to
contain a `*`; duplicate detection already works correctly via literal
string equality on `OldUrl`, exactly as it does for any other exact rule.

## Decisions confirmed with user (2026-07-10)

- Auto-detected via `*` in `OldUrl` + `IsRegex = false` — no new column or
  toggle.
- Exactly one `*` per rule (in `OldUrl`, and at most one in `NewUrl`) — not
  multiple wildcards per rule.
- The `Test` endpoint is updated to understand wildcard matching, unlike
  the two prior sub-projects' precedent of leaving it untouched — because
  this feature's target audience specifically relies on that tool to
  verify a rule works without reading a translated regex.

## Out of scope

- Multiple `*` wildcards in a single rule.
- Server-side validation of the single-wildcard constraint (client-side
  only, per this package's existing validation convention).
- A/B testing for wildcard rules (same limitation regex rules already
  have).
- Trailing-slash-toggle matching for wildcard rules (same limitation regex
  rules already have).
- CSV export/import changes, `RedirectStatsBuilder` changes, or any change
  to `GetByOldUrlAndIsRegex`'s duplicate-check query.
- Conflict/overlap detection between a wildcard rule and another rule that
  could also match the same path (e.g. `/blog/*` vs. an exact `/blog/foo`
  rule) — this is roadmap sub-project 7 ("Çakışma uyarısı"), not this one.
- Any appsettings-level configurability — explicitly excluded from this
  roadmap batch.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step.
