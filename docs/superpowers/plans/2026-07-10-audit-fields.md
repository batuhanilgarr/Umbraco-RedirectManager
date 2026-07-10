# Audit Fields (CreatedBy / ModifiedBy) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record which backoffice user created and last modified each redirect rule, and surface it in both dashboards as a row-level tooltip — no new database dependency beyond two nullable string columns, no new dashboard table column.

**Architecture:** Add nullable `CreatedBy`/`ModifiedBy` string columns to `RedirectEntry` (both `NULL` = no audit trail, fully backward compatible). Resolve the acting backoffice user's display name server-side in `RedirectApiController` via `IBackOfficeSecurityAccessor` (never trust a client-supplied value for audit data) and thread it through `RedirectService.Create`/`Update`/`BulkSetActive` as a plain method parameter. `RedirectEntryDto` gains `CreatedBy`/`ModifiedBy` plus (a gap found while writing this plan — see Task 3's note) `CreatedDate`/`UpdatedDate`, which were never exposed to the client before this feature and are needed for the tooltip's "on `<date>`" text; both new date fields get the same `DateTime.SpecifyKind(..., DateTimeKind.Utc)` treatment already established for `ValidFrom`/`ValidUntil` in a prior sub-project, since the same NPoco `DateTimeKind.Unspecified`-on-read issue applies to every `DateTime` column, not just those two. Both dashboards get a small helper that formats the tooltip text and a `title` attribute on each table row.

**Tech Stack:** NPoco via `IScopeProvider` (unchanged), `Umbraco.Cms.Core.Security.IBackOfficeSecurityAccessor` (new dependency, already available in both target Umbraco versions), Lit and AngularJS dashboards (unchanged tech, new helper + attribute).

Reference spec: `docs/superpowers/specs/2026-07-10-audit-fields-design.md`

This is sub-project 4 of 9 in the current roadmap batch. No version bump/release happens here — that is a separate step once all 9 sub-projects are done.

---

### Task 1: Add `CreatedBy`/`ModifiedBy` columns to `RedirectEntry`

**Files:**
- Modify: `Models/RedirectEntry.cs`

- [ ] **Step 1: Add the properties**

Current (end of the class):

```csharp
    [Column("ValidFrom")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ValidFrom { get; set; }

    [Column("ValidUntil")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ValidUntil { get; set; }
}
```

Replace with:

```csharp
    [Column("ValidFrom")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ValidFrom { get; set; }

    [Column("ValidUntil")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ValidUntil { get; set; }

    [Column("CreatedBy")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? CreatedBy { get; set; }

    [Column("ModifiedBy")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? ModifiedBy { get; set; }
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
feat: add CreatedBy/ModifiedBy columns to RedirectEntry model

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add the `AddAuditFieldColumns` migration step

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
        To<AddValidityWindowColumns>(new Guid("A2E5F8C1-3B6D-4A9E-8F17-5C0D9E4B7A63"));
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
        To<AddAuditFieldColumns>(new Guid("D3B6A947-8F2C-4E15-9A03-6D7B1C5E9F82"));
    }
}
```

- [ ] **Step 2: Add the async (net10.0+) migration class**

In the `#if NET10_0_OR_GREATER` block, immediately after the closing brace of the async `AddValidityWindowColumns` class (right before `#else`), insert:

```csharp
public class AddAuditFieldColumns : AsyncMigrationBase
{
    public AddAuditFieldColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "CreatedBy") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "CreatedBy");
        }

        if (ColumnExists(RedirectEntry.TableName, "ModifiedBy") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ModifiedBy");
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Add the sync (net8.0) migration class**

In the `#else` block, immediately after the closing brace of the sync `AddValidityWindowColumns` class (at the end of the file, right before `#endif`), insert:

```csharp
public class AddAuditFieldColumns : MigrationBase
{
    public AddAuditFieldColumns(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "CreatedBy") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "CreatedBy");
        }

        if (ColumnExists(RedirectEntry.TableName, "ModifiedBy") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ModifiedBy");
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
feat: add migration for RedirectEntry.CreatedBy/ModifiedBy columns

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Add `CreatedBy`/`ModifiedBy`/`CreatedDate`/`UpdatedDate` to `RedirectEntryDto`

**Files:**
- Modify: `Models/RedirectEntryDto.cs`

**Gap found while writing this plan:** the approved spec only called for adding `CreatedBy`/`ModifiedBy` to the read DTO, but the dashboard tooltip it describes ("Created by X on `<date>` · Last modified by Y on `<date>`") also needs the *dates*, and `CreatedDate`/`UpdatedDate` were never exposed on `RedirectEntryDto` at all before this feature (confirmed: no `CreatedDate`/`UpdatedDate` property exists on any DTO in `Models/RedirectEntryDto.cs` today). This task adds both, since the tooltip cannot be built without them, and the fix is applied here rather than left as a follow-up.

Only `RedirectEntryDto` (the read/response DTO) changes — `CreateRedirectEntryDto`/`UpdateRedirectEntryDto` do **not** gain these fields, since none of the four are ever client-supplied.

- [ ] **Step 1: Add the fields to `RedirectEntryDto`**

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
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
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
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }
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
feat: add CreatedBy/ModifiedBy/CreatedDate/UpdatedDate to RedirectEntryDto

CreatedDate/UpdatedDate were never exposed on the read DTO before this
feature; adding them here because the audit tooltip this feature adds to
both dashboards needs the actual dates, not just the actor names.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Wire the acting user's name through the service layer and controller

**Files:**
- Modify: `Services/IRedirectService.cs`
- Modify: `Services/RedirectService.cs`
- Modify: `Controllers/RedirectApiController.cs`

- [ ] **Step 1: Update `IRedirectService`'s mutating method signatures**

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
    IEnumerable<RedirectEntry> GetActiveWildcardEntries();
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
    RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
}
```

`Delete`/`BulkDelete` are intentionally unchanged — a deleted row has no audit trail to update.

- [ ] **Step 2: Update `RedirectService.Create`**

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
            PreserveQueryString = dto.PreserveQueryString,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        using var scope = _scopeProvider.CreateScope();
        scope.Database.Insert(entry);
        scope.Complete();

        InvalidateMatchCaches();

        return entry;
    }
```

Replace with:

```csharp
    public RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName)
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
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            CreatedBy = actorName,
            ModifiedBy = actorName,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        using var scope = _scopeProvider.CreateScope();
        scope.Database.Insert(entry);
        scope.Complete();

        InvalidateMatchCaches();

        return entry;
    }
```

- [ ] **Step 3: Update `RedirectService.Update`**

Current:

```csharp
    public RedirectEntry? Update(int id, UpdateRedirectEntryDto dto)
    {
        using var scope = _scopeProvider.CreateScope();
        var existing = scope.Database.SingleOrDefault<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE Id = @0", id);

        if (existing == null)
        {
            scope.Complete();
            return null;
        }

        existing.IsRegex = dto.IsRegex;
        existing.OldUrl = NormalizeOldUrl(dto.OldUrl, existing.IsRegex);
        existing.NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, existing.IsRegex);
        existing.Domain = DomainNormalizer.Normalize(dto.Domain);
        existing.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        existing.StatusCode = ValidateStatusCode(dto.StatusCode);
        existing.IsActive = dto.IsActive;
        existing.VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, existing.IsRegex);
        existing.VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight;
        existing.PreserveQueryString = dto.PreserveQueryString;
        existing.ValidFrom = dto.ValidFrom;
        existing.ValidUntil = dto.ValidUntil;
        existing.UpdatedDate = DateTime.UtcNow;

        scope.Database.Update(existing);
        scope.Complete();

        InvalidateMatchCaches();

        return existing;
    }
```

Replace with:

```csharp
    public RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName)
    {
        using var scope = _scopeProvider.CreateScope();
        var existing = scope.Database.SingleOrDefault<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE Id = @0", id);

        if (existing == null)
        {
            scope.Complete();
            return null;
        }

        existing.IsRegex = dto.IsRegex;
        existing.OldUrl = NormalizeOldUrl(dto.OldUrl, existing.IsRegex);
        existing.NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, existing.IsRegex);
        existing.Domain = DomainNormalizer.Normalize(dto.Domain);
        existing.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        existing.StatusCode = ValidateStatusCode(dto.StatusCode);
        existing.IsActive = dto.IsActive;
        existing.VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, existing.IsRegex);
        existing.VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight;
        existing.PreserveQueryString = dto.PreserveQueryString;
        existing.ValidFrom = dto.ValidFrom;
        existing.ValidUntil = dto.ValidUntil;
        existing.ModifiedBy = actorName;
        existing.UpdatedDate = DateTime.UtcNow;

        scope.Database.Update(existing);
        scope.Complete();

        InvalidateMatchCaches();

        return existing;
    }
