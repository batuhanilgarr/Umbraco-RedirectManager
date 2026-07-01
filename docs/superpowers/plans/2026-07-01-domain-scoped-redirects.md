# Domain-Scoped Redirects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the same `OldUrl` map to a different `NewUrl` per hostname in multi-site Umbraco installs, with domain-specific redirects taking precedence over global ones, while every existing redirect (which has no `Domain` set) keeps behaving exactly as it does today.

**Architecture:** Add a nullable `Domain` string column to `RedirectEntry`. A shared `DomainNormalizer` helper normalizes both the incoming request's host and any user-entered domain value the same way (lowercase, strip port, no `www.` stripping), so comparisons are consistent. Every lookup that currently ignores domain becomes a two-pass lookup: try an exact domain match first, then fall back to the global (`Domain IS NULL`) bucket.

**Tech Stack:** NPoco via `IScopeProvider` (unchanged), ASP.NET Core (`HttpContext.Request.Host`), Lit and AngularJS dashboards (unchanged tech, new field/column).

Reference spec: `docs/superpowers/specs/2026-07-01-domain-scoped-redirects-design.md`

This is the last of the 4-part roadmap. Once this plan is implemented and reviewed, the whole roadmap is done — a single version bump, tag, and NuGet publish (`1.3.0`) happens afterward, as a separate step outside this plan.

---

### Task 1: Add the `Domain` column to `RedirectEntry`

**Files:**
- Modify: `Models/RedirectEntry.cs`

- [ ] **Step 1: Add the property**

Current:

```csharp
    [Column("NewUrl")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(2048)]
    public string? NewUrl { get; set; }

    [Column("Description")]
```

Replace with:

```csharp
    [Column("NewUrl")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(2048)]
    public string? NewUrl { get; set; }

    [Column("Domain")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? Domain { get; set; }

    [Column("Description")]
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Models/RedirectEntry.cs
git commit -m "$(cat <<'EOF'
feat: add Domain column to RedirectEntry model

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add the `AddDomainColumn` migration step

**Files:**
- Modify: `Migrations/RedirectManagerMigrationPlan.cs`

- [ ] **Step 1: Register the new migration step**

Current:

```csharp
    protected override void DefinePlan()
    {
        To<CreateRedirectManagerTable>(new Guid("C1686EA6-A8CF-4B7E-B91F-D4519EB17FDA"));
        To<AddIsRegexAndDescriptionColumns>(new Guid("EE2670E3-75C8-4BF6-8D70-36B10D5ECC65"));
        To<AddHitCountColumns>(new Guid("4F2A8B31-6C7C-4A8E-9E22-2D4D6D9CDDF1"));
        To<CreateMissedRequestsTable>(new Guid("7A1E9C42-3B5D-4F6A-8E11-9C2D5A7B3F04"));
    }
```

Replace with:

```csharp
    protected override void DefinePlan()
    {
        To<CreateRedirectManagerTable>(new Guid("C1686EA6-A8CF-4B7E-B91F-D4519EB17FDA"));
        To<AddIsRegexAndDescriptionColumns>(new Guid("EE2670E3-75C8-4BF6-8D70-36B10D5ECC65"));
        To<AddHitCountColumns>(new Guid("4F2A8B31-6C7C-4A8E-9E22-2D4D6D9CDDF1"));
        To<CreateMissedRequestsTable>(new Guid("7A1E9C42-3B5D-4F6A-8E11-9C2D5A7B3F04"));
        To<AddDomainColumn>(new Guid("B8D4E617-2F0A-4C9B-A5D3-6E1F8C0A9B72"));
    }
```

- [ ] **Step 2: Add the async (net10.0+) migration class**

In the `#if NET10_0_OR_GREATER` block, immediately after the closing brace of
the async `CreateMissedRequestsTable` class (right before `#else`), insert:

```csharp
public class AddDomainColumn : AsyncMigrationBase
{
    public AddDomainColumn(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "Domain") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Domain");
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Add the sync (net8.0) migration class**

In the `#else` block, immediately after the closing brace of the sync
`CreateMissedRequestsTable` class (right before `#endif`), insert:

```csharp
public class AddDomainColumn : MigrationBase
{
    public AddDomainColumn(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "Domain") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Domain");
        }
    }
}
```

- [ ] **Step 4: Build to confirm both TFMs compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 5: Commit**

