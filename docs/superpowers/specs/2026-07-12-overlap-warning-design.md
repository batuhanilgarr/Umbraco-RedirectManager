# Overlap / Duplicate Warning — Design

## Context

This is sub-project 7 of a 9-part roadmap for BT.RedirectManager, drawn from
`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the "appsettings
config" item, which the user chose not to pursue):

1. Query string koruma (preserve query string) (done)
2. Geçerlilik tarihleri (valid from / until) (done)
3. Basit wildcard (`*`) eşleşme (done)
4. Audit alanları (CreatedBy / ModifiedBy) (done)
5. Health check endpoint (done)
6. Unit / entegrasyon testleri (done)
7. **Çakışma / duplicate uyarısı** (this spec)
8. Rate limiting
9. Culture / çoklu site kapsamı (multi-site scoping)

Each sub-project gets its own spec → plan → implementation cycle. This
document covers only sub-project 7.

## Problem

The package already has a *hard* duplicate check: `Create`/`Update` reject
(`409 Conflict`) when a new rule has the exact same `OldUrl` + match-type
(regex/exact) + domain as an existing rule. What's missing is a *soft*
warning for the more subtle case the roadmap calls out: a broad matcher
(regex or wildcard) that, once active, will also match paths already
covered by an existing **active exact rule** — e.g. adding a wildcard rule
`/blog/*` while an exact rule for `/blog/post-1` already exists. Today
nothing tells the admin this is happening; the exact rule keeps matching
first (per existing match-order: exact → wildcard → regex), so the new
broad rule silently never fires for that one path, which is easy to miss.

## Design

### Scope decision (confirmed with user)

Only one direction is in scope: **a new/updated broad rule (regex or
wildcard) that overlaps an existing active exact rule.** The reverse
direction (a new exact rule falling under an existing broad rule) and
regex-vs-regex/wildcard-vs-wildcard overlap are explicitly out of scope for
this round. This is a non-blocking warning — it never prevents the save,
unlike the existing hard duplicate conflict.

### New service method: `IRedirectService.FindOverlappingExactRules`

```csharp
IEnumerable<RedirectEntry> FindOverlappingExactRules(string oldUrl, bool isRegex, string? domain);
```

Implemented in `RedirectService`:

1. Query active, *truly exact* rules — `IsActive = 1 AND IsRegex = 0 AND
   OldUrl NOT LIKE '%*%'` — scoped by the same domain-fallback rule already
   used by `GetByOldUrl` elsewhere in this class: a candidate is in scope if
   its `Domain` matches the new rule's `Domain` (case-insensitive), or
   either side is null/empty (global rules apply across all domains, and a
   new global broad rule can shadow any domain-specific exact rule).
2. For each candidate, test whether the new rule's pattern matches the
   candidate's `OldUrl`:
   - `isRegex == true`: `new Regex(oldUrl, RegexOptions.CultureInvariant |
     RegexOptions.IgnoreCase, RegexTimeout).IsMatch(candidate.OldUrl)`
   - `isRegex == false` (wildcard): same, but the pattern is
     `WildcardPatternBuilder.BuildRegexPattern(oldUrl)` — identical to how
     `RedirectMiddleware` and the existing `/test` endpoint already build
     wildcard patterns.
   - Wrapped in try/catch: an unparsable regex here would already have been
     rejected by `ValidateRedirect` earlier in the same request, so this is
     defensive, not expected to trigger in practice.
3. Returns the matching `RedirectEntry` objects (no cap inside the service
   method — capping is a presentation concern, done in the controller).

This method is **only ever called from `Create`/`Update`**, never from
`RedirectMiddleware`'s per-request hot path — so, unlike the middleware's
own wildcard/regex matching, there is no tight performance budget here; a
handful of extra `Regex.IsMatch` calls against a query already filtered by
`IsActive`/`IsRegex`/domain is negligible on an admin save action.

### Controller wiring (`RedirectApiController.Create` / `.Update`)

After the existing hard-duplicate check and the actual
`_redirectService.Create(...)` / `.Update(...)` call succeeds, if the saved
entry is `IsActive` and is a broad matcher (`IsRegex == true`, or
`IsRegex == false && OldUrl.Contains('*')`), call
`FindOverlappingExactRules(redirect.OldUrl, redirect.IsRegex,
redirect.Domain)`. If it returns any entries:

- Take the first 5 `OldUrl` values.
- If more than 5 matched, append a final string noting how many more (e.g.
  `"...and 3 more"`).
- Set `RedirectEntryDto.OverlapWarnings` to this list of strings before
  returning `Ok(dto)`.

If the saved entry is inactive, not a broad matcher, or nothing overlaps,
`OverlapWarnings` is left `null` (its default) — the field is simply absent
from the JSON response, identical to today's behavior.

### DTO change: `RedirectEntryDto.OverlapWarnings`

```csharp
public List<string>? OverlapWarnings { get; set; }
```

Added directly to the existing, already-shared `RedirectEntryDto` (used by
`GetAll`, `Create`, `Update`, and the `/test` match endpoint) rather than
introducing a new wrapper response type. This is deliberately the least
invasive option: today, both dashboards do
`this.redirects = [saved, ...this.redirects]` / `this.redirects.map(r =>
r.id === saved.id ? saved : r)` directly against the flat DTO returned by
`Create`/`Update`. Wrapping the response in e.g. `{ entry, warnings }` would
require touching every consumer of that response shape. An extra,
independently-nullable field on the same DTO carries zero risk to existing
callers: `GetAll`/`/test` responses simply never populate it (stays `null`
→ absent from JSON), and the save handlers only need one additional
conditional check for a field that wasn't there before.

### Dashboard UX (both Lit and AngularJS dashboards)

Both dashboards already show a toast/notification after a successful save
(`showMessage(text, type)` in the Lit dashboard; an equivalent in the
AngularJS one) with existing `success`/`error`/`info` styles. This feature
adds a fourth style, `warning` (amber, following the same CSS pattern as
the existing `notif-success`/`notif-error`/`notif-info` rules), and after
a successful Create/Update, if `saved.overlapWarnings` (camelCase — .NET's
default JSON serialization) is present and non-empty, shows a **second**
toast immediately after the existing success toast:

> "Heads up: this rule also matches N existing active rule(s): /a, /b, /c"

This is purely informational — it does not block or alter the save that
already succeeded. All dashboard-facing strings are in English, matching
existing convention.

## Decisions confirmed with user (2026-07-12)

- Scope is narrowly "new broad rule (regex/wildcard) overlaps an existing
  active exact rule" — not the reverse direction, and not
  regex-vs-regex/wildcard-vs-wildcard overlap detection.
- Non-blocking warning, surfaced via a new optional DTO field
  (`OverlapWarnings`) rather than changing the Create/Update response
  shape, to avoid touching existing dashboard consumers of that response.
- Surfaced in both dashboards as a second, non-blocking warning toast after
  the existing success toast.

## Out of scope

- Exact-rule-falls-under-existing-broad-rule warnings (the reverse
  direction from what's implemented here).
- Regex-vs-regex or wildcard-vs-wildcard overlap detection.
- Any warning surfaced outside of the Create/Update save flow (e.g. no
  standing "overlap report" dashboard view, no periodic background scan).
- Blocking/preventing the save in any way — this is purely informational.
- CSV import's duplicate/overlap behavior — unchanged by this sub-project.
- Any appsettings-level configurability (e.g. no toggle to disable this
  check, no configurable cap on the warning list size — hardcoded at 5).
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step.