```

Note: `CreatedBy` is deliberately not touched here — it's read from `existing` (already populated from the DB) and never reassigned, so it survives every subsequent update untouched.

- [ ] **Step 4: Update `RedirectService.BulkSetActive`**

Current:

```csharp
    public int BulkSetActive(IEnumerable<int> ids, bool isActive)
    {
        var idList = ids?.Distinct().ToArray() ?? Array.Empty<int>();
        if (idList.Length == 0)
            return 0;

        using var scope = _scopeProvider.CreateScope();
        var args = new List<object> { isActive ? 1 : 0, DateTime.UtcNow };
        var placeholders = string.Join(",", idList.Select((_, i) => $"@{i + args.Count}"));
        args.AddRange(idList.Cast<object>());
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

Replace with:

```csharp
    public int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName)
    {
        var idList = ids?.Distinct().ToArray() ?? Array.Empty<int>();
        if (idList.Length == 0)
            return 0;

        using var scope = _scopeProvider.CreateScope();
        // actorName may be null here; NPoco maps a null CLR value in the params
        // array to a parameterized DBNull, the same as any other nullable column
        // written via Insert/Update elsewhere in this class -- no special-casing
        // needed for the null case.
        var args = new List<object> { isActive ? 1 : 0, DateTime.UtcNow, actorName };
        var placeholders = string.Join(",", idList.Select((_, i) => $"@{i + args.Count}"));
        args.AddRange(idList.Cast<object>());
        var sql = $"UPDATE {RedirectEntry.TableName} SET IsActive = @0, UpdatedDate = @1, ModifiedBy = @2 WHERE Id IN ({placeholders})";
        var rowsAffected = scope.Database.Execute(sql, args.ToArray());
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateMatchCaches();
        }

        return rowsAffected;
    }
```

- [ ] **Step 5: Build to confirm the service-layer changes compile (the controller will fail to build until Step 6 is done — that's expected)**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: build errors in `Controllers/RedirectApiController.cs` about `Create`/`Update`/`BulkSetActive` call sites missing the new required `actorName` argument. Confirm the errors are specifically about those three method calls, not something else, then continue to Step 6 — do not commit yet.

- [ ] **Step 6: Add the `IBackOfficeSecurityAccessor` dependency and a `GetCurrentUserName()` helper to the controller**

Current (top of file, imports and constructor):

```csharp
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Route("umbraco/api/redirectmanager")]
public class RedirectApiController : Controller
{
    private readonly IRedirectService _redirectService;
    private readonly IMissedRequestService _missedRequestService;
    private readonly IRedirectTelemetryPinger _telemetryPinger;
    private readonly IRedirectTelemetrySettingsStore _telemetrySettingsStore;
    private readonly IRedirectVersionChecker _versionChecker;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectApiController(
        IRedirectService redirectService,
        IMissedRequestService missedRequestService,
        IRedirectTelemetryPinger telemetryPinger,
        IRedirectTelemetrySettingsStore telemetrySettingsStore,
        IRedirectVersionChecker versionChecker)
    {
        _redirectService = redirectService;
        _missedRequestService = missedRequestService;
        _telemetryPinger = telemetryPinger;
        _telemetrySettingsStore = telemetrySettingsStore;
        _versionChecker = versionChecker;
    }
```

Replace with:

```csharp
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Route("umbraco/api/redirectmanager")]
public class RedirectApiController : Controller
{
    private readonly IRedirectService _redirectService;
    private readonly IMissedRequestService _missedRequestService;
    private readonly IRedirectTelemetryPinger _telemetryPinger;
    private readonly IRedirectTelemetrySettingsStore _telemetrySettingsStore;
    private readonly IRedirectVersionChecker _versionChecker;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectApiController(
        IRedirectService redirectService,
        IMissedRequestService missedRequestService,
        IRedirectTelemetryPinger telemetryPinger,
        IRedirectTelemetrySettingsStore telemetrySettingsStore,
        IRedirectVersionChecker versionChecker,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor)
    {
        _redirectService = redirectService;
        _missedRequestService = missedRequestService;
        _telemetryPinger = telemetryPinger;
        _telemetrySettingsStore = telemetrySettingsStore;
        _versionChecker = versionChecker;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
    }

    // Resolves the display name of the currently authenticated backoffice user,
    // for stamping onto CreatedBy/ModifiedBy. Every endpoint on this controller
    // is already gated by [Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)],
    // so this is expected to be non-null on every real request; the nullable
    // return type is a defensive fallback (e.g. a future non-interactive
    // caller), not an expected common case. Deliberately resolved server-side
    // from the authenticated identity rather than accepted from the request
    // body, since a client should never be able to dictate who audit data
    // attributes a change to.
    private string? GetCurrentUserName() =>
        _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Name;
```

- [ ] **Step 7: Update the `Create` action**

Current:

```csharp
        var redirect = _redirectService.Create(dto);
        return Ok(ToDto(redirect));
    }

    [HttpPut("update/{id:int}")]
```

Replace with:

```csharp
        var redirect = _redirectService.Create(dto, GetCurrentUserName());
        return Ok(ToDto(redirect));
    }

    [HttpPut("update/{id:int}")]
```

- [ ] **Step 8: Update the `Update` action**

Current:

```csharp
        var redirect = _redirectService.Update(id, dto);
        if (redirect == null)
            return NotFound();

        return Ok(ToDto(redirect));
    }

    [HttpDelete("delete/{id:int}")]
```

Replace with:

```csharp
        var redirect = _redirectService.Update(id, dto, GetCurrentUserName());
        if (redirect == null)
            return NotFound();

        return Ok(ToDto(redirect));
    }

    [HttpDelete("delete/{id:int}")]
```

- [ ] **Step 9: Update the `BulkActivate`/`BulkDeactivate` actions**

Current:

```csharp
    [HttpPost("bulk/activate")]
    public IActionResult BulkActivate([FromBody] BulkIdsDto dto)
    {
        var updated = _redirectService.BulkSetActive(dto.Ids, true);
        return Ok(new { updated });
    }

    [HttpPost("bulk/deactivate")]
    public IActionResult BulkDeactivate([FromBody] BulkIdsDto dto)
    {
        var updated = _redirectService.BulkSetActive(dto.Ids, false);
        return Ok(new { updated });
    }
```

Replace with:

```csharp
    [HttpPost("bulk/activate")]
    public IActionResult BulkActivate([FromBody] BulkIdsDto dto)
    {
        var updated = _redirectService.BulkSetActive(dto.Ids, true, GetCurrentUserName());
        return Ok(new { updated });
    }

    [HttpPost("bulk/deactivate")]
    public IActionResult BulkDeactivate([FromBody] BulkIdsDto dto)
    {
        var updated = _redirectService.BulkSetActive(dto.Ids, false, GetCurrentUserName());
        return Ok(new { updated });
    }
```

- [ ] **Step 10: Update `ImportCsv` to resolve the actor once and pass it to both `Create`/`Update` calls**

Current:

```csharp
        int created = 0;
        int updated = 0;
        int skipped = 0;

        for (var rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
```

Replace with:

```csharp
        int created = 0;
        int updated = 0;
        int skipped = 0;
        var actorName = GetCurrentUserName();

        for (var rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
```

Then, current:

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
            else
            {
                _redirectService.Update(existing.Id, dto);
                updated++;
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
                }, actorName);
                created++;
            }
            else
            {
                _redirectService.Update(existing.Id, dto, actorName);
                updated++;
            }