```bash
git add Migrations/RedirectManagerMigrationPlan.cs
git commit -m "$(cat <<'EOF'
feat: add migration for RedirectEntry.Domain column

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Add the `DomainNormalizer` helper

**Files:**
- Create: `Services/DomainNormalizer.cs`

This is shared by the middleware (to compute the request's domain from the
`Host` header) and the service layer (to normalize a user-entered domain
value before storing or comparing it), so both sides treat e.g.
`"Example.com"` and `"example.com"` as the same value, and both agree that
an empty string means "no domain" (global), not a distinct value from
`null`. Follows the existing `NormalizeUrl`/`NormalizeOldUrl` private-static
style in `RedirectService.cs`, but as a small standalone public static class
since two different files need it.

- [ ] **Step 1: Write the helper**

```csharp
namespace Umbraco.RedirectManager.Services;

public static class DomainNormalizer
{
    /// <summary>
    /// Normalizes a domain/host value: trims, lowercases, and strips a
    /// trailing ":port" suffix. Does NOT strip a "www." prefix -- an
    /// intentional choice, since Umbraco sites typically manage www/apex
    /// redirection as their own binding, and silently merging the two here
    /// could surprise anyone expecting an exact hostname match. Null or
    /// whitespace-only input normalizes to null (meaning "global"), so null
    /// and empty string are never treated as distinct values.
    /// </summary>
    public static string? Normalize(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        var value = domain.Trim().ToLowerInvariant();

        // Strip a trailing ":port" (e.g. "example.com:8080" -> "example.com").
        // Guard against IPv6 literals in brackets (e.g. "[::1]:8080"), where
        // taking the substring after the last colon would cut into the
        // address itself rather than removing a port.
        var lastColon = value.LastIndexOf(':');
        if (lastColon > 0 && value.IndexOf(']', lastColon) == -1)
        {
            var portPart = value[(lastColon + 1)..];
            if (portPart.Length > 0 && portPart.All(char.IsDigit))
            {
                value = value[..lastColon];
            }
        }

        return value.Length == 0 ? null : value;
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
git add Services/DomainNormalizer.cs
git commit -m "$(cat <<'EOF'
feat: add DomainNormalizer helper for consistent domain comparison

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Make `IRedirectService`/`RedirectService` domain-aware

**Files:**
- Modify: `Services/IRedirectService.cs`
- Modify: `Services/RedirectService.cs`

- [ ] **Step 1: Update the interface**

Current (`Services/IRedirectService.cs`):

```csharp
public interface IRedirectService
{
    IEnumerable<RedirectEntry> GetAll();
    IEnumerable<RedirectEntry> GetAllFiltered(string? query, int? statusCode, bool? isActive, bool? isRegex);
    RedirectEntry? GetById(int id);
    RedirectEntry? GetByOldUrl(string oldUrl);
    RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex);
    IEnumerable<RedirectEntry> GetActiveRegexEntries();
    RedirectEntry Create(CreateRedirectEntryDto dto);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive);
}
```

Replace with:

```csharp
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
}
```

`domain` defaults to `null` on both methods so any existing call site that
doesn't pass one (e.g. the `Test` endpoint, handled in Task 6) keeps
querying the global scope only, unchanged from today's behavior.

- [ ] **Step 2: Rewrite `GetByOldUrl` as a two-pass, domain-aware lookup**

Current (`Services/RedirectService.cs`):

```csharp
    public RedirectEntry? GetByOldUrl(string oldUrl)
    {
        using var scope = _scopeProvider.CreateScope();
        var normalizedUrl = NormalizeUrl(oldUrl);
        var result = scope.Database.SingleOrDefault<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsActive = 1 AND IsRegex = 0", normalizedUrl);
        scope.Complete();
        return result;
    }
```

Replace with:

```csharp
    public RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null)
    {
        using var scope = _scopeProvider.CreateScope();
        var normalizedUrl = NormalizeUrl(oldUrl);
        var normalizedDomain = DomainNormalizer.Normalize(domain);

        RedirectEntry? result = null;
        if (normalizedDomain != null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND Domain = @1 AND IsActive = 1 AND IsRegex = 0",
                normalizedUrl, normalizedDomain);
        }

        if (result == null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND (Domain IS NULL OR Domain = '') AND IsActive = 1 AND IsRegex = 0",
                normalizedUrl);
        }

        scope.Complete();
        return result;
    }
```

- [ ] **Step 3: Rewrite `GetByOldUrlAndIsRegex` as a domain-aware duplicate check**

Current:

```csharp
    public RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex)
    {
        using var scope = _scopeProvider.CreateScope();
        var value = NormalizeOldUrl(oldUrl, isRegex);
        var result = scope.Database.SingleOrDefault<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsRegex = @1", value, isRegex ? 1 : 0);
        scope.Complete();
        return result;
    }
```

Replace with:

```csharp
    public RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null)
    {
        using var scope = _scopeProvider.CreateScope();
        var value = NormalizeOldUrl(oldUrl, isRegex);
        var normalizedDomain = DomainNormalizer.Normalize(domain);

        RedirectEntry? result;
        if (normalizedDomain != null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsRegex = @1 AND Domain = @2",
                value, isRegex ? 1 : 0, normalizedDomain);
        }
        else
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsRegex = @1 AND (Domain IS NULL OR Domain = '')",
                value, isRegex ? 1 : 0);
        }

        scope.Complete();
        return result;
    }
```

Treating `NULL` and `''` as the same "global" bucket in both methods above
is deliberate: without it, someone could end up with one row stored as
`Domain = NULL` and another as `Domain = ''` for the same `OldUrl`, neither
flagged as a duplicate of the other, which would be a confusing, silent
edge case. Since `DomainNormalizer.Normalize` always converts blank input to
`null` before anything is written (Step 4 below), the database should never
actually contain `''` going forward — the `OR Domain = ''` half of these
`WHERE` clauses is defensive redundancy, not something intended to be
exercised in practice.

- [ ] **Step 4: Set `Domain` in `Create` and `Update`**

Current `Create`:

```csharp
    public RedirectEntry Create(CreateRedirectEntryDto dto)
    {
        var isRegex = dto.IsRegex;
        var entry = new RedirectEntry
        {
            OldUrl = NormalizeOldUrl(dto.OldUrl, isRegex),
            NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, isRegex),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            StatusCode = ValidateStatusCode(dto.StatusCode),
            IsActive = dto.IsActive,
            IsRegex = isRegex,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
```

Replace with:

```csharp
    public RedirectEntry Create(CreateRedirectEntryDto dto)
    {
        var isRegex = dto.IsRegex;
        var entry = new RedirectEntry
        {
            OldUrl = NormalizeOldUrl(dto.OldUrl, isRegex),
            NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, isRegex),
            Domain = DomainNormalizer.Normalize(dto.Domain),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            StatusCode = ValidateStatusCode(dto.StatusCode),
            IsActive = dto.IsActive,
            IsRegex = isRegex,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
```

Current `Update`:

```csharp
        existing.IsRegex = dto.IsRegex;
        existing.OldUrl = NormalizeOldUrl(dto.OldUrl, existing.IsRegex);
        existing.NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, existing.IsRegex);
        existing.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        existing.StatusCode = ValidateStatusCode(dto.StatusCode);
        existing.IsActive = dto.IsActive;
        existing.UpdatedDate = DateTime.UtcNow;
```

Replace with:

```csharp
        existing.IsRegex = dto.IsRegex;
        existing.OldUrl = NormalizeOldUrl(dto.OldUrl, existing.IsRegex);
        existing.NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, existing.IsRegex);
        existing.Domain = DomainNormalizer.Normalize(dto.Domain);
        existing.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        existing.StatusCode = ValidateStatusCode(dto.StatusCode);
        existing.IsActive = dto.IsActive;
        existing.UpdatedDate = DateTime.UtcNow;
```

(`dto.Domain` doesn't exist on `CreateRedirectEntryDto`/`UpdateRedirectEntryDto`
yet — that's added in Task 6. This task will not compile in isolation until
Task 6 lands; that's fine, both land in the same implementation pass before
the final build/commit checkpoint. If you're executing tasks strictly in
order and building after every single task, expect Task 4's build step to
fail with "'CreateRedirectEntryDto' does not contain a definition for
'Domain'" — proceed to Task 6 before treating that as a blocker.)

- [ ] **Step 5: Build to confirm it compiles (may fail until Task 6 — see note above)**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: fails with a missing-`Domain`-property error on
`CreateRedirectEntryDto`/`UpdateRedirectEntryDto` until Task 6's DTO changes
are also in place. Do not attempt to work around this by reordering or
skipping Task 6.

- [ ] **Step 6: Commit**

```bash
git add Services/IRedirectService.cs Services/RedirectService.cs
git commit -m "$(cat <<'EOF'
feat: make redirect lookups and duplicate-detection domain-aware

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

(Commit even though the build fails at this checkpoint — Task 6 fixes it in
the very next task, and the plan's per-task commit granularity still holds
since this is a real, reviewable unit of change on its own.)

---

### Task 5: Make `RedirectMiddleware` domain-aware

**Files:**
- Modify: `Middleware/RedirectMiddleware.cs`

- [ ] **Step 1: Compute the request's domain and skip-list check together**

Current:

```csharp
    public async Task InvokeAsync(HttpContext context, IRedirectService redirectService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        
        if (ShouldSkipRedirect(path))
        {
            await _next(context);
            return;
        }
```

Replace with:

```csharp
    public async Task InvokeAsync(HttpContext context, IRedirectService redirectService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var domain = DomainNormalizer.Normalize(context.Request.Host.Value);

        if (ShouldSkipRedirect(path))
        {
            await _next(context);
            return;
        }
```

- [ ] **Step 2: Pass `domain` into the exact-match lookups**

Current:

```csharp
        var redirect = redirectService.GetByOldUrl(pathAndQuery);
        if (redirect == null && pathAndQuery != path)
            redirect = redirectService.GetByOldUrl(path);
```

Replace with:

```csharp
        var redirect = redirectService.GetByOldUrl(pathAndQuery, domain);
        if (redirect == null && pathAndQuery != path)
            redirect = redirectService.GetByOldUrl(path, domain);
```

- [ ] **Step 3: Pass `domain` into the regex-match call**

Current:

```csharp
        var regexRedirect = FindRegexRedirect(path, redirectService);
```

Replace with:

```csharp
        var regexRedirect = FindRegexRedirect(path, domain, redirectService);
```

- [ ] **Step 4: Rewrite `FindRegexRedirect` as a two-pass, domain-aware lookup**

Current:

```csharp
    private RedirectMatch? FindRegexRedirect(string path, IRedirectService redirectService)
    {
        try
        {
            foreach (var r in redirectService.GetActiveRegexEntries())
            {
                if (string.IsNullOrWhiteSpace(r.OldUrl))
                    continue;

                var regex = RegexCache.GetOrAdd(r.OldUrl, pattern =>
                    new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout));

                if (!regex.IsMatch(path))
                    continue;

                var newUrl = r.NewUrl;

                if ((r.StatusCode == 301 || r.StatusCode == 302) && !string.IsNullOrWhiteSpace(newUrl))
                {
                    try
                    {
                        newUrl = regex.Replace(path, newUrl);
                    }
                    catch
                    {
                        // If replace fails, fall back to original NewUrl
                    }
                }

                return new RedirectMatch(r, newUrl);
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.LogWarning(ex, "Regex redirect match timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating regex redirects");
        }

        return null;
    }
```

Replace with:

```csharp
    private RedirectMatch? FindRegexRedirect(string path, string? domain, IRedirectService redirectService)
    {
        try
        {
            var entries = redirectService.GetActiveRegexEntries().ToList();

            if (domain != null)
            {
                var domainMatch = FindRegexMatchIn(entries.Where(r => r.Domain == domain), path);
                if (domainMatch != null)
                    return domainMatch;
            }

            return FindRegexMatchIn(entries.Where(r => string.IsNullOrEmpty(r.Domain)), path);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.LogWarning(ex, "Regex redirect match timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating regex redirects");
        }

        return null;
    }

    private RedirectMatch? FindRegexMatchIn(IEnumerable<Umbraco.RedirectManager.Models.RedirectEntry> candidates, string path)
    {
        foreach (var r in candidates)
        {
            if (string.IsNullOrWhiteSpace(r.OldUrl))
                continue;

            var regex = RegexCache.GetOrAdd(r.OldUrl, pattern =>
                new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout));

            if (!regex.IsMatch(path))
                continue;

            var newUrl = r.NewUrl;

            if ((r.StatusCode == 301 || r.StatusCode == 302) && !string.IsNullOrWhiteSpace(newUrl))
            {
                try
                {
                    newUrl = regex.Replace(path, newUrl);
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
```

The single global `GetActiveRegexEntries()` cache (30-second `IMemoryCache`
entry, unchanged from `RedirectService.cs`) is materialized once via
`.ToList()`, then filtered twice in memory — domain-specific entries first,
global (`Domain` null/empty) entries second. This preserves the existing
cache architecture untouched while adding the precedence rule at the point
of use, matching the design spec exactly.

- [ ] **Step 5: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`
(assuming Tasks 4 and 6 are both already applied, since this task calls
the new `GetByOldUrl(oldUrl, domain)` overload and reads `r.Domain`, which
only exists after Task 1).

- [ ] **Step 6: Commit**

```bash
git add Middleware/RedirectMiddleware.cs
git commit -m "$(cat <<'EOF'
feat: resolve request domain and apply it to redirect matching

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Add `Domain` to the DTOs and wire it through the controller

**Files:**
- Modify: `Models/RedirectEntryDto.cs`
- Modify: `Controllers/RedirectApiController.cs`

- [ ] **Step 1: Add `Domain` to all three DTOs**

Current (`Models/RedirectEntryDto.cs`):

```csharp
public class RedirectEntryDto
{
    public int Id { get; set; }
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
    public int HitCount { get; set; } = 0;
    public DateTime? LastHitDate { get; set; }
}

public class CreateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
}

public class UpdateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
}
```

Replace with:

```csharp
public class RedirectEntryDto
{
    public int Id { get; set; }
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
    public int HitCount { get; set; } = 0;
    public DateTime? LastHitDate { get; set; }
}

public class CreateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
}

public class UpdateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
}
```

- [ ] **Step 2: Map `Domain` in the existing `ToDto(RedirectEntry)` helper**

Current (`Controllers/RedirectApiController.cs`):

```csharp
    private static RedirectEntryDto ToDto(RedirectEntry r)
    {
        return new RedirectEntryDto
        {
            Id = r.Id,
            OldUrl = r.OldUrl,
            NewUrl = r.NewUrl,
            Description = r.Description,
            StatusCode = r.StatusCode,
            IsActive = r.IsActive,
            IsRegex = r.IsRegex,
            HitCount = r.HitCount,
            LastHitDate = r.LastHitDate
        };
    }
```

Replace with:

```csharp
    private static RedirectEntryDto ToDto(RedirectEntry r)
    {
        return new RedirectEntryDto
        {
            Id = r.Id,
            OldUrl = r.OldUrl,
            NewUrl = r.NewUrl,
            Domain = r.Domain,
            Description = r.Description,
            StatusCode = r.StatusCode,
            IsActive = r.IsActive,
            IsRegex = r.IsRegex,
            HitCount = r.HitCount,
            LastHitDate = r.LastHitDate
        };
    }
```

This is the only DTO-mapping call site that needs touching — sub-project
2's code-review fix already consolidated what used to be 6 separate inline
`RedirectEntryDto` constructions into this single helper, used by
`GetAll`/`Get`/`Create`/`Update`/both branches of `Test`.

- [ ] **Step 3: Pass `dto.Domain` into the duplicate-detection calls**

Current `Create` action:

```csharp
        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex);
        if (duplicate != null)
            return Conflict("A redirect with the same Old URL and Match type already exists");

        var redirect = _redirectService.Create(dto);
        return Ok(ToDto(redirect));
    }

    [HttpPut("update/{id:int}")]
```

Replace with:

```csharp
        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
        if (duplicate != null)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");

        var redirect = _redirectService.Create(dto);
        return Ok(ToDto(redirect));
    }

