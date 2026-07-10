# Preserve Query String Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let editors opt in, per redirect rule, to carrying the incoming request's query string forward onto the redirect target, so tracking/campaign params (`?utm_source=...`) survive a 301/302 instead of being silently dropped.

**Architecture:** Add a `PreserveQueryString` bool column to `RedirectEntry` (default `false`, fully backward compatible). Thread it through the DTOs and `RedirectService` create/update mapping unchanged from how every other bool flag (`IsActive`, `IsRegex`) is handled. In `RedirectMiddleware`, add one small static helper that merges the incoming `QueryString` onto a computed target URL (`&` if the target already has a `?`, otherwise a fresh `?`), and call it at both places a `Location` header is currently set for 301/302 (the exact-match branch and the regex-match branch). Add a checkbox to both dashboards' add/edit modal, next to the existing A/B-test toggle.

**Tech Stack:** NPoco via `IScopeProvider` (unchanged), ASP.NET Core (`HttpContext.Request.QueryString`), Lit and AngularJS dashboards (unchanged tech, new field/column).

Reference spec: `docs/superpowers/specs/2026-07-10-preserve-query-string-design.md`

This is sub-project 1 of 9 in the current roadmap batch. Each sub-project ships its own plan; no version bump/release happens here — that is a separate step once all 9 are done.

---

### Task 1: Add the `PreserveQueryString` column to `RedirectEntry`

**Files:**
- Modify: `Models/RedirectEntry.cs`

- [ ] **Step 1: Add the property**

Current (end of the class, after the A/B test columns):

```csharp
    [Column("VariantBLastHitDate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? VariantBLastHitDate { get; set; }
}
```

Replace with:

```csharp
    [Column("VariantBLastHitDate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? VariantBLastHitDate { get; set; }

    [Column("PreserveQueryString")]
    [Constraint(Default = false)]
    public bool PreserveQueryString { get; set; } = false;
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
feat: add PreserveQueryString column to RedirectEntry model

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add the `AddPreserveQueryStringColumn` migration step

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
    }
}
```

- [ ] **Step 2: Add the async (net10.0+) migration class**

In the `#if NET10_0_OR_GREATER` block, immediately after the closing brace of
the async `AddAbTestColumns` class (right before `#else`), insert:

```csharp
public class AddPreserveQueryStringColumn : AsyncMigrationBase
{
    public AddPreserveQueryStringColumn(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "PreserveQueryString") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "PreserveQueryString");
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Add the sync (net8.0) migration class**

In the `#else` block, immediately after the closing brace of the sync
`AddAbTestColumns` class (at the end of the file), insert:

```csharp
public class AddPreserveQueryStringColumn : MigrationBase
{
    public AddPreserveQueryStringColumn(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "PreserveQueryString") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "PreserveQueryString");
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
feat: add migration for RedirectEntry.PreserveQueryString column

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Add `PreserveQueryString` to the DTOs

**Files:**
- Modify: `Models/RedirectEntryDto.cs`

- [ ] **Step 1: Add the field to all three DTO classes**

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

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Models/RedirectEntryDto.cs
git commit -m "$(cat <<'EOF'
feat: add PreserveQueryString to redirect DTOs

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Wire `PreserveQueryString` through `RedirectService` and the API's `ToDto` mapping

**Files:**
- Modify: `Services/RedirectService.cs`
- Modify: `Controllers/RedirectApiController.cs:499-520`

- [ ] **Step 1: Map it in `Create` (`Services/RedirectService.cs`)**

Current:

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
            VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, isRegex),
            VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight,
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
            VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, isRegex),
            VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight,
            PreserveQueryString = dto.PreserveQueryString,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
```

- [ ] **Step 2: Map it in `Update` (`Services/RedirectService.cs`)**

Current:

```csharp
        existing.VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, existing.IsRegex);
        existing.VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight;
        existing.UpdatedDate = DateTime.UtcNow;
```

Replace with:

```csharp
        existing.VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, existing.IsRegex);
        existing.VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight;
        existing.PreserveQueryString = dto.PreserveQueryString;
        existing.UpdatedDate = DateTime.UtcNow;
```

