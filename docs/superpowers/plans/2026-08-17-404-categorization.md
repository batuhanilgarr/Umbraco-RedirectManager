# 404 Log Categorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Category` field to 404 log entries (`MissedRequest`), with filter chips + counts, per-row and bulk category assignment, and a narrow auto-classification ruleset applied to newly-ingested 404s — replacing the current hard-delete "Dismiss" action — across both dashboard UIs (legacy AngularJS for Umbraco 13, Lit for Umbraco 17/18).

**Architecture:** One new nvarchar column on the existing `RedirectManagerMissedRequests` table (default `"Unclassified"`), two new controller endpoints (single + bulk category set) following the codebase's existing `bulk/*` pattern, a pure regex classifier called only from the raw-SQL insert path in `MissedRequestFlushService`, and parallel UI changes (state, fetch calls, chip row, per-row select, checkbox+bulk-apply bar) implemented independently in `redirect-dashboard.js` (Lit) and `redirect.controller.js`/`dashboard.html`/`redirect.resource.js` (AngularJS).

**Tech Stack:** ASP.NET Core / NPoco (net8.0 + net10.0 dual target), Umbraco Cms migrations, xUnit + NSubstitute, Lit web components, AngularJS 1.x.

**Spec:** `docs/superpowers/specs/2026-08-17-404-categorization-design.md`

**Worktree:** This plan executes in `/Users/bhan/Documents/works/sites/RedirectManager-404-categorization` (branch `404-categorization`), created off `main`. All file paths below are relative to that worktree root.

---

### Task 1: `MissedRequestCategory` enum + `Category` on the model/DTO

**Files:**
- Create: `Models/MissedRequestCategory.cs`
- Modify: `Models/MissedRequest.cs`
- Modify: `Models/MissedRequestDto.cs`
- Modify: `Controllers/RedirectApiController.cs:648-659` (`ToDto`)

- [ ] **Step 1: Create the enum**

```csharp
namespace Umbraco.RedirectManager.Models;

public enum MissedRequestCategory
{
    Unclassified,
    MaliciousScanner,
    MissingAsset,
    RedirectNeeded,
    Gone,
    TypoMalformed,
    NeedsInvestigation
}
```

- [ ] **Step 2: Add the `Category` column to `MissedRequest`**

In `Models/MissedRequest.cs`, add after the `LastSeenDate` property (before the closing `}`):

```csharp
    [Column("Category")]
    [Length(32)]
    [Constraint(Default = "Unclassified")]
    public string Category { get; set; } = nameof(MissedRequestCategory.Unclassified);
```

- [ ] **Step 3: Add `category` to the DTO**

In `Models/MissedRequestDto.cs`, add after `LastSeenDate`:

```csharp
    public string Category { get; set; } = nameof(MissedRequestCategory.Unclassified);
```

- [ ] **Step 4: Map it in `ToDto`**

In `Controllers/RedirectApiController.cs`, update the `ToDto` method (currently lines 648-659):

```csharp
    private static MissedRequestDto ToDto(MissedRequest m)
    {
        return new MissedRequestDto
        {
            Id = m.Id,
            Path = m.Path,
            Domain = m.Domain,
            HitCount = m.HitCount,
            FirstSeenDate = m.FirstSeenDate,
            LastSeenDate = m.LastSeenDate,
            Category = m.Category
        };
    }
```

- [ ] **Step 5: Build to confirm no compile errors**

Run: `dotnet build Umbraco.RedirectManager.csproj -f net10.0`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add Models/MissedRequestCategory.cs Models/MissedRequest.cs Models/MissedRequestDto.cs Controllers/RedirectApiController.cs
git commit -m "feat: add Category field to MissedRequest model and DTO"
```

---

### Task 2: Migration — add `Category` column

**Files:**
- Modify: `Migrations/RedirectManagerMigrationPlan.cs`

- [ ] **Step 1: Register the migration in `DefinePlan()`**

In `Migrations/RedirectManagerMigrationPlan.cs`, change line 25 from:

```csharp
        To<AddCultureColumn>(new Guid("E4F7A208-3C5B-46D2-9A81-7F0C3E6B4D95"));
    }
```

to:

```csharp
        To<AddCultureColumn>(new Guid("E4F7A208-3C5B-46D2-9A81-7F0C3E6B4D95"));
        To<AddMissedRequestCategoryColumn>(new Guid("F5A8B319-4D6C-47E3-A092-8B1D4F7C5E06"));
    }
```

- [ ] **Step 2: Add the async (net10.0) migration class**

In the `#if NET10_0_OR_GREATER` block, immediately after the `AddCultureColumn : AsyncMigrationBase` class (currently ending at line 291, right before `#else`), insert:

```csharp
public class AddMissedRequestCategoryColumn : AsyncMigrationBase
{
    public AddMissedRequestCategoryColumn(IMigrationContext context) : base(context)
    {
    }

    protected override async Task MigrateAsync()
    {
        if (TableExists(MissedRequest.TableName) == false)
        {
            return;
        }

        if (ColumnExists(MissedRequest.TableName, "Category") == false)
        {
            AddColumn<MissedRequest>(MissedRequest.TableName, "Category");
            await Database.ExecuteAsync(
                $"UPDATE {MissedRequest.TableName} SET Category = 'Unclassified' WHERE Category IS NULL");
        }
    }
}
```

- [ ] **Step 3: Add the sync (net8.0) migration class**

In the `#else` block, immediately after the `AddCultureColumn : MigrationBase` class (currently ending at line 533, right before `#endif`), insert:

```csharp
public class AddMissedRequestCategoryColumn : MigrationBase
{
    public AddMissedRequestCategoryColumn(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(MissedRequest.TableName) == false)
        {
            return;
        }

        if (ColumnExists(MissedRequest.TableName, "Category") == false)
        {
            AddColumn<MissedRequest>(MissedRequest.TableName, "Category");
            Database.Execute(
                $"UPDATE {MissedRequest.TableName} SET Category = 'Unclassified' WHERE Category IS NULL");
        }
    }
}
```

- [ ] **Step 4: Build both target frameworks**

Run: `dotnet build Umbraco.RedirectManager.csproj -f net10.0 && dotnet build Umbraco.RedirectManager.csproj -f net8.0`
Expected: `Build succeeded.` for both.

- [ ] **Step 5: Commit**

```bash
git add Migrations/RedirectManagerMigrationPlan.cs
git commit -m "feat: add migration for MissedRequest.Category column"
```

---

### Task 3: Service layer — `SetCategory`, `BulkSetCategory`, auto-classification

**Files:**
- Modify: `Services/MissedRequestService.cs`
- Modify: `Services/MissedRequestFlushService.cs:94-137` (`UpsertOne`)
- Create: `Umbraco.RedirectManager.Tests/Services/MissedRequestServiceTests.cs`
- Create: `Umbraco.RedirectManager.Tests/Services/MissedRequestClassifierTests.cs`

- [ ] **Step 1: Write the failing classifier test**

The regex rules live in a small static class so both the flush service and the tests can call them without spinning up a database. Create `Umbraco.RedirectManager.Tests/Services/MissedRequestClassifierTests.cs`:

```csharp
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Tests.Services;

public class MissedRequestClassifierTests
{
    [Theory]
    [InlineData("/wp-login.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/wp-admin/setup-config.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/.env", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/.git/config", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/some-random-scan.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/admin", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/phpmyadmin/index.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/xmlrpc.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/assets/app.js", MissedRequestCategory.MissingAsset)]
    [InlineData("/styles/site.css", MissedRequestCategory.MissingAsset)]
    [InlineData("/assets/app.js.map", MissedRequestCategory.MissingAsset)]
    [InlineData("/images/logo.png", MissedRequestCategory.MissingAsset)]
    [InlineData("/fonts/icon.woff2", MissedRequestCategory.MissingAsset)]
    [InlineData("/old-product-page", MissedRequestCategory.Unclassified)]
    [InlineData("/blog/2019/my-post", MissedRequestCategory.Unclassified)]
    public void Classify_returns_expected_category(string path, MissedRequestCategory expected)
    {
        var result = MissedRequestClassifier.Classify(path);
        Assert.Equal(expected, result);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter MissedRequestClassifierTests`
Expected: FAIL — `MissedRequestClassifier` does not exist.

- [ ] **Step 3: Implement the classifier**

