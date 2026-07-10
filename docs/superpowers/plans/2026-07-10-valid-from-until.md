# Valid From / Valid Until (Scheduled Redirects) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let editors schedule a redirect rule to become active on a future date/time and/or automatically stop firing after a date/time, without manually toggling `IsActive`.

**Architecture:** Add nullable `ValidFrom`/`ValidUntil` `DateTime?` columns to `RedirectEntry` (both `NULL` = unbounded, fully backward compatible). Bake the window check directly into the same SQL `WHERE` clauses that already filter `IsActive = 1` — both the exact-match lookup (`RedirectService.GetByOldUrl`) and the cached regex-entries fetch (`GetActiveRegexEntries`) — using `DateTime.UtcNow` passed as a query parameter for cross-provider portability. Thread the two fields through the DTOs and service create/update mapping as plain passthroughs. In both dashboards, add `datetime-local` inputs that convert between the browser's local time zone and the stored UTC value, a client-side "until after from" validation check, and a computed "Scheduled"/"Expired" list badge.

**Tech Stack:** NPoco via `IScopeProvider` (unchanged), ASP.NET Core, Lit and AngularJS dashboards (unchanged tech, new fields/columns).

Reference spec: `docs/superpowers/specs/2026-07-10-valid-from-until-design.md`

This is sub-project 2 of 9 in the current roadmap batch. No version bump/release happens here — that is a separate step once all 9 sub-projects are done.

---

### Task 1: Add `ValidFrom`/`ValidUntil` columns to `RedirectEntry`

**Files:**
- Modify: `Models/RedirectEntry.cs`

- [ ] **Step 1: Add the properties**

Current (end of the class):

```csharp
    [Column("PreserveQueryString")]
    [Constraint(Default = false)]
    public bool PreserveQueryString { get; set; } = false;
}
```

Replace with:

```csharp
    [Column("PreserveQueryString")]
    [Constraint(Default = false)]
    public bool PreserveQueryString { get; set; } = false;

    [Column("ValidFrom")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ValidFrom { get; set; }

    [Column("ValidUntil")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ValidUntil { get; set; }
}
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
feat: add ValidFrom/ValidUntil columns to RedirectEntry model

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add the `AddValidityWindowColumns` migration step

**Files:**
- Modify: `Migrations/RedirectManagerMigrationPlan.cs`

- [ ] **Step 1: Register the new migration step in `DefinePlan()`**

Current:

```csharp
        To<CreateRedirectManagerTable>(new Guid("C1686EA6-A8CF-4B7E-B91F-D4519EB17FDA"));
        To<AddIsRegexAndDescriptionColumns>(new Guid("EE2670E3-75C8-4BF6-8D70-36B10D5ECC65"));
        To<AddHitCountColumns>(new Guid("4F2A8B31-6C7C-4A8E-9E22-2D4D6D9CDDF1"));
        To<CreateMissedRequestsTable>(new Guid("7A1E9C42-3B5D-4F6A-8E11-9C2D5A7B3F04"));
        To<AddDomainColumn>(new Guid("B8D4E617-2F0A-4C9B-A5D3-6E1F8C0A9B72"));
        To<CreateRedirectHitDailyTable>(new Guid("1D9F4E23-6A8B-4C1D-9E7A-3B5C8D2F4A61"));
        To<AddAbTestColumns>(new Guid("6E3A9C15-4B7D-4F2E-8A1C-9D6E5F0B3C82"));
        To<AddPreserveQueryStringColumn>(new Guid("9C4F2A18-5E7B-4D3A-8F16-2C9E7B4A6D31"));
    }
}
```

Replace with:

```csharp
        To<CreateRedirectManagerTable>(new Guid("C1686EA6-A8CF-4B7E-B91F-D4519EB17FDA"));
        To<AddIsRegexAndDescriptionColumns>(new Guid("EE2670E3-75C8-4BF6-8D70-36B10D5ECC65"));
        To<AddHitCountColumns>(new Guid("4F2A8B31-6C7C-4A8E-9E22-2D4D6D9CDDF1"));
        To<CreateMissedRequestsTable>(new Guid("7A1E9C42-3B5D-4F6A-8E11-9C2D5A7B3F04"));
        To<AddDomainColumn>(new Guid("B8D4E617-2F0A-4C9B-A5D3-6E1F8C0A9B72"));
        To<CreateRedirectHitDailyTable>(new Guid("1D9F4E23-6A8B-4C1D-9E7A-3B5C8D2F4A61"));
        To<AddAbTestColumns>(new Guid("6E3A9C15-4B7D-4F2E-8A1C-9D6E5F0B3C82"));
        To<AddPreserveQueryStringColumn>(new Guid("9C4F2A18-5E7B-4D3A-8F16-2C9E7B4A6D31"));
        To<AddValidityWindowColumns>(new Guid("A2E5F8C1-3B6D-4A9E-8F17-5C0D9E4B7A63"));
    }
}
```

- [ ] **Step 2: Add the async (net10.0+) migration class**

In the `#if NET10_0_OR_GREATER` block, immediately after the closing brace of the async `AddPreserveQueryStringColumn` class (right before `#else`), insert:

```csharp
public class AddValidityWindowColumns : AsyncMigrationBase
{
    public AddValidityWindowColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "ValidFrom") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ValidFrom");
        }

        if (ColumnExists(RedirectEntry.TableName, "ValidUntil") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ValidUntil");
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Add the sync (net8.0) migration class**

In the `#else` block, immediately after the closing brace of the sync `AddPreserveQueryStringColumn` class (at the end of the file, right before `#endif`), insert:

```csharp
public class AddValidityWindowColumns : MigrationBase
{
    public AddValidityWindowColumns(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "ValidFrom") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ValidFrom");
        }

        if (ColumnExists(RedirectEntry.TableName, "ValidUntil") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ValidUntil");
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
feat: add migration for RedirectEntry.ValidFrom/ValidUntil columns

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Add `ValidFrom`/`ValidUntil` to the DTOs

**Files:**
- Modify: `Models/RedirectEntryDto.cs`

- [ ] **Step 1: Add the fields to all three DTO classes**

Current:

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
    public int Hits7d { get; set; } = 0;
    public int Hits30d { get; set; } = 0;
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public int VariantBHitCount { get; set; } = 0;
    public DateTime? VariantBLastHitDate { get; set; }
    public bool PreserveQueryString { get; set; } = false;
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
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public bool PreserveQueryString { get; set; } = false;
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
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public bool PreserveQueryString { get; set; } = false;
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
    public int Hits7d { get; set; } = 0;
    public int Hits30d { get; set; } = 0;
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public int VariantBHitCount { get; set; } = 0;
    public DateTime? VariantBLastHitDate { get; set; }
    public bool PreserveQueryString { get; set; } = false;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
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
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public bool PreserveQueryString { get; set; } = false;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
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
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public bool PreserveQueryString { get; set; } = false;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Models/RedirectEntryDto.cs
git commit -m "$(cat <<'EOF'
feat: add ValidFrom/ValidUntil to redirect DTOs

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Filter matching queries by validity window and wire the fields through service/API mapping

**Files:**
- Modify: `Services/RedirectService.cs`
- Modify: `Controllers/RedirectApiController.cs`

- [ ] **Step 1: Add the window filter to `GetByOldUrl`**

Current:

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

Replace with:

```csharp
    public RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null)
    {
        using var scope = _scopeProvider.CreateScope();
        var normalizedUrl = NormalizeUrl(oldUrl);
        var normalizedDomain = DomainNormalizer.Normalize(domain);
        var now = DateTime.UtcNow;

        RedirectEntry? result = null;
        if (normalizedDomain != null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND Domain = @1 AND IsActive = 1 AND IsRegex = 0 AND (ValidFrom IS NULL OR ValidFrom <= @2) AND (ValidUntil IS NULL OR ValidUntil >= @2)",
                normalizedUrl, normalizedDomain, now);
        }

        if (result == null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND (Domain IS NULL OR Domain = '') AND IsActive = 1 AND IsRegex = 0 AND (ValidFrom IS NULL OR ValidFrom <= @1) AND (ValidUntil IS NULL OR ValidUntil >= @1)",
                normalizedUrl, now);
        }

        scope.Complete();
        return result;
    }
