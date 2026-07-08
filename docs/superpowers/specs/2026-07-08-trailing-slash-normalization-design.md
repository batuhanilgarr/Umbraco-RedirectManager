# Trailing Slash Normalization — Design

## Context

From the project's own feature-ideas roadmap (`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md`,
item 10) and confirmed with the user (2026-07-08): a redirect rule created
for `/sayfa` currently does not fire for a request to `/sayfa/` (or vice
versa), because `RedirectMiddleware`'s lookup does an exact string match
against the stored `OldUrl`. This is a real gap — a rule author may not
control (or think about) whether inbound links include a trailing slash.

Scope, confirmed with the user: **matching flexibility only** — a stored
exact-match rule should fire regardless of a trailing-slash mismatch between
the request path and the stored `OldUrl`. Explicitly **not** in scope: a
site-wide canonical trailing-slash redirect policy (e.g. "always strip
trailing slashes across the whole site") — that's a different, independent
feature (and would need its own spec if wanted later).

## Design

### Approach: a third fallback lookup, not storage-side normalization

`RedirectMiddleware.InvokeAsync` already tries two lookups in sequence:

```csharp
var redirect = redirectService.GetByOldUrl(pathAndQuery, domain);
if (redirect == null && pathAndQuery != path)
    redirect = redirectService.GetByOldUrl(path, domain);
```

Add a third fallback: if both of those miss, try `path` with its trailing
slash toggled (added if absent, removed if present), skipping the toggle
entirely for the root path `/` (toggling `/` is meaningless).

This is a **runtime fallback**, not a data migration. Existing rows keep
whatever `OldUrl` value they already have (with or without a trailing
slash) — no migration needed, no risk of rewriting existing customer data.
This matches the roadmap doc's own original framing of this feature
("normalize the path in the middleware; if not found, retry with `/path` ↔
`/path/`") rather than normalizing at storage time.

**Only applies to exact-match rules.** Regex rules are excluded — a regex
rule's author fully controls whether their pattern accounts for a trailing
slash (e.g. `^/blog/post-1/?$`), and silently retrying with a toggled path
against arbitrary user-authored regexes risks surprising/duplicate matches.
The existing regex lookup path (`FindRegexRedirect`) is untouched.

### Implementation

`Middleware/RedirectMiddleware.cs`:

```csharp
private static string? ToggleTrailingSlash(string path)
{
    if (string.IsNullOrEmpty(path) || path == "/")
        return null;

    return path.EndsWith("/", StringComparison.Ordinal)
        ? path.TrimEnd('/')
        : path + "/";
}
```

In `InvokeAsync`, after the existing two-step lookup:

```csharp
var redirect = redirectService.GetByOldUrl(pathAndQuery, domain);
if (redirect == null && pathAndQuery != path)
    redirect = redirectService.GetByOldUrl(path, domain);
if (redirect == null)
{
    var toggledPath = ToggleTrailingSlash(path);
    if (toggledPath != null)
        redirect = redirectService.GetByOldUrl(toggledPath, domain);
}
```

No changes to `RedirectService`, `RedirectEntry`, the API, or either
dashboard UI — this is entirely a middleware lookup-path change.

### Performance

Worst case (no rule matches at all — the existing 404/passthrough path)
now does up to 3 exact-match DB lookups instead of 2. Each `GetByOldUrl`
call is a single indexed-equality query; this is the same class of cost
the existing 2-lookup fallback already accepts, so a third lookup is a
proportionate, not a step-change, cost increase. No caching added — matches
the existing behavior for exact-match lookups (only regex entries are
cached today).

## Decisions confirmed with user (2026-07-08)

- Matching-flexibility only, not a site-wide trailing-slash redirect policy.
- Runtime fallback lookup, not storage-side normalization or a data
  migration.
- Exact-match rules only; regex rules unaffected.

## Out of scope

- Site-wide canonical trailing-slash enforcement (separate potential
  feature).
- Retroactively normalizing existing `OldUrl` values in the database.
- Any change to regex rule matching.