    [HttpPut("update/{id:int}")]
```

(The conflict message is updated to say "for that domain" since the check
is no longer purely global — this is a user-facing wording fix, not a
behavior change beyond what Task 4 already implements.)

Current `Update` action:

```csharp
        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex);
        if (duplicate != null && duplicate.Id != id)
            return Conflict("A redirect with the same Old URL and Match type already exists");
```

Replace with:

```csharp
        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
        if (duplicate != null && duplicate.Id != id)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");
```

- [ ] **Step 4: Pass domain through in the CSV import path**

Current (inside `ImportCsv`):

```csharp
            var existing = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex);
            if (existing == null)
            {
                _redirectService.Create(new CreateRedirectEntryDto
                {
                    OldUrl = dto.OldUrl,
                    NewUrl = dto.NewUrl,
                    Description = dto.Description,
                    StatusCode = dto.StatusCode,
                    IsActive = dto.IsActive,
                    IsRegex = dto.IsRegex
                });
                created++;
            }
```

Replace with:

```csharp
            var existing = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
            if (existing == null)
            {
                _redirectService.Create(new CreateRedirectEntryDto
                {
                    OldUrl = dto.OldUrl,
                    NewUrl = dto.NewUrl,
                    Domain = dto.Domain,
                    Description = dto.Description,
                    StatusCode = dto.StatusCode,
                    IsActive = dto.IsActive,
                    IsRegex = dto.IsRegex
                });
                created++;
            }