- [ ] **Step 3: Map it in `ToDto` (`Controllers/RedirectApiController.cs`)**

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
            VariantBLastHitDate = r.VariantBLastHitDate
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
            PreserveQueryString = r.PreserveQueryString
        };
    }
```

- [ ] **Step 4: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 5: Commit**

```bash
git add Services/RedirectService.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: wire PreserveQueryString through service create/update and API mapping

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

Note: the `Test` endpoint (`GET .../test?path=`) is intentionally left
untouched — it is out of scope per the approved spec, same as domain
awareness was left out of it in the prior sub-project.

---

### Task 5: Apply query-string merging in `RedirectMiddleware`

**Files:**
- Modify: `Middleware/RedirectMiddleware.cs`

- [ ] **Step 1: Add the merge helper**

Current (end of file, after `ToggleTrailingSlash`):

```csharp
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

Replace with:

```csharp
    private static string? ToggleTrailingSlash(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return null;

        return path.EndsWith("/", StringComparison.Ordinal)
            ? path.TrimEnd('/')
            : path + "/";
    }

    // Appends the incoming request's query string onto a computed redirect
    // target when the matched rule opts in via PreserveQueryString. If
    // targetUrl already has its own query string (e.g. "/new?ref=campaign"),
    // the incoming one is appended with "&" rather than replacing it -- both
    // survive, with no de-duplication of overlapping parameter names (see
    // design spec's "Known edge case" section for why that's acceptable).
    private static string? AppendPreservedQueryString(string? targetUrl, bool preserve, QueryString incomingQuery)
    {
        if (!preserve || string.IsNullOrEmpty(targetUrl) || !incomingQuery.HasValue)
            return targetUrl;

        var incoming = incomingQuery.Value!.TrimStart('?');
        return targetUrl.Contains('?', StringComparison.Ordinal)
            ? $"{targetUrl}&{incoming}"
            : $"{targetUrl}?{incoming}";
    }
}
```

- [ ] **Step 2: Apply it at the exact-match 301/302 branch**

Current:

```csharp
                case 301:
                case 302:
                    var targetUrl = ResolveRedirectTarget(context, redirect);
                    context.Response.StatusCode = redirect.StatusCode;
                    context.Response.Headers.Location = targetUrl ?? "/";
                    return;
```

Replace with:

```csharp
                case 301:
                case 302:
                    var targetUrl = AppendPreservedQueryString(
                        ResolveRedirectTarget(context, redirect), redirect.PreserveQueryString, context.Request.QueryString);
                    context.Response.StatusCode = redirect.StatusCode;
                    context.Response.Headers.Location = targetUrl ?? "/";
                    return;
```

- [ ] **Step 3: Apply it at the regex-match 301/302 branch**

Current:

```csharp
            switch (regexRedirect.Entry.StatusCode)
            {
                case 301:
                    context.Response.StatusCode = 301;
                    context.Response.Headers.Location = regexRedirect.ComputedNewUrl ?? "/";
                    return;

                case 302:
                    context.Response.StatusCode = 302;
                    context.Response.Headers.Location = regexRedirect.ComputedNewUrl ?? "/";
                    return;
```

Replace with:

```csharp
            switch (regexRedirect.Entry.StatusCode)
            {
                case 301:
                    context.Response.StatusCode = 301;
                    context.Response.Headers.Location = AppendPreservedQueryString(
                        regexRedirect.ComputedNewUrl, regexRedirect.Entry.PreserveQueryString, context.Request.QueryString) ?? "/";
                    return;

                case 302:
                    context.Response.StatusCode = 302;
                    context.Response.Headers.Location = AppendPreservedQueryString(
                        regexRedirect.ComputedNewUrl, regexRedirect.Entry.PreserveQueryString, context.Request.QueryString) ?? "/";
                    return;
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
feat: append incoming query string to redirect target when PreserveQueryString is set

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Add the checkbox to the Lit dashboard (Umbraco 17+/18)

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add the default to `getEmptyFormData()`**

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
            variantBWeight: 50
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
            preserveQueryString: false
        };
    }
```

- [ ] **Step 2: Populate it in `openEditModal`**

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
            variantBWeight: redirect.variantBWeight ?? 50
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
            preserveQueryString: !!redirect.preserveQueryString
        };
        this.showModal = true;
    }
```

