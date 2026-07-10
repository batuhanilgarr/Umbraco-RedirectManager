# Simple Wildcard (*) Matching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let editors write `/blog/*` → `/haberler/*` style rules without knowing regex syntax — auto-detected whenever `IsRegex` is off and `OldUrl` contains exactly one `*`, with no new database column.

**Architecture:** A new shared static helper (`WildcardPatternBuilder`) turns a literal `OldUrl` like `/blog/*` into an anchored regex pattern (`Regex.Escape` on both sides of the single `*`, joined by a capturing `(.*)`). `RedirectService` gains a cached `GetActiveWildcardEntries()` query (mirroring `GetActiveRegexEntries()` exactly, filtered by `OldUrl LIKE '%*%'` instead of `IsRegex = 1`). `RedirectMiddleware` gains a new matching pass between the existing exact-match and regex-match passes, reusing the same per-request structure (domain-scoped lookup, `AppendPreservedQueryString`, hit tracking) already used for regex. The `Test` endpoint gains an equivalent third check (a deliberate deviation from two prior sub-projects' "leave Test alone" precedent, since this feature's audience specifically depends on Test to verify a rule without reading a translated regex). Both dashboards get a "Wildcard" list-pill state and OldUrl/NewUrl hint text, computed client-side with no new DTO field.

**Tech Stack:** `System.Text.RegularExpressions.Regex` (unchanged engine, new pattern-building path), NPoco via `IScopeProvider` (unchanged), `IMemoryCache` (new cache key, same 30s TTL pattern), Lit and AngularJS dashboards (unchanged tech, new client-side computed label).

Reference spec: `docs/superpowers/specs/2026-07-10-simple-wildcard-matching-design.md`

This is sub-project 3 of 9 in the current roadmap batch. No version bump/release happens here — that is a separate step once all 9 sub-projects are done.

---

### Task 1: Add the shared `WildcardPatternBuilder` helper

**Files:**
- Create: `Services/WildcardPatternBuilder.cs`

This is a small, single-purpose static class — the same "narrow, shared helper" shape as the existing `Services/DomainNormalizer.cs` — used by both `RedirectMiddleware` (live matching, Task 3) and `RedirectApiController` (the Test URL tool, Task 4), so the escape-and-anchor translation logic exists in exactly one place rather than being duplicated across the two files that need it. This task has no dependency on anything else in the plan and leaves the project in a fully-building state on its own.

- [ ] **Step 1: Write the helper**

```csharp
using System.Text.RegularExpressions;

namespace Umbraco.RedirectManager.Services;

public static class WildcardPatternBuilder
{
    /// <summary>
    /// Splits <paramref name="oldUrl"/> at its first '*' into a prefix and
    /// suffix, escapes each side (so a literal "." or "+" in the URL is
    /// treated as a literal character, not a regex metacharacter -- the
    /// whole point of this being usable without regex knowledge), and joins
    /// them with a capturing "(.*)", anchored at both ends so this behaves
    /// as a whole-path match rather than a substring match (an unanchored
    /// pattern would also match a path that merely contains the prefix and
    /// suffix somewhere in the middle). If <paramref name="oldUrl"/> has no
    /// '*' at all, it's escaped and anchored as a literal exact-match
    /// pattern -- a defensive fallback, since callers are only expected to
    /// pass values already confirmed to contain '*'.
    /// </summary>
    public static string BuildRegexPattern(string oldUrl)
    {
        var starIndex = oldUrl.IndexOf('*');
        if (starIndex < 0)
            return "^" + Regex.Escape(oldUrl) + "$";

        var prefix = oldUrl[..starIndex];
        var suffix = oldUrl[(starIndex + 1)..];
        return "^" + Regex.Escape(prefix) + "(.*)" + Regex.Escape(suffix) + "$";
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Services/WildcardPatternBuilder.cs
git commit -m "$(cat <<'EOF'
feat: add WildcardPatternBuilder helper for translating * patterns to regex

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add `GetActiveWildcardEntries()` to the service layer and rename the cache-invalidation method

**Files:**
- Modify: `Services/IRedirectService.cs`
- Modify: `Services/RedirectService.cs`

Both files are changed together in this task (rather than split across two tasks) because an interface method without its implementation would leave the project in a non-building state — each task in this plan should leave the project building cleanly on its own.

- [ ] **Step 1: Add the method signature to `IRedirectService`**

Current (full file):

```csharp
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectService
{
    IEnumerable<RedirectEntry> GetAll();
    IEnumerable<RedirectEntry> GetAllFiltered(string? query, int? statusCode, bool? isActive, bool? isRegex);
    RedirectEntry? GetById(int id);
    RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null);
    RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null);
    IEnumerable<RedirectEntry> GetActiveRegexEntries();
    RedirectEntry Create(CreateRedirectEntryDto dto);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
}
```

Replace with:

```csharp
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectService
{
    IEnumerable<RedirectEntry> GetAll();
    IEnumerable<RedirectEntry> GetAllFiltered(string? query, int? statusCode, bool? isActive, bool? isRegex);
    RedirectEntry? GetById(int id);
    RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null);
    RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null);
    IEnumerable<RedirectEntry> GetActiveRegexEntries();
    IEnumerable<RedirectEntry> GetActiveWildcardEntries();
    RedirectEntry Create(CreateRedirectEntryDto dto);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
}
```

- [ ] **Step 2: Add the new cache key constant to `RedirectService`**

Current:

```csharp
    private const string ActiveRegexCacheKey = "RedirectManager.ActiveRegexEntries";