```

`dto.Domain` here is always `null` in practice today — the CSV format
(`OldUrl,NewUrl,Description,StatusCode,IsActive,IsRegex`) has no `Domain`
column, and nothing in `ImportCsv` sets it on the `UpdateRedirectEntryDto`
built from each row. This edit makes that explicit and future-proof (if a
`Domain` CSV column is ever added) rather than silently relying on the
default. **Do not add a `Domain` CSV column in this task** — that's out of
scope per the approved spec.

- [ ] **Step 5: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.
This is the point where Task 4's build failure (noted in that task) should
now resolve, since `Domain` exists on all three DTOs.

- [ ] **Step 6: Commit**

```bash
git add Models/RedirectEntryDto.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: add Domain to redirect DTOs and wire it through the controller

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Add a Domain field to the Lit dashboard (Umbraco 17+/18)

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add `domain` to the empty form data**

Current:

```javascript
    getEmptyFormData() {
        return {
            oldUrl: '',
            newUrl: '',
            description: '',
            statusCode: 301,
            isActive: true,
            isRegex: false
        };
    }
```

Replace with:

```javascript
    getEmptyFormData() {
        return {
            oldUrl: '',
            newUrl: '',
            domain: '',
            description: '',
            statusCode: 301,
            isActive: true,
            isRegex: false
        };
    }
```