- [ ] **Step 3: Add the checkbox markup, right after the A/B test block**

Current (the A/B test `form-group` block ends, followed by the Domain
field further down — insert immediately after the closing `` `` : `''}`` of
the A/B test conditional block):

```javascript
                                        <div class="form-group">
                                            <label>Variant B weight — % of visitors sent to B</label>
                                            <input type="number"
                                                   name="variantBWeight"
                                                   min="0" max="100"
                                                   .value=${String(this.formData.variantBWeight)}
                                                   @input=${this.handleInputChange} />
                                            <small>A visitor is assigned once (cookie) and always sees the same variant afterward.</small>
                                        </div>
                                    ` : ''}
                                ` : ''}
```

Replace with:

```javascript
                                        <div class="form-group">
                                            <label>Variant B weight — % of visitors sent to B</label>
                                            <input type="number"
                                                   name="variantBWeight"
                                                   min="0" max="100"
                                                   .value=${String(this.formData.variantBWeight)}
                                                   @input=${this.handleInputChange} />
                                            <small>A visitor is assigned once (cookie) and always sees the same variant afterward.</small>
                                        </div>
                                    ` : ''}
                                ` : ''}

                                <!-- Preserve query string -->
                                ${this.formData.statusCode === 301 || this.formData.statusCode === 302 ? html`
                                    <div class="form-group">
                                        <div class="toggle-row">
                                            <label class="toggle-label" for="modal-preserveQueryString">
                                                Preserve query string
                                                <span class="toggle-hint"> — append the incoming request's query string to New URL</span>
                                            </label>
                                            <label class="toggle-switch">
                                                <input type="checkbox"
                                                       name="preserveQueryString"
                                                       id="modal-preserveQueryString"
                                                       .checked=${this.formData.preserveQueryString}
                                                       @change=${this.handleInputChange} />
                                                <span class="toggle-slider"></span>
                                            </label>
                                        </div>
                                    </div>
                                ` : ''}
```

Note: this reuses the same `toggle-row`/`toggle-label`/`toggle-switch`/
`toggle-slider` CSS classes the A/B test toggle already uses — no new CSS
needed. `handleInputChange` already handles `type === 'checkbox'` generically
by field name, so no changes are needed there.

- [ ] **Step 4: Build to confirm the .NET project still compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`
(this step doesn't type-check the JS, but confirms the package still builds
and the file didn't break any build-time asset copying).

- [ ] **Step 5: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "$(cat <<'EOF'
feat: add Preserve query string toggle to the Lit dashboard modal

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Add the toggle to the AngularJS dashboard (Umbraco 13)

**Files:**
- Modify: `App_Plugins/RedirectManager/modal.html`
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`

- [ ] **Step 1: Add the default to `openAddModal`'s model (`redirect.controller.js`)**

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
                    variantBWeight: 50
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
                    preserveQueryString: false
                },
```

- [ ] **Step 2: Populate it in `openEditModal`'s model (`redirect.controller.js`)**

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
                    variantBWeight: redirect.variantBWeight != null ? redirect.variantBWeight : 50
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
                    preserveQueryString: !!redirect.preserveQueryString
                },
```

- [ ] **Step 3: Add the toggle markup to `modal.html`, right after the Variant B weight field**

Current:

```html
            <umb-control-group label="Variant B weight"
                               description="% of visitors sent to Variant B. A visitor is assigned once (cookie) and always sees the same variant afterward."
                               ng-if="model.redirect.abTestEnabled && (model.redirect.statusCode == 301 || model.redirect.statusCode == 302) && !model.redirect.isRegex">
                <input type="number"
                       min="0" max="100"
                       ng-model="model.redirect.variantBWeight"
                       class="umb-property-editor umb-textstring">
            </umb-control-group>

            <umb-control-group label="Domain"
