# Preserve Query String — Design

## Context

This is sub-project 1 of a 9-part roadmap for BT.RedirectManager, drawn from
`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the "appsettings
config" item, which the user chose not to pursue):

1. **Query string koruma (preserve query string)** (this spec)
2. Geçerlilik tarihleri (valid from / until)
3. Basit wildcard (`*`) eşleşme
4. Audit alanları (CreatedBy / ModifiedBy)
5. Health check endpoint
6. Unit / entegrasyon testleri
7. Çakışma / duplicate uyarısı
8. Rate limiting
9. Culture / çoklu site kapsamı (multi-site scoping)

Each sub-project gets its own spec → plan → implementation cycle. This
document covers only sub-project 1.

## Problem

Redirect rules today always send visitors to `NewUrl` exactly as configured.
Any query string on the incoming request (tracking params like
`?utm_source=google`, campaign tags, etc.) is silently dropped. For SEO and
marketing use cases, editors want an opt-in way to carry the incoming query
string forward to the destination.

## Design

### Schema

`RedirectEntry` (`Models/RedirectEntry.cs`) gains one new column:

```csharp
[Column("PreserveQueryString")]
[Constraint(Default = false)]
public bool PreserveQueryString { get; set; } = false;
```

Default `false` — existing rules keep today's behavior (query string
dropped) until an editor opts in per rule.

### Migration

New step `AddPreserveQueryStringColumn` in
`Migrations/RedirectManagerMigrationPlan.cs`, following the exact existing
pattern used for prior column additions (`#if NET10_0_OR_GREATER` / `#else`
split, `ColumnExists`/`TableExists` guard, new GUID registered in
`DefinePlan()`).

### Merge behavior

New private static helper in `Middleware/RedirectMiddleware.cs`:

```csharp
private static string? AppendPreservedQueryString(string? targetUrl, bool preserve, QueryString incomingQuery)
{
    if (!preserve || string.IsNullOrEmpty(targetUrl) || !incomingQuery.HasValue)
        return targetUrl;

    var incoming = incomingQuery.Value!.TrimStart('?');
    return targetUrl.Contains('?', StringComparison.Ordinal)
        ? $"{targetUrl}&{incoming}"
        : $"{targetUrl}?{incoming}";
}
```

- `NewUrl` with no existing query string → incoming query appended with `?`.
- `NewUrl` with its own query string (e.g. `/yeni?ref=kampanya`) → incoming
  query appended with `&`, so both survive
  (`/yeni?ref=kampanya&utm_source=google`). On a key collision (same param
  name in both), both copies end up in the URL — the incoming one is not
  deduplicated against the existing one; this is acceptable since the target
  page will simply see the last occurrence win, and true dedup would need
  a query-string parser for a corner case nobody asked for.
- Applied uniformly to the A/B test variant actually served (`NewUrl` or
  `VariantBUrl`, whichever `ResolveRedirectTarget` picks), so preserve
  behaves the same regardless of which variant a visitor lands on.
- No effect on 404/410 responses (no `NewUrl` target to append to).

Call sites in `RedirectMiddleware.InvokeAsync`:
- Exact-match 301/302 branch: wrap the result of `ResolveRedirectTarget(...)`
  before assigning `Response.Headers.Location`.
- Regex-match 301/302 branch: wrap `regexRedirect.ComputedNewUrl` the same
  way, using `regexRedirect.Entry.PreserveQueryString`.

Both call sites pass `context.Request.QueryString` (the same value already
read earlier in the method for `pathAndQuery` matching).

### Known edge case (out of scope to fully solve)

If a rule's `OldUrl` itself embeds a literal query string (the existing
"match `/raporlar.aspx?type=11`" behavior) **and** that same rule has
`PreserveQueryString` enabled, the incoming query string gets appended to
`NewUrl` a second time (once implicitly via the literal match, once via this
feature), since the append logic only looks at `context.Request.QueryString`
without knowing whether `OldUrl` already "consumed" it as part of matching.
This combination is expected to be rare (query-string-based `OldUrl` rules
predate this feature and are a legacy-URL-migration pattern); not handling
it specially keeps the merge logic simple and predictable.

### API

`CreateRedirectEntryDto`, `UpdateRedirectEntryDto`, and `RedirectEntryDto`
(`Models/RedirectEntryDto.cs`) each gain:

```csharp
public bool PreserveQueryString { get; set; } = false;
```

No new validation — it's an optional bool like `IsActive`/`IsRegex`.
`RedirectService` create/update mapping passes it through untouched.

### Dashboard UI

Both dashboards gain a checkbox in the add/edit modal, next to the existing
A/B-test toggle:

- **Lit dashboard** (`App_Plugins/RedirectManager/redirect-dashboard.js`):
  a `.toggle-label` checkbox bound to `this.formData.preserveQueryString`,
  same pattern as the existing `abTestEnabled` toggle.
- **AngularJS dashboard** (`App_Plugins/RedirectManager/dashboard.html` +
  `redirect.controller.js`): a matching checkbox bound to
  `vm.formData.preserveQueryString`.

Label: "Query string koruma" (Lit dashboard is otherwise in English per
recent commits — use "Preserve query string" there instead, matching that
dashboard's existing language).

## Decisions confirmed with user (2026-07-10)

- Opt-in per rule via a boolean column, not a global or appsettings toggle.
- On collision, merge with `&` (both survive); incoming query is not
  deduplicated against an existing one on `NewUrl`.

## Out of scope

- Deduplicating query parameters that appear on both `NewUrl` and the
  incoming request.
- Special-casing rules where `OldUrl` itself contains a literal query
  string (see "Known edge case" above).
- Any appsettings-level configurability (explicitly excluded from this
  roadmap batch per user decision).