`openAddModal(prefillOldUrl = '')` already builds its form data via
`{ ...this.getEmptyFormData(), oldUrl: prefillOldUrl }`, so it picks up the
new `domain: ''` default automatically — no change needed there.

- [ ] **Step 2: Populate `domain` when opening the edit modal**

Current:

```javascript
    openEditModal(redirect) {
        this.editingRedirect = redirect;
        this.formData = {
            oldUrl: redirect.oldUrl,
            newUrl: redirect.newUrl || '',
            description: redirect.description || '',
            statusCode: redirect.statusCode,
            isActive: redirect.isActive,
            isRegex: !!redirect.isRegex
        };
        this.showModal = true;
    }
```

Replace with:

```javascript
    openEditModal(redirect) {
        this.editingRedirect = redirect;
        this.formData = {
            oldUrl: redirect.oldUrl,
            newUrl: redirect.newUrl || '',
            domain: redirect.domain || '',
            description: redirect.description || '',
            statusCode: redirect.statusCode,
            isActive: redirect.isActive,
            isRegex: !!redirect.isRegex
        };
        this.showModal = true;
    }
```

- [ ] **Step 3: Add a "Domain" column header**

Current:

```javascript
                                <th style="text-align: center;">Old URL</th>
                                <th style="text-align: center;">New URL</th>
                                <th style="text-align: center;">Notes</th>
```