Add to `Services/MissedRequestService.cs` (new file-scoped class in the same file, or a new file — put it in the same file since it's small and only ever used alongside `MissedRequestService`):

```csharp
using System.Text.RegularExpressions;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IMissedRequestService
{
    IEnumerable<MissedRequest> GetAll();
    bool Delete(int id);
    bool SetCategory(int id, MissedRequestCategory category);
    int BulkSetCategory(IEnumerable<int> ids, MissedRequestCategory category);
}

// Deliberately narrow: only the exact patterns named in the customer's feature
// request (scanner probes and static-asset extensions). Anything else stays
// Unclassified for a human to triage -- this is not meant to be a general WAF
// ruleset, just enough to clear the obvious noise automatically.
public static class MissedRequestClassifier
{
    private static readonly Regex MaliciousScannerPattern = new(
        @"\.php$|^/wp-|/\.env(/|$)|/\.git/|^/(admin|phpmyadmin|xmlrpc\.php)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MissingAssetPattern = new(
        @"\.(js|css|map|jpg|jpeg|png|gif|svg|webp|ico|woff|woff2|ttf)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static MissedRequestCategory Classify(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return MissedRequestCategory.Unclassified;
        }

        if (MaliciousScannerPattern.IsMatch(path))
        {
            return MissedRequestCategory.MaliciousScanner;
        }

        if (MissingAssetPattern.IsMatch(path))
        {
            return MissedRequestCategory.MissingAsset;
        }

        return MissedRequestCategory.Unclassified;
    }
}
```

- [ ] **Step 4: Run the classifier test again to verify it passes**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter MissedRequestClassifierTests`
Expected: PASS (14 tests).

- [ ] **Step 5: Write the failing `MissedRequestService` tests**

These need a real (in-memory SQLite) `IScopeProvider`. Check how `RedirectService`'s own tests (if any) set up a scope — if no existing test in this suite exercises `IScopeProvider` directly, use NSubstitute to fake it at the SQL level is impractical for NPoco; instead, test only the pure/deterministic parts through the controller test in Task 4, and keep `MissedRequestServiceTests` to what's verifiable without a live scope. Create `Umbraco.RedirectManager.Tests/Services/MissedRequestServiceTests.cs`:

```csharp
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class MissedRequestServiceTests
{
    [Fact]
    public void BulkSetCategory_with_empty_ids_returns_zero_without_touching_the_database()
    {
        // IMissedRequestService requires a real IScopeProvider/database to exercise the
        // SQL path meaningfully (see MissedRequestClassifierTests for the pure-logic
        // coverage, and RedirectApiControllerTests for the controller-level pass-through
        // coverage using a substituted IMissedRequestService). This test only pins down
        // the short-circuit contract: an empty id list must never reach the database.
        var service = new MissedRequestService(scopeProvider: null!);

        var updated = service.BulkSetCategory(Array.Empty<int>(), MissedRequestCategory.Gone);

        Assert.Equal(0, updated);
    }
}
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter MissedRequestServiceTests`
Expected: FAIL — `SetCategory`/`BulkSetCategory` don't exist yet on `MissedRequestService`.

- [ ] **Step 7: Implement `SetCategory` and `BulkSetCategory`**

Replace the body of `MissedRequestService` in `Services/MissedRequestService.cs` (keep `GetAll`/`Delete` as-is) by adding, following the exact batched-`UPDATE` pattern already used by `RedirectService.BulkSetActive` (`Services/RedirectService.cs:367-383`):

```csharp
    public bool SetCategory(int id, MissedRequestCategory category)
    {
        using var scope = _scopeProvider.CreateScope();
        var rowsAffected = scope.Database.Execute(
            $"UPDATE {MissedRequest.TableName} SET Category = @0 WHERE Id = @1",
            category.ToString(), id);
        scope.Complete();
        return rowsAffected > 0;
    }

    public int BulkSetCategory(IEnumerable<int> ids, MissedRequestCategory category)
    {
        var idList = ids?.Distinct().ToArray() ?? Array.Empty<int>();
        if (idList.Length == 0)
            return 0;

        using var scope = _scopeProvider.CreateScope();
        var args = new List<object> { category.ToString() };
        var placeholders = string.Join(",", idList.Select((_, i) => $"@{i + args.Count}"));
        args.AddRange(idList.Cast<object>());
        var sql = $"UPDATE {MissedRequest.TableName} SET Category = @0 WHERE Id IN ({placeholders})";
        var rowsAffected = scope.Database.Execute(sql, args.ToArray());
        scope.Complete();
        return rowsAffected;
    }
```

Add `using System.Linq;` at the top of the file if not already implicitly available (the project has `<ImplicitUsings>enable</ImplicitUsings>`, so this is not needed).

- [ ] **Step 8: Run the service tests again to verify they pass**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter MissedRequestServiceTests`
Expected: PASS.

- [ ] **Step 9: Wire auto-classification into ingest**

In `Services/MissedRequestFlushService.cs`, the `UpsertOne` method's `INSERT` (currently lines 112-115) only fires for genuinely new paths (the `UPDATE` above it returned 0 rows) — this is exactly the "newly ingested" point the spec calls for. Update it:

```csharp
            try
            {
                var category = MissedRequestClassifier.Classify(truncatedPath);
                db.Execute(
                    $@"INSERT INTO {MissedRequest.TableName} (Path, PathHash, HitCount, FirstSeenDate, LastSeenDate, Category)
                       VALUES (@0, @1, @2, @3, @4, @5)",
                    truncatedPath, pathHash, miss.Count, miss.FirstSeenUtc, miss.LastSeenUtc, category.ToString());
            }
```

This only changes the `INSERT` branch — the retry-as-`UPDATE` branch below it (lines 124-131) is unaffected since it's updating an existing row and must not touch `Category`.

- [ ] **Step 10: Build and run the full test suite**

Run: `dotnet build Umbraco.RedirectManager.csproj -f net10.0 && dotnet test Umbraco.RedirectManager.Tests`
Expected: `Build succeeded.`, all tests pass.

- [ ] **Step 11: Commit**

```bash
git add Services/MissedRequestService.cs Services/MissedRequestFlushService.cs Umbraco.RedirectManager.Tests/Services/MissedRequestServiceTests.cs Umbraco.RedirectManager.Tests/Services/MissedRequestClassifierTests.cs
git commit -m "feat: add category assignment and auto-classification to MissedRequestService"
```

---

### Task 4: Controller endpoints

**Files:**
- Modify: `Controllers/RedirectApiController.cs`
- Create: `Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerMissedCategoryTests.cs`

- [ ] **Step 1: Write the failing controller tests**

Look at the existing constructor call pattern in `Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerTests.cs:13-24` before writing this — reuse the same substituted-dependency setup. Create `Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerMissedCategoryTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Umbraco.Cms.Core.Security;
using Umbraco.RedirectManager.Controllers;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Controllers;

public class RedirectApiControllerMissedCategoryTests
{
    private readonly IMissedRequestService _missedRequestService = Substitute.For<IMissedRequestService>();
    private readonly RedirectApiController _controller;

    public RedirectApiControllerMissedCategoryTests()
    {
        _controller = new RedirectApiController(
            Substitute.For<IRedirectService>(),
            _missedRequestService,
            Substitute.For<IRedirectTelemetryPinger>(),
            Substitute.For<IRedirectTelemetrySettingsStore>(),
            Substitute.For<IRedirectVersionChecker>(),
            Substitute.For<IBackOfficeSecurityAccessor>());
    }

    [Fact]
    public void SetMissedCategory_returns_ok_when_row_found()
    {
        _missedRequestService.SetCategory(5, MissedRequestCategory.Gone).Returns(true);

        var result = _controller.SetMissedCategory(5, new RedirectApiController.SetCategoryDto { Category = "Gone" });

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public void SetMissedCategory_returns_not_found_when_row_missing()
    {
        _missedRequestService.SetCategory(5, MissedRequestCategory.Gone).Returns(false);

        var result = _controller.SetMissedCategory(5, new RedirectApiController.SetCategoryDto { Category = "Gone" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void SetMissedCategory_returns_bad_request_for_invalid_category()
    {
        var result = _controller.SetMissedCategory(5, new RedirectApiController.SetCategoryDto { Category = "NotARealCategory" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void BulkSetMissedCategory_returns_updated_count()
    {
        _missedRequestService.BulkSetCategory(Arg.Any<IEnumerable<int>>(), MissedRequestCategory.MaliciousScanner).Returns(3);

        var result = _controller.BulkSetMissedCategory(new RedirectApiController.BulkCategoryDto
        {
            Ids = new List<int> { 1, 2, 3 },
            Category = "MaliciousScanner"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, ((dynamic)ok.Value!).updated);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter RedirectApiControllerMissedCategoryTests`
Expected: FAIL — `SetMissedCategory`, `BulkSetMissedCategory`, `SetCategoryDto`, `BulkCategoryDto` don't exist.

- [ ] **Step 3: Add the endpoints**

In `Controllers/RedirectApiController.cs`, replace the existing `DismissMissed` block (currently lines 239-247):

```csharp
    [HttpDelete("missed/{id:int}")]
    public IActionResult DismissMissed(int id)
    {
        var result = _missedRequestService.Delete(id);
        if (!result)
            return NotFound();

        return Ok();
    }
```

with (keeping `DismissMissed` — it's still a valid capability, just no longer called by either dashboard — and adding the two new endpoints after it):

```csharp
    [HttpDelete("missed/{id:int}")]
    public IActionResult DismissMissed(int id)
    {
        var result = _missedRequestService.Delete(id);
        if (!result)
            return NotFound();

        return Ok();
    }

    [HttpPut("missed/{id:int}/category")]
    public IActionResult SetMissedCategory(int id, [FromBody] SetCategoryDto dto)
    {
        if (!Enum.TryParse<MissedRequestCategory>(dto.Category, out var category))
            return BadRequest("Invalid category");

        var result = _missedRequestService.SetCategory(id, category);
        if (!result)
            return NotFound();

        return Ok();
    }

    [HttpPost("missed/bulk-category")]
    public IActionResult BulkSetMissedCategory([FromBody] BulkCategoryDto dto)
    {
        if (!Enum.TryParse<MissedRequestCategory>(dto.Category, out var category))
            return BadRequest("Invalid category");

        var updated = _missedRequestService.BulkSetCategory(dto.Ids, category);
        return Ok(new { updated });
    }
```

- [ ] **Step 4: Add the two request DTOs**

In `Controllers/RedirectApiController.cs`, next to the existing `BulkIdsDto` (currently lines 565-568), add:

```csharp
    public class SetCategoryDto
    {
        public string Category { get; set; } = string.Empty;
    }

    public class BulkCategoryDto
    {
        public List<int> Ids { get; set; } = new();
        public string Category { get; set; } = string.Empty;
    }
```

- [ ] **Step 5: Run the controller tests to verify they pass**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter RedirectApiControllerMissedCategoryTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test Umbraco.RedirectManager.Tests`
Expected: all tests pass, no regressions in the existing `RedirectApiControllerTests`.

- [ ] **Step 7: Commit**

```bash
git add Controllers/RedirectApiController.cs Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerMissedCategoryTests.cs
git commit -m "feat: add single and bulk 404 category endpoints"
```

---

### Task 5: Lit dashboard (Umbraco 17/18) — filter chips, per-row category, bulk-apply

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add category constants**

Near the top of the file, right after the `import` statements (line 3), add:

```javascript
const MISSED_CATEGORIES = [
    { value: 'Unclassified', label: 'Unclassified' },
    { value: 'MaliciousScanner', label: 'Malicious / scanner' },
    { value: 'MissingAsset', label: 'Missing asset' },
    { value: 'RedirectNeeded', label: 'Redirect needed' },
    { value: 'Gone', label: 'Gone' },
    { value: 'TypoMalformed', label: 'Typo / malformed' },
    { value: 'NeedsInvestigation', label: 'Needs investigation' }
];
```

- [ ] **Step 2: Add reactive state properties**

In the `static properties = { ... }` block (lines 6-37), add before the closing `};`:

```javascript
        missedCategoryFilter: { type: Array },
        selectedMissedIds: { type: Array }
```

- [ ] **Step 3: Initialize the new state in the constructor**

In `constructor()` (lines 736-766), add after `this.missedLoading = false;` (line 754):

```javascript
        this.missedCategoryFilter = [];
        this.selectedMissedIds = [];
```

- [ ] **Step 4: Extend the filter getter and add counts/selection getters**

Replace the existing `filteredMissedRequests` getter (lines 854-858):

```javascript
    get filteredMissedRequests() {
        const q = this.query.trim().toLowerCase();
        let rows = this.missedRequests;
        if (q) {
            rows = rows.filter(item => (item.path || '').toLowerCase().includes(q));
        }
        if (this.missedCategoryFilter.length > 0) {
            rows = rows.filter(item => this.missedCategoryFilter.includes(item.category));
        }
        return rows;
    }

    get missedCategoryCounts() {
        const counts = {};
        for (const cat of MISSED_CATEGORIES) counts[cat.value] = 0;
        for (const item of this.missedRequests) {
            counts[item.category] = (counts[item.category] || 0) + 1;
        }
        return counts;
    }

    get anyMissedSelected() {
        return this.selectedMissedIds.length > 0;
    }

    get allMissedSelected() {
        const rows = this.sortedMissedRequests;
        return rows.length > 0 && rows.every(r => this.selectedMissedIds.includes(r.id));
    }
```

- [ ] **Step 5: Add filter-toggle, selection, and category-apply methods**

After the existing `createRedirectFromMissed(item)` method (lines 1072-1074), add:

```javascript
    toggleMissedCategoryFilter(value) {
        if (this.missedCategoryFilter.includes(value)) {
            this.missedCategoryFilter = this.missedCategoryFilter.filter(v => v !== value);
        } else {
            this.missedCategoryFilter = [...this.missedCategoryFilter, value];
        }
    }

    toggleSelectAllMissed(e) {
        const checked = e.target.checked;
        const rows = this.sortedMissedRequests;
        if (checked) {
            const ids = new Set(this.selectedMissedIds);
            rows.forEach(r => ids.add(r.id));
            this.selectedMissedIds = [...ids];
        } else {
            const rowIds = new Set(rows.map(r => r.id));
            this.selectedMissedIds = this.selectedMissedIds.filter(id => !rowIds.has(id));
        }
    }

    toggleSelectMissedId(id, checked) {
        if (checked) {
            if (!this.selectedMissedIds.includes(id)) {
                this.selectedMissedIds = [...this.selectedMissedIds, id];
            }
        } else {
            this.selectedMissedIds = this.selectedMissedIds.filter(x => x !== id);
        }
    }

    async setMissedCategory(item, category) {
        try {
            const response = await this.authFetch(`/umbraco/api/redirectmanager/missed/${item.id}/category`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ category })
            });
            if (response.ok) {
                this.missedRequests = this.missedRequests.map(m => m.id === item.id ? { ...m, category } : m);
            } else {
                this.showMessage('Failed to update category', 'error');
            }
        } catch (error) {
            console.error('Failed to set category:', error);
            this.showMessage('Failed to update category', 'error');
        }
    }

    async bulkApplyMissedCategory(category) {
        if (!this.anyMissedSelected) return;

        try {
            const response = await this.authFetch('/umbraco/api/redirectmanager/missed/bulk-category', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ids: this.selectedMissedIds, category })
            });
            if (response.ok) {
                const selected = new Set(this.selectedMissedIds);
                this.missedRequests = this.missedRequests.map(m => selected.has(m.id) ? { ...m, category } : m);
                this.selectedMissedIds = [];
                this.showMessage('Selected entries updated', 'success');
            } else {
                const error = await response.text();
                this.showMessage(error || 'Failed to update selected entries', 'error');
            }
        } catch (error) {
            console.error('Failed to bulk apply category:', error);
            this.showMessage('Failed to update selected entries', 'error');
        }
    }