```

Replace with:

```csharp
    private const string ActiveRegexCacheKey = "RedirectManager.ActiveRegexEntries";
    private const string ActiveWildcardCacheKey = "RedirectManager.ActiveWildcardEntries";
```

- [ ] **Step 3: Add `GetActiveWildcardEntries()`, right after `GetActiveRegexEntries()`**

Current:

```csharp
    public IEnumerable<RedirectEntry> GetActiveRegexEntries()
    {
        return _memoryCache.GetOrCreate(ActiveRegexCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            using var scope = _scopeProvider.CreateScope();
            var now = DateTime.UtcNow;
            var results = scope.Database.Fetch<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE IsActive = 1 AND IsRegex = 1 AND (ValidFrom IS NULL OR ValidFrom <= @0) AND (ValidUntil IS NULL OR ValidUntil >= @0) ORDER BY CreatedDate DESC",
                now);
            scope.Complete();
            return results;
        }) ?? Enumerable.Empty<RedirectEntry>();
    }

    public IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts()
```

Replace with:

```csharp
    public IEnumerable<RedirectEntry> GetActiveRegexEntries()
    {
        return _memoryCache.GetOrCreate(ActiveRegexCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            using var scope = _scopeProvider.CreateScope();
            var now = DateTime.UtcNow;
            var results = scope.Database.Fetch<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE IsActive = 1 AND IsRegex = 1 AND (ValidFrom IS NULL OR ValidFrom <= @0) AND (ValidUntil IS NULL OR ValidUntil >= @0) ORDER BY CreatedDate DESC",
                now);
            scope.Complete();
            return results;
        }) ?? Enumerable.Empty<RedirectEntry>();
    }

    public IEnumerable<RedirectEntry> GetActiveWildcardEntries()
    {
        return _memoryCache.GetOrCreate(ActiveWildcardCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            using var scope = _scopeProvider.CreateScope();
            var now = DateTime.UtcNow;
            var results = scope.Database.Fetch<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE IsActive = 1 AND IsRegex = 0 AND OldUrl LIKE '%*%' AND (ValidFrom IS NULL OR ValidFrom <= @0) AND (ValidUntil IS NULL OR ValidUntil >= @0) ORDER BY CreatedDate DESC",
                now);
            scope.Complete();
            return results;
        }) ?? Enumerable.Empty<RedirectEntry>();
    }

    public IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts()
```

- [ ] **Step 4: Rename `InvalidateRegexCache` to `InvalidateMatchCaches` and clear both cache keys**

Current (this anchor includes the following method, `NormalizeUrl`, purely so the text is uniquely located in the file — `NormalizeUrl` itself is untouched by this edit):

```csharp
    private void InvalidateRegexCache() => _memoryCache.Remove(ActiveRegexCacheKey);

    private static string NormalizeUrl(string url)
```

Replace with:

```csharp
    private void InvalidateMatchCaches()
    {
        _memoryCache.Remove(ActiveRegexCacheKey);
        _memoryCache.Remove(ActiveWildcardCacheKey);
    }

    private static string NormalizeUrl(string url)
```

- [ ] **Step 5: Update the five call sites that invoke the renamed method**

Do these five edits in any order — none of their anchors overlap with each other or with Step 4's anchor (which ends at `NormalizeUrl`, untouched here), so there is no ordering dependency anywhere in this task.

`Create` (ends with `return entry;`):

Current:

```csharp
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Insert(entry);
        scope.Complete();

        InvalidateRegexCache();

        return entry;
    }
