# Trailing Slash Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An exact-match redirect rule fires regardless of a trailing-slash mismatch between the request path and the stored `OldUrl` (e.g. a rule for `/sayfa` also fires for `/sayfa/`), without any data migration.

**Architecture:** Add a third fallback lookup to `RedirectMiddleware.InvokeAsync`'s existing two-step exact-match lookup — if both existing lookups miss, retry once more with the request path's trailing slash toggled (added if absent, removed if present). Regex rules and the root path `/` are excluded.

**Tech Stack:** ASP.NET Core middleware, C#.

Reference spec: `docs/superpowers/specs/2026-07-08-trailing-slash-normalization-design.md`

---

### Task 1: Add trailing-slash fallback lookup to `RedirectMiddleware`

**Files:**
- Modify: `Middleware/RedirectMiddleware.cs`

- [ ] **Step 1: Add the `ToggleTrailingSlash` helper method**

Current (lines 199-218), the existing `ShouldSkipRedirect` static method (the new helper goes right after it, before the closing class brace):

```csharp
    private static bool ShouldSkipRedirect(string path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        var skipPaths = new[]
        {
            "/umbraco",
            "/api",
            "/install",
            "/app_plugins",
            "/media",
            "/scripts",
            "/css",
            "/images",
            "/fonts"
        };

        return skipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
```

Replace with:

```csharp
    private static bool ShouldSkipRedirect(string path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        var skipPaths = new[]
        {
            "/umbraco",
            "/api",
            "/install",
            "/app_plugins",
            "/media",
            "/scripts",
            "/css",
            "/images",
            "/fonts"
        };

        return skipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    // Lets an exact-match rule fire regardless of a trailing-slash mismatch
    // between the request path and the stored OldUrl (e.g. a rule for
    // "/sayfa" also fires for "/sayfa/"). Returns null for the root path,
    // where toggling a trailing slash is meaningless.
    private static string? ToggleTrailingSlash(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return null;

        return path.EndsWith("/", StringComparison.Ordinal)
            ? path.TrimEnd('/')
            : path + "/";
    }
}
```

- [ ] **Step 2: Add the third fallback lookup in `InvokeAsync`**

Current (lines 50-52):

```csharp
        var redirect = redirectService.GetByOldUrl(pathAndQuery, domain);
        if (redirect == null && pathAndQuery != path)
            redirect = redirectService.GetByOldUrl(path, domain);
```

Replace with:

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

This only affects the exact-match lookup path. `FindRegexRedirect`/`FindRegexMatchIn` (regex rule matching, lines 123-185) are untouched — regex rules are explicitly out of scope per the spec.

- [ ] **Step 3: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit**

```bash
git add Middleware/RedirectMiddleware.cs
git commit -m "$(cat <<'EOF'
feat: match exact redirect rules regardless of trailing-slash mismatch

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Manual verification against a live Umbraco site

**Files:** none (verification only)

Following the same live-test-site pattern already established this
session (`/Users/bhan/Desktop/u18`, local BaGet feed, `sql2022` Docker
container) — this repo does not have a runnable Umbraco host or test
project, so this is a real, executable verification, not a deferred one.

- [ ] **Step 1: Push the new build to the local feed and update the test site**

```bash
./scripts/push-to-feed.sh
```

(Requires `nuget.config` to temporarily include the local BaGet source
with `allowInsecureConnections="true"` — see prior session's pattern; add
it before testing and remove it again afterward, it's local-dev-only and
should not be committed.)

Then in `/Users/bhan/Desktop/u18/MyProject`, bump the pinned version in
`Directory.Packages.props` to match, clear the NuGet HTTP cache
(`dotnet nuget locals http-cache --clear`), delete the cached package
(`rm -rf ~/.nuget/packages/bt.redirectmanager`), and do a clean
`rm -rf obj bin && dotnet build -c Debug` — confirm no `NU1603` warning
(package version actually resolved, not silently falling back to an
older cached version — this exact failure mode bit the session's earlier
testing and is worth guarding against explicitly).

- [ ] **Step 2: Create a test rule and verify both directions**

Start the test site (`dotnet run --no-build -c Debug`). In the Redirect
Manager dashboard, create an exact-match rule with **no** trailing slash,
e.g. `/slash-test` → `/slash-target` (301). Then:

```bash
curl -sk -o /dev/null -w "no slash -> %{http_code}\n" https://localhost:44365/slash-test
curl -sk -o /dev/null -w "with slash -> %{http_code}\n" https://localhost:44365/slash-test/
```

Expected: both return `301` with `Location: /slash-target`.

Edit the same rule to add a trailing slash to its Old URL (`/slash-test/`),
save, and repeat both `curl` calls — expected: both still return `301`
(now matching in the other direction, confirming the toggle works
regardless of which form was stored).

- [ ] **Step 3: Confirm root path and regex rules are unaffected**

```bash
curl -sk -o /dev/null -w "root -> %{http_code}\n" https://localhost:44365/
```

Expected: no error/exception in the app logs from this request (confirms
`ToggleTrailingSlash` correctly returns `null` for `/` rather than
producing something like `//` or an empty string that could match
unintended rules).

Create a regex rule matching `^/regex-test$` → `/regex-target`, and
confirm `https://localhost:44365/regex-test/` (with trailing slash) does
**NOT** redirect (404s or falls through) — confirming the fallback is
scoped to exact-match rules only, not applied to regex matching.

- [ ] **Step 4: Record the result**

Report back: did all three checks match expectations? Any deviation means
returning to Task 1 rather than considering this done.

---

## Out of scope for this plan

- Site-wide canonical trailing-slash redirect policy (a separate,
  independent feature per the spec).
- Retroactively normalizing existing `OldUrl` values in the database.
- Any change to regex rule matching or the dashboard UIs.