```

Replace with:

```html
            <umb-control-group label="Variant B weight"
                               description="% of visitors sent to Variant B. A visitor is assigned once (cookie) and always sees the same variant afterward."
                               ng-if="model.redirect.abTestEnabled && (model.redirect.statusCode == 301 || model.redirect.statusCode == 302) && !model.redirect.isRegex">
                <input type="number"
                       min="0" max="100"
                       ng-model="model.redirect.variantBWeight"
                       class="umb-property-editor umb-textstring">
            </umb-control-group>

            <umb-control-group label="Preserve query string"
                               description="Append the incoming request's query string (e.g. ?utm_source=...) to New URL"
                               ng-if="model.redirect.statusCode == 301 || model.redirect.statusCode == 302">
                <umb-toggle checked="model.redirect.preserveQueryString"
                            on-click="model.redirect.preserveQueryString = !model.redirect.preserveQueryString">
                </umb-toggle>
            </umb-control-group>

            <umb-control-group label="Domain"
```

Note: this AngularJS dashboard's existing labels in this file are all in
English ("Status Code", "New URL", "A/B test", "Domain", "Notes", "Active",
"Regex match"), contradicting the design spec's assumption that this
dashboard uses Turkish labels — corrected here during plan self-review to
match the file's actual, established convention. Both dashboards use the
English "Preserve query string" label.

- [ ] **Step 4: Build to confirm the .NET project still compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 5: Commit**

```bash
git add App_Plugins/RedirectManager/modal.html App_Plugins/RedirectManager/redirect.controller.js
git commit -m "$(cat <<'EOF'
feat: add Preserve query string toggle to the AngularJS dashboard modal

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Manual verification — DEFERRED (documented, not executed)

Same constraint as every prior sub-project in this repo: no automated test
project, no runnable Umbraco host in this repo, no local test site
currently available (sub-project 6 of this same roadmap batch,
"Unit/entegrasyon testleri," is what will eventually close this gap). This
documents what to run manually before this sub-project is considered done.

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
confirm `RedirectManagerEntries` has the new `PreserveQueryString` column,
with existing rows showing `0`/`false`.

- [ ] **Step 3 (deferred): Confirm existing redirects are unaffected**

Visit a path with an existing redirect rule with a query string on the
request (e.g. `/old-page?foo=bar`) and confirm the query string is
**dropped** as before (since `PreserveQueryString` defaults to `false`).

- [ ] **Step 4 (deferred): Confirm preserve-on, target has no existing query string**

Create a 301 rule `/promo` → `/landing` with "Preserve query string" on.
Visit `/promo?utm_source=google` and confirm the browser is redirected to
`/landing?utm_source=google`.

- [ ] **Step 5 (deferred): Confirm preserve-on, target already has a query string**

Edit the rule so `New URL` is `/landing?ref=campaign`. Visit
`/promo?utm_source=google` again and confirm the redirect target is
`/landing?ref=campaign&utm_source=google` (both survive).

- [ ] **Step 6 (deferred): Confirm it works for a regex rule**

Create a regex 301 rule (`OldUrl` = `^/blog/(.*)$`, `NewUrl` =
`/articles/$1`) with "Preserve query string" on. Visit
`/blog/my-post?ref=x` and confirm the target is
`/articles/my-post?ref=x`.

- [ ] **Step 7 (deferred): Confirm it applies to the A/B test variant actually served**

On a rule with A/B testing and "Preserve query string" both enabled, visit
the redirect enough times (or manipulate the `rm_ab_{id}` cookie) to observe
both Variant A and Variant B being served, and confirm the query string is
appended in both cases.

- [ ] **Step 8 (deferred): Confirm the dashboard UI round-trips the flag correctly**

Create, edit, and view a redirect with "Preserve query string" toggled on
through both dashboards (Umbraco 13's AngularJS one and Umbraco 17+/18's
Lit one, if both test environments are available) and confirm the toggle
state displays and persists correctly.

---

## Out of scope for this plan

- The `Test` endpoint (`GET .../test?path=`) does not simulate query-string
  preservation in its response — unchanged from before this plan, per the
  approved spec.
- De-duplicating query parameters that appear on both `NewUrl` and the
  incoming request — both copies are kept, per the approved spec.
- Any special-casing for rules whose `OldUrl` itself contains a literal
  query string — documented as a known, accepted edge case in the spec.
- 404/410 rules — no `NewUrl` target exists for the flag to apply to.
- Any appsettings-level configurability — explicitly excluded from this
  roadmap batch per the user's decision on sub-project 2.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step outside this plan.