```

Replace with:

```csharp
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Insert(entry);
        scope.Complete();

        InvalidateMatchCaches();

        return entry;
    }
```

`Update` (ends with `return existing;`):

Current:

```csharp
        scope.Database.Update(existing);
        scope.Complete();

        InvalidateRegexCache();

        return existing;
    }
```

Replace with:

```csharp
        scope.Database.Update(existing);
        scope.Complete();

        InvalidateMatchCaches();

        return existing;
    }
```

`Delete`:

Current:

```csharp
    public bool Delete(int id)
    {
        using var scope = _scopeProvider.CreateScope();
        var rowsAffected = scope.Database.Delete<RedirectEntry>(id);
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateRegexCache();
        }

        return rowsAffected > 0;
    }
```

Replace with:

```csharp
    public bool Delete(int id)
    {
        using var scope = _scopeProvider.CreateScope();
        var rowsAffected = scope.Database.Delete<RedirectEntry>(id);
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateMatchCaches();
        }

        return rowsAffected > 0;
    }
```

`BulkDelete`:

Current:

```csharp
        var rowsAffected = scope.Database.Execute(sql, idList.Cast<object>().ToArray());
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateRegexCache();
        }

        return rowsAffected;
    }

    public int BulkSetActive(IEnumerable<int> ids, bool isActive)
```

Replace with:

```csharp
        var rowsAffected = scope.Database.Execute(sql, idList.Cast<object>().ToArray());
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateMatchCaches();
        }

        return rowsAffected;
    }

    public int BulkSetActive(IEnumerable<int> ids, bool isActive)
```

`BulkSetActive` (identify it by the preceding unique SQL line — the only `if (rowsAffected > 0)` block preceded by this exact `UPDATE` statement):

Current:

```csharp
        var sql = $"UPDATE {RedirectEntry.TableName} SET IsActive = @0, UpdatedDate = @1 WHERE Id IN ({placeholders})";
        var rowsAffected = scope.Database.Execute(sql, args.ToArray());
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateRegexCache();
        }

        return rowsAffected;
    }
```

Replace with:

```csharp
        var sql = $"UPDATE {RedirectEntry.TableName} SET IsActive = @0, UpdatedDate = @1 WHERE Id IN ({placeholders})";
        var rowsAffected = scope.Database.Execute(sql, args.ToArray());
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateMatchCaches();
        }

        return rowsAffected;
    }
```

- [ ] **Step 6: Search the file for any remaining `InvalidateRegexCache` references**

```bash
grep -n "InvalidateRegexCache" Services/RedirectService.cs
```

Expected: no output (zero matches) — every call site and the method itself should now say `InvalidateMatchCaches`.

- [ ] **Step 7: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 8: Commit**

```bash
git add Services/IRedirectService.cs Services/RedirectService.cs
git commit -m "$(cat <<'EOF'
feat: add GetActiveWildcardEntries and rename cache invalidation to cover it

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Match wildcard rules in `RedirectMiddleware`

**Files:**
- Modify: `Middleware/RedirectMiddleware.cs`

- [ ] **Step 1: Add the `WildcardRegexCache` field, right after `RegexCache`**

Current:

```csharp
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
```

Replace with:

```csharp
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly ConcurrentDictionary<string, Regex> WildcardRegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
```

- [ ] **Step 2: Insert the wildcard-match block in `InvokeAsync`, between the exact-match block and the regex-match line**

Current:

```csharp
                case 410:
                    _hitTracker.RecordHit(redirect.Id);
                    context.Response.StatusCode = 410;
                    await context.Response.WriteAsync("Gone");
                    return;
            }
        }

        var regexRedirect = FindRegexRedirect(path, domain, redirectService);
```

Replace with:

```csharp
                case 410:
                    _hitTracker.RecordHit(redirect.Id);
                    context.Response.StatusCode = 410;
                    await context.Response.WriteAsync("Gone");
                    return;
            }
        }

        var wildcardRedirect = FindWildcardRedirect(path, domain, redirectService);
        if (wildcardRedirect != null)
        {
            _logger.LogDebug("Wildcard redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                wildcardRedirect.Entry.OldUrl, wildcardRedirect.ComputedNewUrl, wildcardRedirect.Entry.StatusCode);
            _hitTracker.RecordHit(wildcardRedirect.Entry.Id);

            switch (wildcardRedirect.Entry.StatusCode)
            {
                case 301:
                    context.Response.StatusCode = 301;
                    context.Response.Headers.Location = AppendPreservedQueryString(
                        wildcardRedirect.ComputedNewUrl, wildcardRedirect.Entry.PreserveQueryString, context.Request.QueryString) ?? "/";
                    return;

                case 302:
                    context.Response.StatusCode = 302;
                    context.Response.Headers.Location = AppendPreservedQueryString(
                        wildcardRedirect.ComputedNewUrl, wildcardRedirect.Entry.PreserveQueryString, context.Request.QueryString) ?? "/";
                    return;

                case 404:
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Not Found");
                    return;

                case 410:
                    context.Response.StatusCode = 410;
                    await context.Response.WriteAsync("Gone");
                    return;
            }
        }

        var regexRedirect = FindRegexRedirect(path, domain, redirectService);
```

Note: this is intentionally a near-identical copy of the switch statement already used for the exact-match and regex-match blocks above it — consistent with this file's existing style, which already has two such blocks before this change.

- [ ] **Step 3: Add `FindWildcardRedirect`/`FindWildcardMatchIn`, right after `FindRegexMatchIn`**

Current:

```csharp
            return new RedirectMatch(r, newUrl);
        }

        return null;
    }

    private sealed class RedirectMatch
```

Replace with:

```csharp
            return new RedirectMatch(r, newUrl);
        }

        return null;
    }

    private RedirectMatch? FindWildcardRedirect(string path, string? domain, IRedirectService redirectService)
    {
        try
        {
            var entries = redirectService.GetActiveWildcardEntries();

            if (domain != null)
            {
                var domainMatch = FindWildcardMatchIn(entries.Where(r => r.Domain == domain), path);
                if (domainMatch != null)
                    return domainMatch;
            }

            return FindWildcardMatchIn(entries.Where(r => string.IsNullOrEmpty(r.Domain)), path);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.LogWarning(ex, "Wildcard redirect match timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating wildcard redirects");
        }

        return null;
    }

    private RedirectMatch? FindWildcardMatchIn(IEnumerable<Umbraco.RedirectManager.Models.RedirectEntry> candidates, string path)
    {
        foreach (var r in candidates)
        {
            if (string.IsNullOrWhiteSpace(r.OldUrl))
                continue;

            var regex = WildcardRegexCache.GetOrAdd(r.OldUrl, pattern =>
                new Regex(WildcardPatternBuilder.BuildRegexPattern(pattern), RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout));

            if (!regex.IsMatch(path))
                continue;

            var newUrl = r.NewUrl;

            if ((r.StatusCode == 301 || r.StatusCode == 302) && !string.IsNullOrWhiteSpace(newUrl))
            {
                try
                {
                    newUrl = regex.Replace(path, newUrl.Replace("*", "$1", StringComparison.Ordinal));
                }
                catch
                {
                    // If replace fails, fall back to original NewUrl
                }
            }

            return new RedirectMatch(r, newUrl);
        }

        return null;
    }

    private sealed class RedirectMatch
```

- [ ] **Step 4: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 5: Commit**