```

- [ ] **Step 11: Update `ToDto` to map the four new fields**

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
            PreserveQueryString = r.PreserveQueryString,
            ValidFrom = AsUtc(r.ValidFrom),
            ValidUntil = AsUtc(r.ValidUntil)
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
            ValidFrom = AsUtc(r.ValidFrom),
            ValidUntil = AsUtc(r.ValidUntil),
            CreatedBy = r.CreatedBy,
            ModifiedBy = r.ModifiedBy,
            CreatedDate = DateTime.SpecifyKind(r.CreatedDate, DateTimeKind.Utc),
            UpdatedDate = DateTime.SpecifyKind(r.UpdatedDate, DateTimeKind.Utc)
        };
    }
```

`CreatedDate`/`UpdatedDate` on `RedirectEntry` are non-nullable `DateTime`, so this applies `DateTime.SpecifyKind` directly rather than through the nullable-typed `AsUtc` helper (which exists specifically for the nullable `ValidFrom`/`ValidUntil` case) — same underlying fix (NPoco returns `DateTimeKind.Unspecified` on read, which `System.Text.Json` would otherwise serialize without a `Z` suffix, causing the dashboards' `new Date(...)` parsing to misread it as local time), applied inline since these two fields are non-nullable.

- [ ] **Step 12: Build to confirm everything compiles now**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 13: Commit**

```bash
git add Services/IRedirectService.cs Services/RedirectService.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: stamp CreatedBy/ModifiedBy from the authenticated backoffice user on create/update/bulk-activate

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Add the audit tooltip to the Lit dashboard (Umbraco 17+/18)

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add the `getAuditTitle` helper, right after `getLastHitTitle`**

Current:

```javascript
    getLastHitTitle(redirect) {
        return redirect.lastHitDate
            ? `Last hit: ${new Date(redirect.lastHitDate).toLocaleString()}`
            : 'Never hit';
    }

    // Converts a stored UTC ISO string (or null) into the local-time string
```

Replace with:

```javascript
    getLastHitTitle(redirect) {
        return redirect.lastHitDate
            ? `Last hit: ${new Date(redirect.lastHitDate).toLocaleString()}`
            : 'Never hit';
    }

    getAuditTitle(redirect) {
        const created = redirect.createdDate ? new Date(redirect.createdDate).toLocaleString() : null;
        const modified = redirect.updatedDate ? new Date(redirect.updatedDate).toLocaleString() : null;

        const createdPart = created ? `Created${redirect.createdBy ? ` by ${redirect.createdBy}` : ''} on ${created}` : '';
        const modifiedPart = modified ? `Last modified${redirect.modifiedBy ? ` by ${redirect.modifiedBy}` : ''} on ${modified}` : '';

        return [createdPart, modifiedPart].filter(Boolean).join(' · ');
    }

    // Converts a stored UTC ISO string (or null) into the local-time string
```

- [ ] **Step 2: Add the `title` attribute to the list table's row**

Current:

```javascript
                                ${this.redirects.map(redirect => html`
                                    <tr class="${this.selectedIds.includes(redirect.id) ? 'row-selected' : ''}">
```

Replace with:

```javascript
                                ${this.redirects.map(redirect => html`
                                    <tr class="${this.selectedIds.includes(redirect.id) ? 'row-selected' : ''}" title="${this.getAuditTitle(redirect)}">
```

IMPORTANT: locate this exact block by its distinctive content (the `class="${this.selectedIds.includes(redirect.id)...` expression) — there is exactly ONE such `<tr>` in the file (the redirects list table). If you find more than one match or can't find an unambiguous one, STOP and report BLOCKED describing what you found.

- [ ] **Step 3: Build to confirm the .NET project still compiles, then verify JS syntax**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
node --check App_Plugins/RedirectManager/redirect-dashboard.js
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`, and `node --check` produces no output.

- [ ] **Step 4: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "$(cat <<'EOF'
feat: show CreatedBy/ModifiedBy audit trail as a row tooltip in the Lit dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Add the audit tooltip to the AngularJS dashboard (Umbraco 13)

**Files:**
- Modify: `App_Plugins/RedirectManager/dashboard.html`
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`

- [ ] **Step 1: Add the `vm.getAuditTitle` helper, right after `vm.getMatchTypeLabel`**

Current:

```javascript
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

Replace with:

```javascript
        vm.getMatchTypeLabel = function (redirect) {
            if (redirect.isRegex) {
                return "Regex";
            }
            if (redirect.oldUrl && redirect.oldUrl.indexOf("*") !== -1) {
                return "Wildcard";
            }
            return "Exact";
        };

        vm.getAuditTitle = function (redirect) {
            var created = redirect.createdDate ? new Date(redirect.createdDate).toLocaleString() : null;
            var modified = redirect.updatedDate ? new Date(redirect.updatedDate).toLocaleString() : null;

            var createdPart = created ? "Created" + (redirect.createdBy ? " by " + redirect.createdBy : "") + " on " + created : "";
            var modifiedPart = modified ? "Last modified" + (redirect.modifiedBy ? " by " + redirect.modifiedBy : "") + " on " + modified : "";

            return [createdPart, modifiedPart].filter(Boolean).join(" · ");
        };

        vm.loadRedirects = function () {
```

- [ ] **Step 2: Add the `title` attribute to the list table's row in `dashboard.html`**

Current:

```html
                    <tbody>
                        <tr ng-repeat="redirect in vm.redirects">
```

Replace with:

```html
                    <tbody>
                        <tr ng-repeat="redirect in vm.redirects" title="{{vm.getAuditTitle(redirect)}}">
```

IMPORTANT: locate this exact block by its distinctive content (the `ng-repeat="redirect in vm.redirects"` attribute) — there is exactly ONE such `<tr>` in `dashboard.html`. If you find more than one match or can't find an unambiguous one, STOP and report BLOCKED describing what you found.

- [ ] **Step 3: Build to confirm the .NET project still compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit**

```bash
git add App_Plugins/RedirectManager/dashboard.html App_Plugins/RedirectManager/redirect.controller.js
git commit -m "$(cat <<'EOF'
feat: show CreatedBy/ModifiedBy audit trail as a row tooltip in the AngularJS dashboard

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

Check startup logs for the migration plan completing without error, and confirm `RedirectManagerEntries` has the new `CreatedBy`/`ModifiedBy` columns, with existing rows showing `NULL`.

- [ ] **Step 3 (deferred): Confirm a new redirect stamps both fields**

Log into the backoffice as a specific user, create a new redirect through either dashboard, then hover over the new row (on a cell other than Old URL/New URL) and confirm the tooltip shows "Created by `<your name>` on `<today's date/time>` · Last modified by `<your name>` on `<today's date/time>`" (both the same, since it was just created).

- [ ] **Step 4 (deferred): Confirm editing updates only ModifiedBy**

Log in as a *different* backoffice user, edit the redirect from Step 3 (e.g. change its Notes), save, and confirm the tooltip now shows the ORIGINAL creator in the "Created by" clause and the SECOND user in the "Last modified by" clause, with an updated modified date/time.

- [ ] **Step 5 (deferred): Confirm bulk activate/deactivate updates ModifiedBy**

Select the redirect from Step 3/4 in the list, use "Deactivate selected" (or the equivalent bulk action) as a third backoffice user, and confirm the tooltip's "Last modified by" now shows that third user.

- [ ] **Step 6 (deferred): Confirm CSV import stamps the importing user**

Import a CSV containing a brand-new `OldUrl` and confirm the resulting row's tooltip shows the importing user as both `CreatedBy` and `ModifiedBy`. Then re-import the same CSV (which should update the existing row) as a different user and confirm only `ModifiedBy` changes.

- [ ] **Step 7 (deferred): Confirm pre-existing rows show a graceful fallback**

Find (or leave alone) a redirect created before this migration ran (`CreatedBy`/`ModifiedBy` both `NULL`) and confirm its tooltip reads just "Created on `<date>` · Last modified on `<date>`" with no "by `<null>`"/"by undefined" text and no broken formatting.

- [ ] **Step 8 (deferred): Confirm the tooltip doesn't fight with the Old URL/New URL cell tooltips**

Hover directly over the Old URL or New URL cell of a row and confirm the browser shows THAT cell's own tooltip (the untruncated URL), not the row-level audit tooltip; then hover over any other cell in the same row (e.g. Domain, Match, Active, Hits) and confirm the audit tooltip appears there instead.

---

## Out of scope for this plan

- Any UI to browse/filter by `CreatedBy`/`ModifiedBy`.
- Any change to `Delete`/`BulkDelete`, `GetByOldUrlAndIsRegex`, `GetAllFiltered`/`GetAll`'s query shape (beyond the two new columns being included automatically via `SELECT *`), or the `Test` endpoint.
- CSV export/import column changes beyond passing the resolved actor name into the existing `Create`/`Update` calls — the CSV file format itself is unchanged.
- Falling back to a numeric user ID display if the name is unavailable.
- Any appsettings-level configurability.
- Version bump, git tag, and NuGet publish — happens once, after all 9 sub-projects in this batch are done, as a separate step outside this plan.