```

- [ ] **Step 6: Remove `dismissMissedRequest`**

Delete the `dismissMissedRequest(item)` method (lines 1058-1070) — it's superseded by `setMissedCategory`.

- [ ] **Step 7: Add the filter-chip row and bulk-apply bar to the render, inside the "404 log" tab**

In the `render()` method, the missed-tab block currently starts at `${this.activeTab === 'missed' ? html\`` (line 1756). Insert the chip row and bulk-apply bar immediately after that line, before the `${this.missedLoading ? html\`` check:

```javascript
            ${this.activeTab === 'missed' ? html`
                <div class="category-chip-row">
                    ${MISSED_CATEGORIES.map(cat => html`
                        <button
                            class="category-chip ${this.missedCategoryFilter.includes(cat.value) ? 'active' : ''}"
                            @click=${() => this.toggleMissedCategoryFilter(cat.value)}>
                            ${cat.label}
                            <span class="chip-count">${this.missedCategoryCounts[cat.value] || 0}</span>
                        </button>
                    `)}
                </div>

                ${this.anyMissedSelected ? html`
                    <div class="bulk-bar">
                        <strong>${this.selectedMissedIds.length} selected</strong>
                        <select @change=${(e) => { if (e.target.value) { this.bulkApplyMissedCategory(e.target.value); e.target.value = ''; } }}>
                            <option value="">Apply category...</option>
                            ${MISSED_CATEGORIES.map(cat => html`<option value="${cat.value}">${cat.label}</option>`)}
                        </select>
                    </div>
                ` : ''}

                ${this.missedLoading ? html`
```

- [ ] **Step 8: Add the checkbox column and per-row category select to the table**

Replace the missed-table `<thead>` (currently lines 1764-1779):

```javascript
                            <thead>
                                <tr>
                                    <th><input type="checkbox" .checked=${this.allMissedSelected} @change=${this.toggleSelectAllMissed} /></th>
                                    <th class="sortable" @click=${() => this.onSortClick('missedSort', 'path', 'string')}>
                                        Path<span class="sort-indicator">${this.sortIndicator('missedSort', 'path')}</span>
                                    </th>
                                    <th class="center sortable" @click=${() => this.onSortClick('missedSort', 'hitCount', 'number')}>
                                        Hits<span class="sort-indicator">${this.sortIndicator('missedSort', 'hitCount')}</span>
                                    </th>
                                    <th class="center sortable" @click=${() => this.onSortClick('missedSort', 'firstSeenDate', 'date')}>
                                        First seen<span class="sort-indicator">${this.sortIndicator('missedSort', 'firstSeenDate')}</span>
                                    </th>
                                    <th class="center sortable" @click=${() => this.onSortClick('missedSort', 'lastSeenDate', 'date')}>
                                        Last seen<span class="sort-indicator">${this.sortIndicator('missedSort', 'lastSeenDate')}</span>
                                    </th>
                                    <th>Category</th>
                                    <th></th>
                                </tr>
                            </thead>
```

Replace the row body (currently lines 1781-1820, the `<tbody>...</tbody>` for the missed table) — add a checkbox cell, a category `<select>` cell, and drop the "Dismiss" button:

```javascript
                            <tbody>
                                ${this.sortedMissedRequests.map(item => {
                                    const existing = this.existingRedirectFor(item);
                                    return html`
                                    <tr>
                                        <td><input type="checkbox" .checked=${this.selectedMissedIds.includes(item.id)} @change=${(e) => this.toggleSelectMissedId(item.id, e.target.checked)} /></td>
                                        <td class="url-cell" title="${this.getMissedRequestTitle(item)}">
                                            <span class="url-val">${item.path}</span>
                                            ${existing ? html`
                                                <span class="schedule-badge scheduled" title="A redirect to ${existing.newUrl || '—'} already exists for this path">
                                                    Redirect exists
                                                </span>
                                            ` : ''}
                                        </td>
                                        <td class="center">
                                            <span class="hit-count ${item.hitCount > 0 ? 'has-hits' : ''}">${item.hitCount}</span>
                                        </td>
                                        <td class="center" style="font-size:11px;color:#888;">
                                            ${new Date(item.firstSeenDate).toLocaleDateString()}
                                        </td>
                                        <td class="center" style="font-size:11px;color:#888;">
                                            ${new Date(item.lastSeenDate).toLocaleDateString()}
                                        </td>
                                        <td>
                                            <select .value=${item.category} @change=${(e) => this.setMissedCategory(item, e.target.value)}>
                                                ${MISSED_CATEGORIES.map(cat => html`<option value="${cat.value}">${cat.label}</option>`)}
                                            </select>
                                        </td>
                                        <td>
                                            <div class="act-group">
                                                ${existing ? html`
                                                    <button class="btn btn-sm btn-info" @click=${() => this.openEditModal(existing)}>
                                                        Edit redirect
                                                    </button>
                                                ` : html`
                                                    <button class="btn btn-sm btn-success-sm" @click=${() => this.createRedirectFromMissed(item)}>
                                                        Create redirect
                                                    </button>
                                                `}
                                            </div>
                                        </td>
                                    </tr>`;
                                })}
                            </tbody>
```

- [ ] **Step 9: Add CSS for the chip row**

In `static styles = css\`...\``, near the existing `/* ── tab-count ── */` rule block (after `.tab-count.danger` at line 337), add:

```css
        /* ── category chips ── */
        .category-chip-row {
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
            margin-bottom: 10px;
        }

        .category-chip {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            padding: 5px 10px;
            border-radius: 14px;
            border: 1px solid #e0e0e0;
            background: #fff;
            font-size: 12px;
            color: #555;
            cursor: pointer;
        }

        .category-chip.active {
            background: #eef0fb;
            border-color: #3544b1;
            color: #3544b1;
            font-weight: 600;
        }

        .category-chip .chip-count {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 16px;
            height: 16px;
            padding: 0 4px;
            border-radius: 8px;
            background: rgba(0,0,0,0.08);
            font-size: 10px;
            font-weight: 600;
        }

        .category-chip.active .chip-count {
            background: #3544b1;
            color: #fff;
        }
```

- [ ] **Step 10: Manual verification**

Run: `dotnet build Umbraco.RedirectManager.csproj -f net10.0`
Expected: `Build succeeded.` (this build doesn't type-check the JS, but confirms nothing else broke; the JS is verified by loading the dashboard per Task 7's manual check).

- [ ] **Step 11: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "feat: add category filter chips, per-row category, and bulk-apply to Lit dashboard"
```

---

### Task 6: AngularJS dashboard (Umbraco 13) — same behavior

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect.resource.js`
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`
- Modify: `App_Plugins/RedirectManager/dashboard.html`

- [ ] **Step 1: Add resource methods**

In `App_Plugins/RedirectManager/redirect.resource.js`, replace the `dismissMissed` entry (lines 44-46):

```javascript
            dismissMissed: function (id) {
                return $http.delete(baseUrl + "missed/" + id);
            },
            setMissedCategory: function (id, category) {
                return $http.put(baseUrl + "missed/" + id + "/category", { category: category });
            },
            bulkSetMissedCategory: function (ids, category) {
                return $http.post(baseUrl + "missed/bulk-category", { ids: ids, category: category });
            },
```

(keep `dismissMissed` itself — same rationale as the backend endpoint: unused by the UI now, but not removed as a capability.)

- [ ] **Step 2: Add category constants and state**

In `App_Plugins/RedirectManager/redirect.controller.js`, after the `"use strict";` line (line 2), add:

```javascript
    var MISSED_CATEGORIES = [
        { value: 'Unclassified', label: 'Unclassified' },
        { value: 'MaliciousScanner', label: 'Malicious / scanner' },
        { value: 'MissingAsset', label: 'Missing asset' },
        { value: 'RedirectNeeded', label: 'Redirect needed' },
        { value: 'Gone', label: 'Gone' },
        { value: 'TypoMalformed', label: 'Typo / malformed' },
        { value: 'NeedsInvestigation', label: 'Needs investigation' }
    ];
```

In the `DashboardController` body, after `vm.missedLoading = false;` (line 14), add:

```javascript
        vm.missedCategories = MISSED_CATEGORIES;
        vm.missedCategoryFilter = [];
        vm.selectedMissedIds = [];
```

- [ ] **Step 3: Replace `dismissMissedRequest` with category methods**

Replace `vm.dismissMissedRequest` (lines 55-61):

```javascript
        vm.toggleMissedCategoryFilter = function (value) {
            var idx = vm.missedCategoryFilter.indexOf(value);
            if (idx === -1) {
                vm.missedCategoryFilter.push(value);
            } else {
                vm.missedCategoryFilter.splice(idx, 1);
            }
        };

        vm.missedCategoryCounts = function () {
            var counts = {};
            MISSED_CATEGORIES.forEach(function (c) { counts[c.value] = 0; });
            vm.missedRequests.forEach(function (item) {
                counts[item.category] = (counts[item.category] || 0) + 1;
            });
            return counts;
        };

        vm.filteredMissedRequests = function () {
            if (vm.missedCategoryFilter.length === 0) {
                return vm.missedRequests;
            }
            return vm.missedRequests.filter(function (item) {
                return vm.missedCategoryFilter.indexOf(item.category) !== -1;
            });
        };

        vm.toggleSelectAllMissed = function (checked) {
            var rows = vm.sortedMissedRequests();
            if (checked) {
                rows.forEach(function (r) {
                    if (vm.selectedMissedIds.indexOf(r.id) === -1) {
                        vm.selectedMissedIds.push(r.id);
                    }
                });
            } else {
                var rowIds = rows.map(function (r) { return r.id; });
                vm.selectedMissedIds = vm.selectedMissedIds.filter(function (id) {
                    return rowIds.indexOf(id) === -1;
                });
            }
        };

        vm.toggleSelectMissedId = function (id, checked) {
            var idx = vm.selectedMissedIds.indexOf(id);
            if (checked && idx === -1) {
                vm.selectedMissedIds.push(id);
            } else if (!checked && idx !== -1) {
                vm.selectedMissedIds.splice(idx, 1);
            }
        };

        vm.setMissedCategory = function (item, category) {
            redirectResource.setMissedCategory(item.id, category).then(function () {
                item.category = category;
            }, function () {
                notificationsService.error("Error", "Failed to update category");
            });
        };

        vm.bulkApplyMissedCategory = function (category) {
            if (vm.selectedMissedIds.length === 0 || !category) return;
            redirectResource.bulkSetMissedCategory(vm.selectedMissedIds, category).then(function () {
                var selected = vm.selectedMissedIds;
                vm.missedRequests.forEach(function (item) {
                    if (selected.indexOf(item.id) !== -1) {
                        item.category = category;
                    }
                });
                vm.selectedMissedIds = [];
            }, function () {
                notificationsService.error("Error", "Failed to update selected entries");
            });
        };
```

- [ ] **Step 4: Update `sortedMissedRequests` to filter by category**

Replace `vm.sortedMissedRequests` (currently lines 152-155):

```javascript
        vm.sortedMissedRequests = function () {
            var s = vm.missedSort;
            var rows = vm.filteredMissedRequests();
            return s.column ? vm.sortRows(rows, s.column, s.direction, s.type) : rows;
        };
```

- [ ] **Step 5: Update the template — chip row, bulk-apply bar, checkbox + category column**

In `App_Plugins/RedirectManager/dashboard.html`, immediately before the existing `<table ng-if="!vm.missedLoading...` line (line 255), add:

```html
                <div class="category-chip-row">
                    <button type="button"
                            ng-repeat="cat in vm.missedCategories"
                            class="category-chip"
                            ng-class="{active: vm.missedCategoryFilter.indexOf(cat.value) !== -1}"
                            ng-click="vm.toggleMissedCategoryFilter(cat.value)">
                        {{cat.label}}
                        <span class="chip-count">{{vm.missedCategoryCounts()[cat.value] || 0}}</span>
                    </button>
                </div>

                <div class="bulk-bar" ng-if="vm.selectedMissedIds.length > 0">
                    <strong>{{vm.selectedMissedIds.length}} selected</strong>
                    <select ng-model="vm.missedBulkCategory"
                            ng-options="cat.value as cat.label for cat in vm.missedCategories"
                            ng-change="vm.bulkApplyMissedCategory(vm.missedBulkCategory); vm.missedBulkCategory = null;">
                        <option value="">Apply category...</option>
                    </select>
                </div>

```

Replace the `<thead>` (lines 256-271):

```html
                    <thead>
                        <tr>
                            <th>
                                <input type="checkbox"
                                       ng-checked="vm.selectedMissedIds.length > 0 && vm.selectedMissedIds.length === vm.sortedMissedRequests().length"
                                       ng-click="vm.toggleSelectAllMissed($event.target.checked)" />
                            </th>
                            <th class="sortable" ng-click="vm.sortBy('missedSort', 'path', 'string')">
                                Path<span class="sort-indicator">{{vm.sortIndicator('missedSort', 'path')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('missedSort', 'hitCount', 'number')">
                                Hits<span class="sort-indicator">{{vm.sortIndicator('missedSort', 'hitCount')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('missedSort', 'firstSeenDate', 'date')">
                                First seen<span class="sort-indicator">{{vm.sortIndicator('missedSort', 'firstSeenDate')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('missedSort', 'lastSeenDate', 'date')">
                                Last seen<span class="sort-indicator">{{vm.sortIndicator('missedSort', 'lastSeenDate')}}</span>
                            </th>
                            <th>Category</th>
                            <th></th>
                        </tr>
                    </thead>
```

Replace the `<tbody>` (lines 273-300):

```html
                    <tbody>
                        <tr ng-repeat="item in vm.sortedMissedRequests()">
                            <td>
                                <input type="checkbox"
                                       ng-checked="vm.selectedMissedIds.indexOf(item.id) !== -1"
                                       ng-click="vm.toggleSelectMissedId(item.id, $event.target.checked)" />
                            </td>
                            <td class="redirect-url">{{item.path}}</td>
                            <td style="text-align:center;">
                                <span class="hit-count live">{{item.hitCount}}</span>
                            </td>
                            <td style="text-align:center;font-size:11px;color:#888;">
                                {{item.firstSeenDate | date:'mediumDate'}}
                            </td>
                            <td style="text-align:center;font-size:11px;color:#888;">
                                {{item.lastSeenDate | date:'mediumDate'}}
                            </td>
                            <td>
                                <select ng-model="item.category"
                                        ng-options="cat.value as cat.label for cat in vm.missedCategories"
                                        ng-change="vm.setMissedCategory(item, item.category)">
                                </select>
                            </td>
                            <td class="redirect-actions">
                                <umb-button type="button"
                                            button-style="success"
                                            size="xs"
                                            label="Create redirect"
                                            action="vm.createRedirectFromMissed(item)">
                                </umb-button>
                            </td>
                        </tr>
                    </tbody>
```

- [ ] **Step 6: Add matching CSS**

Find the AngularJS stylesheet (`App_Plugins/RedirectManager/redirect.css`) and check for an existing `.bulk-bar` rule (added by the earlier table-UX-improvements work per `docs/superpowers/specs/2026-07-22-table-ux-improvements-design.md`). If present, add a `.category-chip-row` / `.category-chip` block matching the CSS from Task 5 Step 9 (same class names, so the same rules can be reused verbatim) into `redirect.css`. If `.bulk-bar` doesn't already exist in this file, add it too, matching the Lit version's styling from `redirect-dashboard.js:275-291`.

- [ ] **Step 7: Manual verification**

Run: `dotnet build Umbraco.RedirectManager.csproj -f net8.0`
Expected: `Build succeeded.` (JS/HTML aren't compiled by this build; verified by loading the dashboard per Task 7).

- [ ] **Step 8: Commit**

```bash
git add App_Plugins/RedirectManager/redirect.resource.js App_Plugins/RedirectManager/redirect.controller.js App_Plugins/RedirectManager/dashboard.html App_Plugins/RedirectManager/redirect.css
git commit -m "feat: add category filter chips, per-row category, and bulk-apply to AngularJS dashboard"
```

---

### Task 7: Full build/test verification + version bump

**Files:**
- Modify: `Umbraco.RedirectManager.csproj`
- Modify: `App_Plugins/RedirectManager/umbraco-package.json`
- Modify: `README.md`

- [ ] **Step 1: Full solution build, both target frameworks**

Run: `dotnet build Umbraco.RedirectManager.csproj -f net8.0 && dotnet build Umbraco.RedirectManager.csproj -f net10.0`
Expected: `Build succeeded.` for both, zero warnings introduced by this feature's new files.

- [ ] **Step 2: Full test suite**

Run: `dotnet test Umbraco.RedirectManager.Tests`
Expected: all tests pass (existing suite + the new classifier/service/controller tests from Tasks 3–4).

- [ ] **Step 3: Bump the version — csproj**

In `Umbraco.RedirectManager.csproj`, change:

```xml
    <Version>1.9.1</Version>
```

to:

```xml
    <Version>1.10.0</Version>
```

- [ ] **Step 4: Bump the version — umbraco-package.json**

In `App_Plugins/RedirectManager/umbraco-package.json`, update the `"version"` field from `"1.9.1"` to `"1.10.0"`, and update the `?v=1.9.1` cache-busting query string on the dashboard element URL to `?v=1.10.0` (per [[reference_redirectmanager_publish_flow]] — both must match exactly, this is a two-file convention, not just the csproj).

- [ ] **Step 5: Update README Features**

In `README.md`, update the existing 404-log bullet (line 27) to mention categorization. Replace:

```
- **404 log with one-click redirect creation**: Genuine 404s are logged automatically with hit count, first seen, and last seen dates. Turn any frequent 404 into a redirect in a single click — the 404 row disappears from the log as soon as its redirect is created. If a redirect already covers a logged path, the row is flagged with a "Redirect exists" badge and offers an Edit shortcut instead of a duplicate Create action. The log's search box also filters its own rows now, matching the Redirects tab.
```

with:

```
- **404 log with one-click redirect creation**: Genuine 404s are logged automatically with hit count, first seen, and last seen dates. Turn any frequent 404 into a redirect in a single click — the 404 row disappears from the log as soon as its redirect is created. If a redirect already covers a logged path, the row is flagged with a "Redirect exists" badge and offers an Edit shortcut instead of a duplicate Create action. The log's search box also filters its own rows now, matching the Redirects tab.
- **404 triage categories**: Tag each 404 as Malicious/scanner, Missing asset, Redirect needed, Gone, Typo/malformed, Needs investigation, or Unclassified, with filter chips (showing live counts) and bulk-apply to a filtered selection. Obvious scanner probes (`.php`, `/wp-*`, `/.env`, `/.git/`) and static-asset extensions are auto-tagged as soon as they're logged, so manual triage only touches the ambiguous rows. Replaces the old all-or-nothing "Dismiss" (which deleted the row); a category is now remembered instead.
```

- [ ] **Step 6: Commit**

```bash
git add Umbraco.RedirectManager.csproj App_Plugins/RedirectManager/umbraco-package.json README.md
git commit -m "chore: bump version to 1.10.0, document 404 triage categories in README"
```

- [ ] **Step 7: Manual dashboard verification (both UIs)**

This package requires a running Umbraco site to load either dashboard — there's no existing local dev-server/preview setup in this repo for it. Verification here means: read through the final diffs for `redirect-dashboard.js` and `dashboard.html`/`redirect.controller.js` once more end-to-end (not just the hunks touched in Tasks 5–6) checking for unclosed template literals/tags, mismatched `MISSED_CATEGORIES` references, and that no leftover reference to the removed `dismissMissedRequest`/`vm.dismissMissedRequest` remains anywhere in either file (`grep -rn "dismissMissedRequest" App_Plugins/RedirectManager/`). This matches how the prior `2026-07-22-table-ux-improvements-design.md` dashboard changes were verified (no automated UI test harness exists in this repo).

---

## Post-implementation (not part of this plan's tasks — separate, explicit step)

Per [[project_roadmap_batch_release_goal]] and [[reference_redirectmanager_publish_flow]]: once this plan's tasks are complete and merged to `main`, publishing (git tag + push, triggering the NuGet/Marketplace release) requires a separate, explicit confirmation from the user before the tag-push step — do not tag/push as part of finishing this plan.