```

Note: `GetByOldUrlAndIsRegex` (the duplicate-check method, immediately below this one in the same file) is intentionally NOT modified — it must keep matching a rule regardless of its validity window, exactly as it already ignores `IsActive` today.

- [ ] **Step 2: Add the window filter to `GetActiveRegexEntries`**

Current:

```csharp
    public IEnumerable<RedirectEntry> GetActiveRegexEntries()
    {
        return _memoryCache.GetOrCreate(ActiveRegexCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            using var scope = _scopeProvider.CreateScope();
            var results = scope.Database.Fetch<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE IsActive = 1 AND IsRegex = 1 ORDER BY CreatedDate DESC");
            scope.Complete();
            return results;
        }) ?? Enumerable.Empty<RedirectEntry>();
    }
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
```

Note: because this method's result is cached for 30 seconds in `IMemoryCache`, a regex rule crossing its `ValidFrom`/`ValidUntil` boundary purely due to the clock ticking (not an edit) will only be reflected once the cache naturally expires — up to 30 seconds of staleness. This is a known, accepted limitation (see the design spec) and requires no code change here.

- [ ] **Step 3: Map the fields in `Create` (plain passthrough)**

Current:

```csharp
            VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, isRegex),
            VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight,
            PreserveQueryString = dto.PreserveQueryString,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
```

Replace with:

```csharp
            VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, isRegex),
            VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight,
            PreserveQueryString = dto.PreserveQueryString,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
```

- [ ] **Step 4: Map the fields in `Update` (plain passthrough)**

Current:

```csharp
        existing.VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, existing.IsRegex);
        existing.VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight;
        existing.PreserveQueryString = dto.PreserveQueryString;
        existing.UpdatedDate = DateTime.UtcNow;
```

Replace with:

```csharp
        existing.VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, existing.IsRegex);
        existing.VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight;
        existing.PreserveQueryString = dto.PreserveQueryString;
        existing.ValidFrom = dto.ValidFrom;
        existing.ValidUntil = dto.ValidUntil;
        existing.UpdatedDate = DateTime.UtcNow;
```

- [ ] **Step 5: Map the fields in `ToDto` (`Controllers/RedirectApiController.cs`)**

Current:

```csharp
    private static RedirectEntryDto ToDto(RedirectEntry r, int hits7d = 0, int hits30d = 0)
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
            LastHitDate = r.LastHitDate,
            Hits7d = hits7d,
            Hits30d = hits30d,
            VariantBUrl = r.VariantBUrl,
            VariantBWeight = r.VariantBWeight,
            VariantBHitCount = r.VariantBHitCount,
            VariantBLastHitDate = r.VariantBLastHitDate,
            PreserveQueryString = r.PreserveQueryString
        };
    }
```

Replace with:

```csharp
    private static RedirectEntryDto ToDto(RedirectEntry r, int hits7d = 0, int hits30d = 0)
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
            LastHitDate = r.LastHitDate,
            Hits7d = hits7d,
            Hits30d = hits30d,
            VariantBUrl = r.VariantBUrl,
            VariantBWeight = r.VariantBWeight,
            VariantBHitCount = r.VariantBHitCount,
            VariantBLastHitDate = r.VariantBLastHitDate,
            PreserveQueryString = r.PreserveQueryString,
            ValidFrom = r.ValidFrom,
            ValidUntil = r.ValidUntil
        };
    }
```

- [ ] **Step 6: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 7: Commit**

```bash
git add Services/RedirectService.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: filter redirect matching by ValidFrom/ValidUntil window and wire fields through service/API mapping

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