Replace with:

```javascript
                                <th style="text-align: center;">Old URL</th>
                                <th style="text-align: center;">New URL</th>
                                <th style="text-align: center;">Domain</th>
                                <th style="text-align: center;">Notes</th>
```

- [ ] **Step 4: Add the "Domain" data cell**

Current:

```javascript
                                    <td class="url-cell" title="${redirect.newUrl || ''}">
                                        ${redirect.newUrl ? html`
                                            <div style="display:flex; gap:6px; align-items:center;flex-direction:column;">
                                                <span>${redirect.newUrl}</span>
                                                <button class="btn btn-secondary btn-sm" @click=${() => this.copyToClipboard(redirect.newUrl)}>Copy</button>
                                            </div>
                                        ` : '-'}
                                    </td>
                                    <td class="notes-cell" title="${redirect.description || ''}">${redirect.description || '-'}</td>
```

Replace with:

```javascript
                                    <td class="url-cell" title="${redirect.newUrl || ''}">
                                        ${redirect.newUrl ? html`
                                            <div style="display:flex; gap:6px; align-items:center;flex-direction:column;">
                                                <span>${redirect.newUrl}</span>
                                                <button class="btn btn-secondary btn-sm" @click=${() => this.copyToClipboard(redirect.newUrl)}>Copy</button>
                                            </div>
                                        ` : '-'}
                                    </td>
                                    <td style="text-align: center;">${redirect.domain || 'All domains'}</td>
                                    <td class="notes-cell" title="${redirect.description || ''}">${redirect.description || '-'}</td>
```

- [ ] **Step 5: Add the "Domain (optional)" field to the modal, after New URL**

Current:

```javascript
                            ${this.formData.statusCode === 301 || this.formData.statusCode === 302 ? html`
                                <div class="form-group">
                                    <label>New URL *</label>
                                    <input type="text" 
                                           name="newUrl" 
                                           .value=${this.formData.newUrl} 
                                           @input=${this.handleInputChange}
                                           placeholder="/new-page">
                                    <small>The URL path to redirect to</small>
                                </div>
                            ` : ''}
                        </div>

                        <div class="form-group">
                            <label>Notes</label>
```

Replace with:

```javascript
                            ${this.formData.statusCode === 301 || this.formData.statusCode === 302 ? html`
                                <div class="form-group">
                                    <label>New URL *</label>
                                    <input type="text" 
                                           name="newUrl" 
                                           .value=${this.formData.newUrl} 
                                           @input=${this.handleInputChange}
                                           placeholder="/new-page">
                                    <small>The URL path to redirect to</small>
                                </div>
                            ` : ''}
                        </div>

                        <div class="form-group">
                            <label>Domain (optional)</label>
                            <input type="text" 
                                   name="domain" 
                                   .value=${this.formData.domain} 
                                   @input=${this.handleInputChange}
                                   placeholder="example.com">
                            <small>Leave blank to apply this redirect to all domains. If both a domain-specific and an all-domains redirect exist for the same Old URL, the domain-specific one wins.</small>
                        </div>

                        <div class="form-group">
                            <label>Notes</label>
```