```bash
git add Middleware/RedirectMiddleware.cs
git commit -m "$(cat <<'EOF'
feat: match wildcard (*) redirect rules in RedirectMiddleware

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Support wildcard matching in the `Test` endpoint

**Files:**
- Modify: `Controllers/RedirectApiController.cs`

- [ ] **Step 1: Insert the wildcard check between the exact-match check and the regex-entries loop**

Current:

```csharp
        var exact = _redirectService.GetByOldUrl(normalizedPath);
        if (exact != null)
        {
            return Ok(new
            {
                matched = true,
                matchType = "Exact",
                redirect = ToDto(exact),
                computedNewUrl = exact.NewUrl
            });
        }

        foreach (var r in _redirectService.GetActiveRegexEntries())
        {
```

Replace with:

```csharp
        var exact = _redirectService.GetByOldUrl(normalizedPath);
        if (exact != null)
        {
            return Ok(new
            {
                matched = true,
                matchType = "Exact",
                redirect = ToDto(exact),
                computedNewUrl = exact.NewUrl
            });
        }

        foreach (var r in _redirectService.GetActiveWildcardEntries())
        {
            if (string.IsNullOrWhiteSpace(r.OldUrl))
                continue;

            Regex wildcardRegex;
            try
            {
                wildcardRegex = new Regex(WildcardPatternBuilder.BuildRegexPattern(r.OldUrl), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch
            {
                continue;
            }

            bool wildcardMatched;
            try
            {
                wildcardMatched = wildcardRegex.IsMatch(normalizedPath);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!wildcardMatched)
                continue;

            var wildcardNewUrl = r.NewUrl;
            if ((r.StatusCode == 301 || r.StatusCode == 302) && !string.IsNullOrWhiteSpace(wildcardNewUrl))
            {
                try
                {
                    wildcardNewUrl = wildcardRegex.Replace(normalizedPath, wildcardNewUrl.Replace("*", "$1", StringComparison.Ordinal));
                }
                catch
                {
                    // ignore
                }
            }

            return Ok(new
            {
                matched = true,
                matchType = "Wildcard",
                redirect = ToDto(r),
                computedNewUrl = wildcardNewUrl
            });
        }

        foreach (var r in _redirectService.GetActiveRegexEntries())
        {
```

Note: this endpoint already builds its own local `Regex` objects (it doesn't share the middleware's `RegexCache`/`WildcardRegexCache` static dictionaries — see the existing regex loop right below, which does the same thing), so this follows that existing local-construction pattern rather than introducing new shared caching. It does, however, reuse the shared `WildcardPatternBuilder.BuildRegexPattern` translation logic, so a fix to that logic never needs to be duplicated.

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: support wildcard matching in the Test URL endpoint

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Add wildcard hints, the "Wildcard" list pill, and validation to the Lit dashboard (Umbraco 17+/18)

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add the `getMatchTypeLabel` helper, right after `getScheduleBadge`**

Current:

```javascript
    getScheduleBadge(redirect) {
        const now = new Date();
        if (redirect.validFrom && new Date(redirect.validFrom) > now) return 'Scheduled';
        if (redirect.validUntil && new Date(redirect.validUntil) < now) return 'Expired';
        return null;
    }

    getMissedRequestTitle(item) {
```

Replace with:

```javascript
    getScheduleBadge(redirect) {
        const now = new Date();
        if (redirect.validFrom && new Date(redirect.validFrom) > now) return 'Scheduled';
        if (redirect.validUntil && new Date(redirect.validUntil) < now) return 'Expired';
        return null;
    }

    getMatchTypeLabel(redirect) {
        if (redirect.isRegex) return 'Regex';
        if (redirect.oldUrl && redirect.oldUrl.includes('*')) return 'Wildcard';
        return 'Exact';
    }

    getMissedRequestTitle(item) {
```

- [ ] **Step 2: Add the "one wildcard max" validation in `saveRedirect`**

Current:

```javascript
    async saveRedirect() {
        if (!this.formData.oldUrl) {
            this.showMessage('Old URL is required', 'error');
            return;
        }

        if ((this.formData.statusCode === 301 || this.formData.statusCode === 302) && !this.formData.newUrl) {
            this.showMessage('New URL is required for redirect status codes', 'error');
            return;
        }

        if (this.formData.abTestEnabled && !this.formData.variantBUrl) {
```

Replace with:

```javascript
    async saveRedirect() {
        if (!this.formData.oldUrl) {
            this.showMessage('Old URL is required', 'error');
            return;
        }

        if ((this.formData.oldUrl.match(/\*/g) || []).length > 1) {
            this.showMessage('Old URL can only contain one wildcard (*)', 'error');
            return;
        }

        if ((this.formData.statusCode === 301 || this.formData.statusCode === 302) && !this.formData.newUrl) {
            this.showMessage('New URL is required for redirect status codes', 'error');
            return;
        }

        if ((this.formData.newUrl.match(/\*/g) || []).length > 1) {
            this.showMessage('New URL can only contain one wildcard (*)', 'error');
            return;
        }

        if (this.formData.abTestEnabled && !this.formData.variantBUrl) {
```

- [ ] **Step 3: Update the Old URL / New URL hint text**

Current:

```javascript
                                    <div class="form-group">
                                        <label>Old URL <span class="req">*</span></label>
                                        <input type="text"
                                               name="oldUrl"
                                               .value=${this.formData.oldUrl}
                                               @input=${this.handleInputChange}
                                               placeholder="/old-page" />
                                        <small>The path to redirect from.</small>
                                    </div>
                                    ${this.formData.statusCode === 301 || this.formData.statusCode === 302 ? html`
                                        <div class="form-group">
                                            <label>New URL <span class="req">*</span></label>
                                            <input type="text"
                                                   name="newUrl"
                                                   .value=${this.formData.newUrl}
                                                   @input=${this.handleInputChange}
                                                   placeholder="/new-page" />
                                            <small>The path to redirect to.</small>
                                        </div>
                                    ` : ''}
```

Replace with:

```javascript
                                    <div class="form-group">
                                        <label>Old URL <span class="req">*</span></label>
                                        <input type="text"
                                               name="oldUrl"
                                               .value=${this.formData.oldUrl}
                                               @input=${this.handleInputChange}
                                               placeholder="/old-page" />
                                        <small>The path to redirect from. Tip: use * to match anything (e.g. /blog/*).</small>
                                    </div>
                                    ${this.formData.statusCode === 301 || this.formData.statusCode === 302 ? html`
                                        <div class="form-group">
                                            <label>New URL <span class="req">*</span></label>
                                            <input type="text"
                                                   name="newUrl"
                                                   .value=${this.formData.newUrl}
                                                   @input=${this.handleInputChange}
                                                   placeholder="/new-page" />
                                            <small>The path to redirect to. Use * to reuse the matched value (e.g. /articles/*).</small>
                                        </div>
                                    ` : ''}
```

- [ ] **Step 4: Update the list table's type pill**

Current:

```javascript
                                        <td class="center">
                                            <span class="type-pill ${redirect.isRegex ? 'regex' : ''}">
                                                ${redirect.isRegex ? 'Regex' : 'Exact'}
                                            </span>
```

Replace with:

```javascript
                                        <td class="center">
                                            <span class="type-pill ${redirect.isRegex ? 'regex' : (redirect.oldUrl && redirect.oldUrl.includes('*') ? 'wildcard' : '')}">
                                                ${this.getMatchTypeLabel(redirect)}
                                            </span>
```

IMPORTANT: locate this exact block by its distinctive content (the `type-pill ${redirect.isRegex ...}` expression) — there is exactly ONE such block in the file (the redirects list table). If you find more than one match or can't find an unambiguous one, STOP and report BLOCKED describing what you found.

- [ ] **Step 5: Add the `.type-pill.wildcard` CSS, right after `.type-pill.regex`**

Current:

```javascript
        .type-pill.regex {
            background: #f3f0fe;
            border-color: #d0c8f7;
            color: #5b21b6;
        }

        .type-pill.ab-pill {
```

Replace with:

```javascript
        .type-pill.regex {
            background: #f3f0fe;
            border-color: #d0c8f7;
            color: #5b21b6;
        }

        .type-pill.wildcard {
            background: #fef9c3;
            border-color: #fde047;
            color: #854d0e;
        }

        .type-pill.ab-pill {
```

- [ ] **Step 6: Build to confirm the .NET project still compiles, then verify JS syntax**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
node --check App_Plugins/RedirectManager/redirect-dashboard.js
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`, and `node --check` produces no output.

- [ ] **Step 7: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "$(cat <<'EOF'
feat: add wildcard hints, Wildcard list pill, and validation to the Lit dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Add wildcard hints, the "Wildcard" list pill, and validation to the AngularJS dashboard (Umbraco 13)

**Files:**
- Modify: `App_Plugins/RedirectManager/modal.html`
- Modify: `App_Plugins/RedirectManager/dashboard.html`
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`
- Modify: `App_Plugins/RedirectManager/redirect.css`

- [ ] **Step 1: Add the `vm.getMatchTypeLabel` helper, right after `vm.getScheduleBadge`**

Current:

```javascript
        vm.getScheduleBadge = function (redirect) {
            var now = new Date();
            if (redirect.validFrom && new Date(redirect.validFrom) > now) {
                return "Scheduled";
            }
            if (redirect.validUntil && new Date(redirect.validUntil) < now) {
                return "Expired";
            }
            return null;
        };

        vm.loadRedirects = function () {
```

Replace with:

```javascript
        vm.getScheduleBadge = function (redirect) {
            var now = new Date();
            if (redirect.validFrom && new Date(redirect.validFrom) > now) {
                return "Scheduled";
            }
            if (redirect.validUntil && new Date(redirect.validUntil) < now) {
                return "Expired";
            }
            return null;
        };

        vm.getMatchTypeLabel = function (redirect) {
            if (redirect.isRegex) {
                return "Regex";
            }
            if (redirect.oldUrl && redirect.oldUrl.indexOf("*") !== -1) {
                return "Wildcard";
            }
            return "Exact";
        };

        vm.loadRedirects = function () {
```

- [ ] **Step 2: Add the "one wildcard max" validation in `vm.saveRedirect`**

Current:

```javascript
            if (!redirect.oldUrl) {
                notificationsService.error("Validation Error", "Old URL is required");
                return;
            }

            if ((redirect.statusCode === 301 || redirect.statusCode === 302) && !redirect.newUrl) {
                notificationsService.error("Validation Error", "New URL is required for redirect status codes");
                return;
            }

            if (redirect.abTestEnabled && !redirect.variantBUrl) {
```

Replace with:

```javascript
            if (!redirect.oldUrl) {
                notificationsService.error("Validation Error", "Old URL is required");
                return;
            }

            if ((redirect.oldUrl.match(/\*/g) || []).length > 1) {
                notificationsService.error("Validation Error", "Old URL can only contain one wildcard (*)");
                return;
            }

            if ((redirect.statusCode === 301 || redirect.statusCode === 302) && !redirect.newUrl) {
                notificationsService.error("Validation Error", "New URL is required for redirect status codes");
                return;
            }

            if ((redirect.newUrl.match(/\*/g) || []).length > 1) {
                notificationsService.error("Validation Error", "New URL can only contain one wildcard (*)");
                return;
            }

            if (redirect.abTestEnabled && !redirect.variantBUrl) {
```

- [ ] **Step 3: Update the Old URL / New URL hint text in `modal.html`**

Current:

```html
            <umb-control-group label="Old URL"
                               description="The URL path to redirect from"
                               required="true">
                <input type="text"
                       ng-model="model.redirect.oldUrl"
                       class="umb-property-editor umb-textstring"
                       placeholder="/old-page"
                       required>
            </umb-control-group>

            <umb-control-group label="New URL"
                               description="The URL path to redirect to"
                               ng-if="model.redirect.statusCode == 301 || model.redirect.statusCode == 302">
                <input type="text"
                       ng-model="model.redirect.newUrl"
                       class="umb-property-editor umb-textstring"
                       placeholder="/new-page">
            </umb-control-group>
```

Replace with:

```html
            <umb-control-group label="Old URL"
                               description="The URL path to redirect from. Tip: use * to match anything (e.g. /blog/*)."
                               required="true">
                <input type="text"
                       ng-model="model.redirect.oldUrl"
                       class="umb-property-editor umb-textstring"
                       placeholder="/old-page"
                       required>
            </umb-control-group>

            <umb-control-group label="New URL"
                               description="The URL path to redirect to. Use * to reuse the matched value (e.g. /articles/*)."
                               ng-if="model.redirect.statusCode == 301 || model.redirect.statusCode == 302">
                <input type="text"
                       ng-model="model.redirect.newUrl"
                       class="umb-property-editor umb-textstring"
                       placeholder="/new-page">
            </umb-control-group>
```

- [ ] **Step 4: Update the list table's type pill in `dashboard.html`**

Current:

```html
                            <td style="text-align:center;">
                                <span class="type-pill" ng-class="{'regex': redirect.isRegex}">
                                    {{redirect.isRegex ? 'Regex' : 'Exact'}}
                                </span>
```

Replace with:

```html
                            <td style="text-align:center;">
                                <span class="type-pill"
                                      ng-class="{'regex': redirect.isRegex, 'wildcard': !redirect.isRegex && redirect.oldUrl.indexOf('*') !== -1}">
                                    {{vm.getMatchTypeLabel(redirect)}}
                                </span>
```

IMPORTANT: locate this exact block by its distinctive content (the `class="type-pill" ng-class="{'regex': redirect.isRegex}"` markup) — there is exactly ONE such block in `dashboard.html`. If you find more than one match or can't find an unambiguous one, STOP and report BLOCKED describing what you found.

- [ ] **Step 5: Add the `.type-pill.wildcard` CSS to `redirect.css`, right after `.type-pill.regex`**

Current:

```css
.type-pill.regex {
    background: #f3f0fe;
    border-color: #d0c8f7;
    color: #5b21b6;
}
```

Replace with:

```css
.type-pill.regex {
    background: #f3f0fe;
    border-color: #d0c8f7;
    color: #5b21b6;
}

.type-pill.wildcard {
    background: #fef9c3;
    border-color: #fde047;
    color: #854d0e;
}
```

- [ ] **Step 6: Build to confirm the .NET project still compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 7: Commit**

```bash
git add App_Plugins/RedirectManager/modal.html App_Plugins/RedirectManager/dashboard.html App_Plugins/RedirectManager/redirect.controller.js App_Plugins/RedirectManager/redirect.css
git commit -m "$(cat <<'EOF'
feat: add wildcard hints, Wildcard list pill, and validation to the AngularJS dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

Note: both dashboards' "Test URL" result display already renders `result.matchType` generically (`` `Matched ${result.matchType} (...)` `` in the Lit dashboard, `"Matched " + result.matchType + ...` in the AngularJS controller) — neither hardcodes "Exact"/"Regex", so the new `"Wildcard"` value from Task 4 displays correctly with no further dashboard changes needed.

---

### Task 7: Manual verification — DEFERRED (documented, not executed)

Same constraint as every prior sub-project in this repo: no automated test project, no runnable Umbraco host in this repo, no local test site currently available. This documents what to run manually before this sub-project is considered done.

**Files:** none

- [ ] **Step 1 (deferred): Push to the local BaGet feed and install into a test site**

```bash
docker compose -f docker/docker-compose.yml up -d
./scripts/push-to-feed.sh
```

Then update the package in a test Umbraco site and start it.

- [ ] **Step 2 (deferred): Confirm existing exact and regex redirects still work unchanged**

Visit paths with existing exact-match and regex rules and confirm they still redirect correctly — this feature adds a new matching pass but must not change the two existing ones.

- [ ] **Step 3 (deferred): Confirm a basic wildcard redirect works**

Create a 301 rule with `Old URL = /blog/*`, `New URL = /articles/*`. Visit `/blog/my-post` and confirm it redirects to `/articles/my-post`.

- [ ] **Step 4 (deferred): Confirm literal characters in the wildcard pattern are treated literally, not as regex**

Create a rule `Old URL = /page.old/*` → `New URL = /page.new/*` (note the literal `.`). Visit `/page.old/x` and confirm it redirects to `/page.new/x`. Then visit `/pageXold/x` (replacing the `.` with a different character) and confirm it does NOT redirect — if `.` were treated as a regex metacharacter (matches any character) rather than a literal period, this would incorrectly match.

- [ ] **Step 5 (deferred): Confirm a wildcard rule with no `*` in `New URL` works**

Create a rule `Old URL = /old-section/*` → `New URL = /gone` (301). Visit `/old-section/anything` and confirm it redirects to `/gone` regardless of what follows `/old-section/`.

- [ ] **Step 6 (deferred): Confirm 404/410 wildcard rules work**

Create a rule `Old URL = /secret/*`, status code 410. Visit `/secret/anything` and confirm a 410 response, with no `New URL` needed.

- [ ] **Step 7 (deferred): Confirm domain scoping and the validity window apply to wildcard rules**

Repeat the domain-scoping test from the domain-scoped-redirects sub-project and the scheduling test from the valid-from/until sub-project, but using a wildcard rule instead of an exact one, and confirm both behave the same way (domain-specific wildcard rule wins over a global one on the matching hostname; a `ValidUntil`-expired wildcard rule stops firing).

- [ ] **Step 8 (deferred): Confirm the Test URL tool reports wildcard matches correctly in both dashboards**

Using the rule from Step 3, use each dashboard's "Test URL" button with `/blog/my-post` and confirm it reports "Matched Wildcard (301) -> /articles/my-post".

- [ ] **Step 9 (deferred): Confirm the "one wildcard max" validation**

In both dashboards' add/edit modal, try saving a rule with `Old URL = /a/*/b/*` (two wildcards) and confirm it's rejected with the "Old URL can only contain one wildcard (*)" message before any request is sent. Repeat for `New URL`.

- [ ] **Step 10 (deferred): Confirm the dashboard list pill and hint text**

Create/view a wildcard rule in both dashboards' list tables and confirm the type pill reads "Wildcard" (not "Exact"), with its own distinct color. Confirm the Old URL/New URL fields' hint text mentions `*` usage.

---

## Out of scope for this plan

- Multiple `*` wildcards in a single rule — rejected by client-side validation, per the approved spec.
- Server-side validation of the single-wildcard constraint.
- A/B testing or trailing-slash-toggle matching for wildcard rules — both already unavailable for regex rules today, and this feature doesn't change that.
- CSV export/import, `RedirectStatsBuilder`, or `GetByOldUrlAndIsRegex` changes.
- Conflict/overlap detection between a wildcard rule and another rule that could also match the same path — that's roadmap sub-project 7 ("Çakışma uyarısı"), not this one.
- Any appsettings-level configurability.
- Version bump, git tag, and NuGet publish — happens once, after all 9 sub-projects in this batch are done, as a separate step outside this plan.