Note: `GetAllFiltered`/`GetAll` (used by the dashboard's list view) and the `Test` endpoint are intentionally left untouched — see the design spec's "Out of scope" section.

---

### Task 5: Add date fields, validation, and the schedule badge to the Lit dashboard (Umbraco 17+/18)

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add the defaults to `getEmptyFormData()`**

Current:

```javascript
    getEmptyFormData() {
        return {
            oldUrl: '',
            newUrl: '',
            domain: '',
            description: '',
            statusCode: 301,
            isActive: true,
            isRegex: false,
            abTestEnabled: false,
            variantBUrl: '',
            variantBWeight: 50,
            preserveQueryString: false
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
            isRegex: false,
            abTestEnabled: false,
            variantBUrl: '',
            variantBWeight: 50,
            preserveQueryString: false,
            validFrom: '',
            validUntil: ''
        };
    }
```

- [ ] **Step 2: Populate it in `openEditModal`, converting the stored UTC value to a local `datetime-local` string**

Current:

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
            isRegex: !!redirect.isRegex,
            abTestEnabled: !!redirect.variantBUrl,
            variantBUrl: redirect.variantBUrl || '',
            variantBWeight: redirect.variantBWeight ?? 50,
            preserveQueryString: !!redirect.preserveQueryString
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
            isRegex: !!redirect.isRegex,
            abTestEnabled: !!redirect.variantBUrl,
            variantBUrl: redirect.variantBUrl || '',
            variantBWeight: redirect.variantBWeight ?? 50,
            preserveQueryString: !!redirect.preserveQueryString,
            validFrom: this.toDatetimeLocalValue(redirect.validFrom),
            validUntil: this.toDatetimeLocalValue(redirect.validUntil)
        };
        this.showModal = true;
    }