`handleInputChange` already generically applies `[name]: value` from any
input's `name` attribute to `this.formData`, so no change is needed there
for the new `domain` field to work — and `saveRedirect()` sends
`JSON.stringify(this.formData)` as-is, so `domain` flows to the API
automatically.

- [ ] **Step 6: Build to confirm the .NET project still compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`
(this static JS asset isn't compiled — this only confirms nothing else in
the repo broke). After editing, visually re-read the modified sections of
`render()` once to confirm the new `<th>`/`<td>` pair line up in the same
column position and the new `form-group` sits between the New URL row and
the Notes field.

- [ ] **Step 7: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "$(cat <<'EOF'
feat: add Domain field and column to the Lit dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Add a Domain field to the AngularJS dashboard (Umbraco 13)

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`
- Modify: `App_Plugins/RedirectManager/modal.html`
- Modify: `App_Plugins/RedirectManager/dashboard.html`

- [ ] **Step 1: Add `domain` to the add-modal's redirect object**

Current (`redirect.controller.js`):

```javascript
        vm.openAddModal = function (prefillOldUrl) {
            vm.modalModel = {
                title: "Add New Redirect",
                redirect: {
                    oldUrl: prefillOldUrl || "",
                    newUrl: "",
                    description: "",
                    statusCode: "301",
                    isActive: true,
                    isRegex: false
                },
```

Replace with:

```javascript
        vm.openAddModal = function (prefillOldUrl) {
            vm.modalModel = {
                title: "Add New Redirect",
                redirect: {
                    oldUrl: prefillOldUrl || "",
                    newUrl: "",
                    domain: "",
                    description: "",
                    statusCode: "301",
                    isActive: true,
                    isRegex: false
                },
```

- [ ] **Step 2: Add `domain` to the edit-modal's redirect object**

Current:

```javascript
        vm.openEditModal = function (redirect) {
            vm.modalModel = {
                title: "Edit Redirect",
                redirect: {
                    id: redirect.id,
                    oldUrl: redirect.oldUrl,
                    newUrl: redirect.newUrl || "",
                    description: redirect.description || "",
                    statusCode: redirect.statusCode.toString(),
                    isActive: redirect.isActive,
                    isRegex: !!redirect.isRegex
                },
```

Replace with:

```javascript
        vm.openEditModal = function (redirect) {
            vm.modalModel = {
                title: "Edit Redirect",
                redirect: {
                    id: redirect.id,
                    oldUrl: redirect.oldUrl,
                    newUrl: redirect.newUrl || "",
                    domain: redirect.domain || "",
                    description: redirect.description || "",
                    statusCode: redirect.statusCode.toString(),
                    isActive: redirect.isActive,
                    isRegex: !!redirect.isRegex
                },
```

`vm.saveRedirect` sends `model.redirect` as-is via `redirectResource.create`/
`.update`, so no change is needed there for `domain` to reach the API.

- [ ] **Step 3: Build to confirm the .NET project still compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit the controller change**

```bash
git add App_Plugins/RedirectManager/redirect.controller.js
git commit -m "$(cat <<'EOF'
feat: pass Domain through the AngularJS dashboard controller

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 5: Add the "Domain (optional)" field to the modal**

Current (`modal.html`):

```html
            <umb-control-group label="New URL"
                               description="The URL path to redirect to"
                               ng-if="model.redirect.statusCode == 301 || model.redirect.statusCode == 302">
                <input type="text"
                       ng-model="model.redirect.newUrl"
                       class="umb-property-editor umb-textstring"
                       placeholder="/new-page">
            </umb-control-group>

            <umb-control-group label="Notes"
```

Replace with:

```html
            <umb-control-group label="New URL"
                               description="The URL path to redirect to"
                               ng-if="model.redirect.statusCode == 301 || model.redirect.statusCode == 302">
                <input type="text"
                       ng-model="model.redirect.newUrl"
                       class="umb-property-editor umb-textstring"
                       placeholder="/new-page">
            </umb-control-group>

            <umb-control-group label="Domain"
                               description="Leave blank to apply this redirect to all domains. If both a domain-specific and an all-domains redirect exist for the same Old URL, the domain-specific one wins.">
                <input type="text"
                       ng-model="model.redirect.domain"
                       class="umb-property-editor umb-textstring"
                       placeholder="example.com">
            </umb-control-group>

            <umb-control-group label="Notes"