```

- [ ] **Step 3: Add the `toDatetimeLocalValue`/`fromDatetimeLocalValue`/`getScheduleBadge` helper methods**

Current (end of the `getLastHitTitle` method, immediately before `getMissedRequestTitle`):

```javascript
    getLastHitTitle(redirect) {
        return redirect.lastHitDate
            ? `Last hit: ${new Date(redirect.lastHitDate).toLocaleString()}`
            : 'Never hit';
    }

    getMissedRequestTitle(item) {
```

Replace with:

```javascript
    getLastHitTitle(redirect) {
        return redirect.lastHitDate
            ? `Last hit: ${new Date(redirect.lastHitDate).toLocaleString()}`
            : 'Never hit';
    }

    // Converts a stored UTC ISO string (or null) into the local-time string
    // an <input type="datetime-local"> expects (no timezone designator,
    // minute precision). Returns '' for null/invalid input, which the input
    // renders as empty (no date selected).
    toDatetimeLocalValue(isoString) {
        if (!isoString) return '';
        const d = new Date(isoString);
        if (Number.isNaN(d.getTime())) return '';
        const pad = (n) => String(n).padStart(2, '0');
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    }

    // Converts a <input type="datetime-local"> value (interpreted by the
    // browser/JS Date constructor as local time, since it has no timezone
    // designator) into a UTC ISO string for the API. Returns null for
    // blank/invalid input.
    fromDatetimeLocalValue(localValue) {
        if (!localValue) return null;
        const d = new Date(localValue);
        if (Number.isNaN(d.getTime())) return null;
        return d.toISOString();
    }

    getScheduleBadge(redirect) {
        const now = new Date();
        if (redirect.validFrom && new Date(redirect.validFrom) > now) return 'Scheduled';
        if (redirect.validUntil && new Date(redirect.validUntil) < now) return 'Expired';
        return null;
    }

    getMissedRequestTitle(item) {
```

- [ ] **Step 4: Add the "until after from" validation and convert the fields in `saveRedirect`'s payload**

Current:

```javascript
        if (this.formData.abTestEnabled && !this.formData.variantBUrl) {
            this.showMessage('Variant B URL is required when A/B test is enabled', 'error');
            return;
        }

        const payload = {
            ...this.formData,
            variantBUrl: this.formData.abTestEnabled ? this.formData.variantBUrl : null,
            variantBWeight: this.formData.abTestEnabled ? this.formData.variantBWeight : null
        };
```

Replace with:

```javascript
        if (this.formData.abTestEnabled && !this.formData.variantBUrl) {
            this.showMessage('Variant B URL is required when A/B test is enabled', 'error');
            return;
        }

        if (this.formData.validFrom && this.formData.validUntil && new Date(this.formData.validUntil) < new Date(this.formData.validFrom)) {
            this.showMessage('Valid until must be after Valid from', 'error');
            return;
        }

        const payload = {
            ...this.formData,
            variantBUrl: this.formData.abTestEnabled ? this.formData.variantBUrl : null,
            variantBWeight: this.formData.abTestEnabled ? this.formData.variantBWeight : null,
            validFrom: this.fromDatetimeLocalValue(this.formData.validFrom),
            validUntil: this.fromDatetimeLocalValue(this.formData.validUntil)
        };
```

- [ ] **Step 5: Add the two date fields to the modal markup, right after the Domain field**

Current:

```javascript
                                <!-- Domain -->
                                <div class="form-group">
                                    <label>Domain <span class="lbl-opt">(optional)</span></label>
                                    <input type="text"
                                           name="domain"
                                           .value=${this.formData.domain}
                                           @input=${this.handleInputChange}
                                           placeholder="e.g. shop.example.com" />
                                    <small>Leave blank to apply to all domains. Domain-specific rules take precedence.</small>
                                </div>

                                <!-- Notes -->
```

Replace with:

```javascript
                                <!-- Domain -->
                                <div class="form-group">
                                    <label>Domain <span class="lbl-opt">(optional)</span></label>
                                    <input type="text"
                                           name="domain"
                                           .value=${this.formData.domain}
                                           @input=${this.handleInputChange}
                                           placeholder="e.g. shop.example.com" />
                                    <small>Leave blank to apply to all domains. Domain-specific rules take precedence.</small>
                                </div>

                                <!-- Valid from / Valid until -->
                                <div class="form-row">
                                    <div class="form-group">
                                        <label>Valid from <span class="lbl-opt">(optional)</span></label>
                                        <input type="datetime-local"
                                               name="validFrom"
                                               .value=${this.formData.validFrom}
                                               @input=${this.handleInputChange} />
                                        <small>Leave blank to make this redirect active immediately.</small>
                                    </div>
                                    <div class="form-group">
                                        <label>Valid until <span class="lbl-opt">(optional)</span></label>
                                        <input type="datetime-local"
                                               name="validUntil"
                                               .value=${this.formData.validUntil}
                                               @input=${this.handleInputChange} />
                                        <small>Leave blank to keep this redirect active indefinitely.</small>
                                    </div>
                                </div>

                                <!-- Notes -->
```

- [ ] **Step 6: Add the schedule badge to the list table's Active column**

Current:

```javascript
                                        <td class="center">
                                            <span class="active-indicator">
                                                <span class="status-dot ${redirect.isActive ? 'active' : 'inactive'}"></span>
                                                ${redirect.isActive ? 'Yes' : 'No'}
                                            </span>
                                        </td>
```

Replace with:

```javascript
                                        <td class="center">
                                            <span class="active-indicator">
                                                <span class="status-dot ${redirect.isActive ? 'active' : 'inactive'}"></span>
                                                ${redirect.isActive ? 'Yes' : 'No'}
                                            </span>
                                            ${this.getScheduleBadge(redirect) ? html`
                                                <span class="schedule-badge ${this.getScheduleBadge(redirect) === 'Scheduled' ? 'scheduled' : 'expired'}">
                                                    ${this.getScheduleBadge(redirect)}
                                                </span>
                                            ` : ''}
                                        </td>
```

IMPORTANT: locate this exact block by its distinctive content (the `active-indicator`/`status-dot` pair) — there is exactly ONE such block in the file (the redirects list table). If you find more than one match or can't find an unambiguous one, STOP and report BLOCKED describing what you found.

- [ ] **Step 7: Add the `.schedule-badge` CSS, right after the existing `.status-dot` rules**

Current:

```javascript
        .status-dot.active   { background: #2bc37b; }
        .status-dot.inactive { background: #d42054; }

        .hit-count {
```

Replace with:

```javascript
        .status-dot.active   { background: #2bc37b; }
        .status-dot.inactive { background: #d42054; }

        .schedule-badge {
            display: inline-block;
            margin-left: 5px;
            padding: 1px 6px;
            border-radius: 4px;
            font-size: 10px;
            font-weight: 600;
        }

        .schedule-badge.scheduled { background: #dbeafe; color: #1e40af; }
        .schedule-badge.expired   { background: #f3f4f6; color: #6b7280; }

        .hit-count {
```

- [ ] **Step 8: Build to confirm the .NET project still compiles, then verify JS syntax**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
node --check App_Plugins/RedirectManager/redirect-dashboard.js
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`, and `node --check` produces no output (meaning the file parses as valid JS — this catches unbalanced template literals/braces that `dotnet build` won't).

- [ ] **Step 9: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "$(cat <<'EOF'
feat: add Valid from/until scheduling fields and status badge to the Lit dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Add date fields, validation, and the schedule badge to the AngularJS dashboard (Umbraco 13)

**Files:**
- Modify: `App_Plugins/RedirectManager/modal.html`
- Modify: `App_Plugins/RedirectManager/dashboard.html`
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`
- Modify: `App_Plugins/RedirectManager/redirect.css`

- [ ] **Step 1: Add the defaults to `openAddModal`'s model (`redirect.controller.js`)**

Current:

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
                    isRegex: false,
                    abTestEnabled: false,
                    variantBUrl: "",
                    variantBWeight: 50,
                    preserveQueryString: false
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
                    isRegex: false,
                    abTestEnabled: false,
                    variantBUrl: "",
                    variantBWeight: 50,
                    preserveQueryString: false,
                    validFrom: null,
                    validUntil: null
                },
```

- [ ] **Step 2: Populate it in `openEditModal`'s model, converting the stored UTC ISO string into a `Date` object**

Current:

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
                    isRegex: !!redirect.isRegex,
                    abTestEnabled: !!redirect.variantBUrl,
                    variantBUrl: redirect.variantBUrl || "",
                    variantBWeight: redirect.variantBWeight != null ? redirect.variantBWeight : 50,
                    preserveQueryString: !!redirect.preserveQueryString
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
                    isRegex: !!redirect.isRegex,
                    abTestEnabled: !!redirect.variantBUrl,
                    variantBUrl: redirect.variantBUrl || "",
                    variantBWeight: redirect.variantBWeight != null ? redirect.variantBWeight : 50,
                    preserveQueryString: !!redirect.preserveQueryString,
                    validFrom: redirect.validFrom ? new Date(redirect.validFrom) : null,
                    validUntil: redirect.validUntil ? new Date(redirect.validUntil) : null
                },
```

Note: AngularJS's `input[datetime-local]` directive requires its bound `ng-model` value to be a native `Date` object (or `null`), not a string — it displays the Date in the browser's local time zone and, when the field changes, writes a new local-time `Date` object back onto the model. No manual UTC conversion is needed elsewhere: when `vm.saveRedirect` later sends `redirect` to `redirectResource.create/update` (which uses `$http`), AngularJS's request serializer calls `Date.prototype.toJSON()` on any `Date` object automatically, producing a UTC ISO string — the same mechanism already used implicitly for every other field in this object.

- [ ] **Step 3: Add the `vm.getScheduleBadge` helper, right after `vm.getStatusCodeLabel`**

Current:

```javascript
        vm.getStatusCodeLabel = function (code) {
            var found = vm.statusCodes.find(function (sc) {
                return sc.value == code;
            });
            return found ? found.label : code;
        };

        vm.loadRedirects = function () {
```

Replace with:

```javascript
        vm.getStatusCodeLabel = function (code) {
            var found = vm.statusCodes.find(function (sc) {
                return sc.value == code;
            });
            return found ? found.label : code;
        };

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

- [ ] **Step 4: Add the "until after from" validation in `vm.saveRedirect`**

Current:

```javascript
            if (redirect.abTestEnabled && !redirect.variantBUrl) {
                notificationsService.error("Validation Error", "Variant B URL is required when A/B test is enabled");
                return;
            }

            if (redirect.abTestEnabled) {
```

Replace with:

```javascript
            if (redirect.abTestEnabled && !redirect.variantBUrl) {
                notificationsService.error("Validation Error", "Variant B URL is required when A/B test is enabled");
                return;
            }

            if (redirect.validFrom && redirect.validUntil && new Date(redirect.validUntil) < new Date(redirect.validFrom)) {
                notificationsService.error("Validation Error", "Valid until must be after Valid from");
                return;
            }

            if (redirect.abTestEnabled) {
```

- [ ] **Step 5: Add the two date fields to `modal.html`, right after the Domain group**

Current:

```html
            <umb-control-group label="Domain"
                               description="Leave blank to apply this redirect to all domains. If both a domain-specific and an all-domains redirect exist for the same Old URL, the domain-specific one wins.">
                <input type="text"
                       ng-model="model.redirect.domain"
                       class="umb-property-editor umb-textstring"
                       placeholder="example.com">
            </umb-control-group>

            <umb-control-group label="Notes"
```

Replace with:

```html
            <umb-control-group label="Domain"
                               description="Leave blank to apply this redirect to all domains. If both a domain-specific and an all-domains redirect exist for the same Old URL, the domain-specific one wins.">
                <input type="text"
                       ng-model="model.redirect.domain"
                       class="umb-property-editor umb-textstring"
                       placeholder="example.com">
            </umb-control-group>

            <umb-control-group label="Valid from"
                               description="Leave blank to make this redirect active immediately.">
                <input type="datetime-local"
                       ng-model="model.redirect.validFrom"
                       class="umb-property-editor umb-textstring">
            </umb-control-group>

            <umb-control-group label="Valid until"
                               description="Leave blank to keep this redirect active indefinitely.">
                <input type="datetime-local"
                       ng-model="model.redirect.validUntil"
                       class="umb-property-editor umb-textstring">
            </umb-control-group>

            <umb-control-group label="Notes"
```

- [ ] **Step 6: Add the schedule badge to the list table's Active column (`dashboard.html`)**

Current:

```html
                            <td style="text-align:center;">
                                <span ng-class="{'redirect-active': redirect.isActive, 'redirect-inactive': !redirect.isActive}">
                                    {{redirect.isActive ? 'Yes' : 'No'}}
                                </span>
                            </td>
```

Replace with:

```html
                            <td style="text-align:center;">
                                <span ng-class="{'redirect-active': redirect.isActive, 'redirect-inactive': !redirect.isActive}">
                                    {{redirect.isActive ? 'Yes' : 'No'}}
                                </span>
                                <span ng-if="vm.getScheduleBadge(redirect)"
                                      class="schedule-badge"
                                      ng-class="{'scheduled': vm.getScheduleBadge(redirect) === 'Scheduled', 'expired': vm.getScheduleBadge(redirect) === 'Expired'}">
                                    {{vm.getScheduleBadge(redirect)}}
                                </span>
                            </td>
```

IMPORTANT: locate this exact block by its distinctive content (the `redirect-active`/`redirect-inactive` pair) — there is exactly ONE such block in `dashboard.html` (the redirects list table's Active column). If you find more than one match or can't find an unambiguous one, STOP and report BLOCKED describing what you found.

- [ ] **Step 7: Add the `.schedule-badge` CSS to `redirect.css`, right after the existing Active-indicator rules**

Current:

```css
/* ── Active indicators ── */
.redirect-active   { color: #2bc37b; font-weight: 600; }
.redirect-inactive { color: #d42054; font-weight: 600; }

/* ── Action buttons ── */
```

Replace with:

```css
/* ── Active indicators ── */
.redirect-active   { color: #2bc37b; font-weight: 600; }
.redirect-inactive { color: #d42054; font-weight: 600; }

.schedule-badge {
    display: inline-block;
    margin-left: 5px;
    padding: 1px 6px;
    border-radius: 4px;
    font-size: 10px;
    font-weight: 600;
}

.schedule-badge.scheduled { background: #dbeafe; color: #1e40af; }
.schedule-badge.expired   { background: #f3f4f6; color: #6b7280; }

/* ── Action buttons ── */
```

- [ ] **Step 8: Build to confirm the .NET project still compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 9: Commit**

```bash
git add App_Plugins/RedirectManager/modal.html App_Plugins/RedirectManager/dashboard.html App_Plugins/RedirectManager/redirect.controller.js App_Plugins/RedirectManager/redirect.css
git commit -m "$(cat <<'EOF'
feat: add Valid from/until scheduling fields and status badge to the AngularJS dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Manual verification — DEFERRED (documented, not executed)

Same constraint as every prior sub-project in this repo: no automated test project, no runnable Umbraco host in this repo, no local test site currently available. This documents what to run manually before this sub-project is considered done.

**Files:** none

- [ ] **Step 1 (deferred): Push to the local BaGet feed and install into a test site**

```bash
docker compose -f docker/docker-compose.yml up -d
./scripts/push-to-feed.sh
```

Then update the package in a test Umbraco site and start it so the new migration runs.

- [ ] **Step 2 (deferred): Confirm the migration applied cleanly**

Check startup logs for the migration plan completing without error, and confirm `RedirectManagerEntries` has the new `ValidFrom`/`ValidUntil` columns, with existing rows showing `NULL`.

- [ ] **Step 3 (deferred): Confirm existing (pre-migration) redirects still work unchanged**

Visit a path with an existing redirect rule and confirm it still redirects, since both new columns default to `NULL` (unbounded).

- [ ] **Step 4 (deferred): Confirm a future-dated rule doesn't fire yet**

Create a 301 rule with `Valid from` set a few hours in the future. Visit its `Old URL` and confirm it does NOT redirect (falls through to a normal 404, or to another matching rule if one exists) — the "Scheduled" badge should show in both dashboards' list views.

- [ ] **Step 5 (deferred): Confirm the rule starts firing once its `Valid from` passes**

Either wait for the scheduled time, or edit the rule to set `Valid from` a minute in the past, and confirm the redirect now fires.

- [ ] **Step 6 (deferred): Confirm an expired rule stops firing**

Create/edit a rule with `Valid until` set in the past. Visit its `Old URL` and confirm it does NOT redirect — the "Expired" badge should show in both dashboards' list views.

- [ ] **Step 7 (deferred): Confirm this applies to regex rules too (with the known 30s cache caveat)**

Create a regex rule with a `Valid until` a minute in the future. Wait past that time plus up to 30 seconds (the regex cache TTL) and confirm it stops firing.

- [ ] **Step 8 (deferred): Confirm the "until before from" client-side validation**

In both dashboards' add/edit modal, set `Valid until` earlier than `Valid from` and attempt to save — confirm it's rejected with the "Valid until must be after Valid from" message and no request is sent.

- [ ] **Step 9 (deferred): Confirm duplicate-detection still works for scheduled/expired rules**

Create a rule for `/foo`, then set its `Valid until` in the past (so it's now "Expired"). Attempt to create a second rule for the same `/foo` (same domain) — confirm it's still rejected as a duplicate, exactly as it would be for an `IsActive = false` rule today.

- [ ] **Step 10 (deferred): Confirm the dashboard UI round-trips the dates correctly across time zones**

Set `Valid from`/`Valid until` in one browser time zone, reload the dashboard, and confirm the same local date/time re-displays correctly in the edit modal (i.e. the UTC round-trip through the API didn't shift the displayed value).

---

## Out of scope for this plan

- Any server-side validation of `ValidUntil >= ValidFrom` — client-side only, per the approved spec.
- Exposing the validity window in the `Test` endpoint's response.
- Any change to `GetAllFiltered`/`GetAll` — the dashboard list continues to show every row regardless of schedule state.
- Any appsettings-level configurability — explicitly excluded from this roadmap batch.
- Version bump, git tag, and NuGet publish — happens once, after all 9 sub-projects in this batch are done, as a separate step outside this plan.