```

- [ ] **Step 6: Add the "Domain" column header**

Current (`dashboard.html`):

```html
                        <th>Old URL</th>
                        <th>New URL</th>
                        <th>Notes</th>
```

Replace with:

```html
                        <th>Old URL</th>
                        <th>New URL</th>
                        <th>Domain</th>
                        <th>Notes</th>
```

- [ ] **Step 7: Add the "Domain" data cell**

Current:

```html
                        <td class="redirect-url">{{redirect.oldUrl}}</td>
                        <td class="redirect-url">{{redirect.newUrl || '-'}}</td>
                        <td class="redirect-notes">{{redirect.description || '-'}}</td>
```

Replace with:

```html
                        <td class="redirect-url">{{redirect.oldUrl}}</td>
                        <td class="redirect-url">{{redirect.newUrl || '-'}}</td>
                        <td>{{redirect.domain || 'All domains'}}</td>
                        <td class="redirect-notes">{{redirect.description || '-'}}</td>
```

- [ ] **Step 8: Build to confirm the .NET project still compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.
After editing, visually re-read `dashboard.html`'s table `<thead>`/`<tbody>`
once to confirm the new `<th>`/`<td>` pair are in the same column position.

- [ ] **Step 9: Commit the markup changes**

```bash
git add App_Plugins/RedirectManager/modal.html App_Plugins/RedirectManager/dashboard.html
git commit -m "$(cat <<'EOF'
feat: add Domain field and column to the AngularJS dashboard markup

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Manual verification — DEFERRED (documented, not executed)

Same constraint as sub-projects 1-3: no automated test project, no runnable
Umbraco host in this repo, no local test site currently available. This
documents what to run manually before the batched `1.3.0` release ships.

**Files:** none

- [ ] **Step 1 (deferred): Push to the local BaGet feed and install into a test site**

```bash
docker compose -f docker/docker-compose.yml up -d
./scripts/push-to-feed.sh
```

Then update the package in a test Umbraco site and start it so the new
migration runs.

- [ ] **Step 2 (deferred): Confirm the migration applied cleanly**

Check startup logs for the migration plan completing without error, and
confirm `RedirectManagerEntries` has the new `Domain` column, with existing
rows showing `NULL`.

- [ ] **Step 3 (deferred): Confirm existing (pre-migration) redirects still work unchanged**

Visit a path that already had a redirect rule before this release, from any
hostname the test site responds to, and confirm it still redirects
correctly — this is the core backward-compatibility check.

- [ ] **Step 4 (deferred): Confirm domain precedence**

Create a global redirect for `/foo` → `/global-target`. Then create a
second redirect for `/foo` → `/domain-target` scoped to the test site's
actual hostname. Visit `/foo` on that hostname and confirm it goes to
`/domain-target`, not `/global-target` — the core precedence rule.

- [ ] **Step 5 (deferred): Confirm a domain-scoped redirect does NOT fire on a different host**

If the test environment has a second hostname/binding available, confirm
the domain-scoped `/foo` redirect from Step 4 does *not* fire when visited
via a different hostname (it should fall through to the global rule, or to
a real 404 if no global rule exists for that path).

- [ ] **Step 6 (deferred): Confirm duplicate-detection allows the same OldUrl across domains**

In the dashboard, try creating a second redirect for `/foo` scoped to the
same domain used in Step 4 — confirm it's rejected as a duplicate. Then
create one for `/foo` scoped to a *different* domain — confirm it's
accepted.

- [ ] **Step 7 (deferred): Confirm the dashboard UI round-trips Domain correctly**

Create, edit, and view a domain-scoped redirect through both dashboards
(Umbraco 13's AngularJS one and Umbraco 17+/18's Lit one, if both test
environments are available) and confirm the Domain value displays and
persists correctly, and blank Domain shows as "All domains".

---

## Out of scope for this plan

- Any Umbraco `IDomainService` integration, dropdown/autocomplete of
  configured site domains, or wildcard/pattern domain matching — all
  explicitly out of scope per the approved spec.
- A `Domain` column/filter in CSV export/import — CSV format is unchanged.
- Domain-awareness for the `Test` endpoint or `GetAllFiltered` — both
  continue to operate globally, unchanged from before this plan (the
  `Test` endpoint's `GetByOldUrl` call relies on the new parameter's
  `null` default rather than being touched directly).
- Wiring up `MissedRequest.Domain` (added proactively in sub-project 3) —
  still unused after this plan, per the spec's explicit scope boundary.
- Version bump, git tag, and NuGet publish — this is the last of the 4
  sub-projects; that release step happens once, after this one is
  confirmed done, covering all four together as `1.3.0`.
