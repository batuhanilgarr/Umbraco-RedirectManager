# Windowed Hit Tracking (7d/30d) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface 7-day and 30-day rolling hit totals per redirect (in addition to the existing all-time `HitCount`), so an editor can spot redirects that have gone quiet (cleanup candidates) or aren't firing when they should (misconfiguration). Also fix a data-loss bug found while investigating this feature: a failed flush currently drops hits permanently instead of retrying.

**Architecture:** A new daily-bucket table (`RedirectManagerHitDaily`, one row per `(RedirectId, HitDate)`) is upserted by the existing `RedirectHitFlushService` background flush, in the same transaction as the existing all-time `HitCount` update. A new aggregate query sums buckets into 7-day/30-day totals on read. A 24-hour retention job (mirroring `MissedRequestFlushService`'s pattern) prunes buckets older than 35 days. On flush failure, the drained batch is merged back into the in-memory tracker instead of being discarded.

**Tech Stack:** ASP.NET Core (`BackgroundService`, DI), NPoco (via Umbraco's `IScopeProvider`), Umbraco CMS migrations, Lit (Umbraco 17+/18 dashboard), AngularJS (Umbraco 13 dashboard).

Reference spec: `docs/superpowers/specs/2026-07-08-windowed-hit-tracking-design.md`

---

### Task 1: Add the `RedirectHitDaily` model

**Files:**
- Create: `Models/RedirectHitDaily.cs`

- [ ] **Step 1: Write the model**

```csharp
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Umbraco.RedirectManager.Models;

[TableName(RedirectHitDaily.TableName)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class RedirectHitDaily
{
    public const string TableName = "RedirectManagerHitDaily";

    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("Id")]
    public int Id { get; set; }

    [Column("RedirectId")]
    public int RedirectId { get; set; }

    // Date-only (time component zeroed) — one row per redirect per UTC day.
    [Column("HitDate")]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_RedirectManagerHitDaily_RedirectId_HitDate", ForColumns = "RedirectId,HitDate")]
    public DateTime HitDate { get; set; }

    [Column("HitCount")]
    [Constraint(Default = 0)]
    public int HitCount { get; set; } = 0;
}
```

The `[Index]` attribute is placed on `HitDate` with `ForColumns = "RedirectId,HitDate"` to
declare the composite unique index across both columns — this is the same
`ForColumns` mechanism Umbraco's own core models use for composite indexes
(confirmed by inspecting `Umbraco.Infrastructure.dll`).

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Models/RedirectHitDaily.cs
git commit -m "$(cat <<'EOF'
feat: add RedirectHitDaily model for windowed hit tracking

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add the `CreateRedirectHitDailyTable` migration step

**Files:**
- Modify: `Migrations/RedirectManagerMigrationPlan.cs`

- [ ] **Step 1: Register the new migration step in `DefinePlan()`**

Current (lines 13-20):

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

Replace with:

```csharp
    protected override void DefinePlan()
    {
        To<CreateRedirectManagerTable>(new Guid("C1686EA6-A8CF-4B7E-B91F-D4519EB17FDA"));
        To<AddIsRegexAndDescriptionColumns>(new Guid("EE2670E3-75C8-4BF6-8D70-36B10D5ECC65"));
        To<AddHitCountColumns>(new Guid("4F2A8B31-6C7C-4A8E-9E22-2D4D6D9CDDF1"));
        To<CreateMissedRequestsTable>(new Guid("7A1E9C42-3B5D-4F6A-8E11-9C2D5A7B3F04"));
        To<AddDomainColumn>(new Guid("B8D4E617-2F0A-4C9B-A5D3-6E1F8C0A9B72"));
        To<CreateRedirectHitDailyTable>(new Guid("1D9F4E23-6A8B-4C1D-9E7A-3B5C8D2F4A61"));
    }
```

- [ ] **Step 2: Add the model's `using` to the top of the file**

Current (lines 1-3):

```csharp
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.RedirectManager.Models;
```

No change needed here — `Umbraco.RedirectManager.Models` is already imported
and covers the new `RedirectHitDaily` type too.

- [ ] **Step 3: Add the async (net10.0+) migration class**

In the `#if NET10_0_OR_GREATER` block, immediately after the closing brace of
`AddDomainColumn` (the class ending right before `#else`), insert:

```csharp
public class CreateRedirectHitDailyTable : AsyncMigrationBase
{
    public CreateRedirectHitDailyTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectHitDaily.TableName) == false)
        {
            Create.Table<RedirectHitDaily>().Do();
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Add the sync (net8.0) migration class**

In the `#else` block, immediately after the closing brace of the sync
`AddDomainColumn` class (right before `#endif`), insert:

```csharp
public class CreateRedirectHitDailyTable : MigrationBase
{
    public CreateRedirectHitDailyTable(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectHitDaily.TableName) == false)
        {
            Create.Table<RedirectHitDaily>().Do();
        }
    }
}
```

- [ ] **Step 5: Build to confirm both TFMs compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 6: Commit**

```bash
git add Migrations/RedirectManagerMigrationPlan.cs
git commit -m "$(cat <<'EOF'
feat: add migration for RedirectManagerHitDaily table

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Add `MergeBack` to `IRedirectHitTracker`

**Files:**
- Modify: `Services/RedirectHitTracker.cs`

This is the fix for the data-loss bug found during investigation: today, a
failed flush discards the drained batch. `MergeBack` lets the flush service
put a failed batch back into the live tracker so the next successful flush
picks it up.

- [ ] **Step 1: Add the method to the interface**

Current (lines 6-10):

```csharp
public interface IRedirectHitTracker
{
    void RecordHit(int redirectId);
    IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll();
}
```

Replace with:

```csharp
public interface IRedirectHitTracker
{
    void RecordHit(int redirectId);
    IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll();
    void MergeBack(IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> data);
}
```

- [ ] **Step 2: Implement it on `RedirectHitTracker`**

Current (lines 16-26), the existing `RecordHit` method:

```csharp
    public void RecordHit(int redirectId)
    {
        _accumulators.AddOrUpdate(
            redirectId,
            _ => new HitAccumulator(1, DateTime.UtcNow),
            (_, existing) =>
            {
                existing.Increment();
                return existing;
            });
    }
```

Add immediately after it:

```csharp
    public void MergeBack(IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> data)
    {
        foreach (var (redirectId, hit) in data)
        {
            _accumulators.AddOrUpdate(
                redirectId,
                _ => new HitAccumulator(hit.Count, hit.LastHitUtc),
                (_, existing) =>
                {
                    existing.Add(hit.Count, hit.LastHitUtc);
                    return existing;
                });
        }
    }
```

- [ ] **Step 3: Add the `Add` method to `HitAccumulator`**

Current (lines 45-64), the `HitAccumulator` class's `Increment` method:

```csharp
        public void Increment()
        {
            Interlocked.Increment(ref _count);
            Interlocked.Exchange(ref _lastHitUtcTicks, DateTime.UtcNow.Ticks);
        }
    }
}
```

Replace with:

```csharp
        public void Increment()
        {
            Interlocked.Increment(ref _count);
            Interlocked.Exchange(ref _lastHitUtcTicks, DateTime.UtcNow.Ticks);
        }

        public void Add(int count, DateTime lastHitUtc)
        {
            Interlocked.Add(ref _count, count);

            // Only advance LastHitUtc, never regress it — a merge-back of an
            // older failed batch shouldn't overwrite a newer hit that was
            // recorded in the meantime.
            var newTicks = lastHitUtc.Ticks;
            var current = Interlocked.Read(ref _lastHitUtcTicks);
            while (newTicks > current)
            {
                var original = Interlocked.CompareExchange(ref _lastHitUtcTicks, newTicks, current);
                if (original == current)
                {
                    break;
                }

                current = original;
            }
        }
    }
}
```

- [ ] **Step 4: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 5: Commit**

```bash
git add Services/RedirectHitTracker.cs
git commit -m "$(cat <<'EOF'
fix: merge failed hit-flush batches back into the tracker instead of dropping them

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Extend `RedirectHitFlushService` — daily buckets, retention, and merge-back

**Files:**
- Modify: `Services/RedirectHitFlushService.cs`

- [ ] **Step 1: Add retention/cleanup fields**

Current (lines 1-20):

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectHitFlushService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    private readonly IRedirectHitTracker _hitTracker;
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<RedirectHitFlushService> _logger;

    public RedirectHitFlushService(
        IRedirectHitTracker hitTracker,
        IScopeProvider scopeProvider,
        ILogger<RedirectHitFlushService> logger)
    {
        _hitTracker = hitTracker;
        _scopeProvider = scopeProvider;
        _logger = logger;
    }
```

Replace with:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectHitFlushService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan HitDailyRetentionPeriod = TimeSpan.FromDays(35);

    private readonly IRedirectHitTracker _hitTracker;
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<RedirectHitFlushService> _logger;
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public RedirectHitFlushService(
        IRedirectHitTracker hitTracker,
        IScopeProvider scopeProvider,
        ILogger<RedirectHitFlushService> logger)
    {
        _hitTracker = hitTracker;
        _scopeProvider = scopeProvider;
        _logger = logger;
    }
```

- [ ] **Step 2: Rewrite `Flush()` to upsert daily buckets, run retention, and merge back on failure**

Current (lines 41-70), the entire `Flush()` method:

```csharp
    private void Flush()
    {
        var drained = _hitTracker.DrainAll();
        if (drained.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = _scopeProvider.CreateScope();

            foreach (var (redirectId, hit) in drained)
            {
                scope.Database.Execute(
                    $@"UPDATE {RedirectEntry.TableName}
                       SET HitCount = HitCount + @0,
                           LastHitDate = CASE WHEN LastHitDate IS NULL OR @1 > LastHitDate THEN @1 ELSE LastHitDate END
                       WHERE Id = @2",
                    hit.Count, hit.LastHitUtc, redirectId);
            }

            scope.Complete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush redirect hit counts for {Count} redirect(s)", drained.Count);
        }
    }
```

Replace with:

```csharp
    private void Flush()
    {
        var drained = _hitTracker.DrainAll();
        var cleanupDue = DateTime.UtcNow - _lastCleanupUtc >= CleanupInterval;

        if (drained.Count == 0 && !cleanupDue)
        {
            return;
        }

        if (drained.Count > 0)
        {
            var today = DateTime.UtcNow.Date;

            try
            {
                using var scope = _scopeProvider.CreateScope();

                foreach (var (redirectId, hit) in drained)
                {
                    scope.Database.Execute(
                        $@"UPDATE {RedirectEntry.TableName}
                           SET HitCount = HitCount + @0,
                               LastHitDate = CASE WHEN LastHitDate IS NULL OR @1 > LastHitDate THEN @1 ELSE LastHitDate END
                           WHERE Id = @2",
                        hit.Count, hit.LastHitUtc, redirectId);

                    UpsertDailyBucket(scope, redirectId, hit.Count, today);
                }

                scope.Complete();
            }
            catch (Exception ex)
            {
                // Whole batch shares one transaction, so on any failure none of
                // it committed — safe to merge the entire drained snapshot back
                // into the tracker for the next flush attempt to retry.
                _hitTracker.MergeBack(drained);
                _logger.LogWarning(ex, "Failed to flush redirect hit counts for {Count} redirect(s)", drained.Count);
            }
        }

        if (cleanupDue)
        {
            try
            {
                using var scope = _scopeProvider.CreateScope();
                scope.Database.Execute(
                    $"DELETE FROM {RedirectHitDaily.TableName} WHERE HitDate < @0",
                    DateTime.UtcNow.Date - HitDailyRetentionPeriod);
                scope.Complete();
                _lastCleanupUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run redirect hit-daily retention cleanup");
            }
        }
    }

    private static void UpsertDailyBucket(IScope scope, int redirectId, int count, DateTime hitDate)
    {
        var rowsAffected = scope.Database.Execute(
            $@"UPDATE {RedirectHitDaily.TableName}
               SET HitCount = HitCount + @0
               WHERE RedirectId = @1 AND HitDate = @2",
            count, redirectId, hitDate);

        if (rowsAffected > 0)
        {
            return;
        }

        try
        {
            scope.Database.Execute(
                $@"INSERT INTO {RedirectHitDaily.TableName} (RedirectId, HitDate, HitCount)
                   VALUES (@0, @1, @2)",
                redirectId, hitDate, count);
        }
        catch (Exception)
        {
            // Another instance's flush inserted the same (RedirectId, HitDate)
            // bucket between our UPDATE and INSERT. Retry as an update now
            // that the row exists (same race-recovery pattern as
            // MissedRequestFlushService.UpsertOne).
            scope.Database.Execute(
                $@"UPDATE {RedirectHitDaily.TableName}
                   SET HitCount = HitCount + @0
                   WHERE RedirectId = @1 AND HitDate = @2",
                count, redirectId, hitDate);
        }
    }
```

- [ ] **Step 3: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit**

```bash
git add Services/RedirectHitFlushService.cs
git commit -m "$(cat <<'EOF'
feat: upsert daily hit buckets and add retention cleanup to the flush service

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Add `GetHitWindowCounts` to `IRedirectService`/`RedirectService`

**Files:**
- Modify: `Services/IRedirectService.cs`
- Modify: `Services/RedirectService.cs`

- [ ] **Step 1: Add the method to the interface**

Current (`Services/IRedirectService.cs`, full file):

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
    RedirectEntry Create(CreateRedirectEntryDto dto);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
}
```

- [ ] **Step 2: Implement it on `RedirectService`**

Current (`Services/RedirectService.cs` lines 125-137), the existing
`GetActiveRegexEntries` method (implementation goes right after it):

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

Add immediately after it:

```csharp
    public IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts()
    {
        using var scope = _scopeProvider.CreateScope();

        // "Last 7 days" / "last 30 days" both count today, so the cutoff is
        // today minus (window - 1) days.
        var cutoff7 = DateTime.UtcNow.Date.AddDays(-6);
        var cutoff30 = DateTime.UtcNow.Date.AddDays(-29);

        var rows = scope.Database.Fetch<HitWindowRow>(
            $@"SELECT RedirectId,
                      SUM(CASE WHEN HitDate >= @0 THEN HitCount ELSE 0 END) AS Last7,
                      SUM(CASE WHEN HitDate >= @1 THEN HitCount ELSE 0 END) AS Last30
               FROM {RedirectHitDaily.TableName}
               WHERE HitDate >= @1
               GROUP BY RedirectId",
            cutoff7, cutoff30);

        scope.Complete();

        return rows.ToDictionary(r => r.RedirectId, r => (r.Last7, r.Last30));
    }

    private sealed class HitWindowRow
    {
        public int RedirectId { get; set; }
        public int Last7 { get; set; }
        public int Last30 { get; set; }
    }
```

- [ ] **Step 3: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit**

```bash
git add Services/IRedirectService.cs Services/RedirectService.cs
git commit -m "$(cat <<'EOF'
feat: add GetHitWindowCounts for 7-day/30-day hit aggregation

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Wire `Hits7d`/`Hits30d` through the DTO and API

**Files:**
- Modify: `Models/RedirectEntryDto.cs`
- Modify: `Controllers/RedirectApiController.cs`

- [ ] **Step 1: Add the two fields to `RedirectEntryDto`**

Current (`Models/RedirectEntryDto.cs` lines 3-15):

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
}
```

- [ ] **Step 2: Give `ToDto` optional window-count parameters**

Current (`Controllers/RedirectApiController.cs` lines 424-439), the
`ToDto(RedirectEntry)` overload:

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
            Hits30d = hits30d
        };
    }
```

Every other call site (`Get`, `Create`, `Update`, `Test`) keeps calling
`ToDto(x)` unchanged — they get `Hits7d`/`Hits30d` defaulted to `0`, matching
the approved design (window counts are only wired into the list view).

- [ ] **Step 3: Wire window counts into `GetAll`**

Current (`Controllers/RedirectApiController.cs` lines 29-41), the entire
`GetAll` action:

```csharp
    [HttpGet("getall")]
    public IActionResult GetAll(
        [FromQuery] string? q,
        [FromQuery] int? statusCode,
        [FromQuery] bool? isActive,
        [FromQuery] bool? isRegex)
    {
        var redirects = string.IsNullOrWhiteSpace(q) && statusCode == null && isActive == null && isRegex == null
            ? _redirectService.GetAll()
            : _redirectService.GetAllFiltered(q, statusCode, isActive, isRegex);

        return Ok(redirects.Select(ToDto));
    }
```

Replace with:

```csharp
    [HttpGet("getall")]
    public IActionResult GetAll(
        [FromQuery] string? q,
        [FromQuery] int? statusCode,
        [FromQuery] bool? isActive,
        [FromQuery] bool? isRegex)
    {
        var redirects = string.IsNullOrWhiteSpace(q) && statusCode == null && isActive == null && isRegex == null
            ? _redirectService.GetAll()
            : _redirectService.GetAllFiltered(q, statusCode, isActive, isRegex);

        var windowCounts = _redirectService.GetHitWindowCounts();

        return Ok(redirects.Select(r =>
        {
            var window = windowCounts.TryGetValue(r.Id, out var w) ? w : (Last7: 0, Last30: 0);
            return ToDto(r, window.Last7, window.Last30);
        }));
    }
```

- [ ] **Step 4: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 5: Commit**

```bash
git add Models/RedirectEntryDto.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: expose Hits7d and Hits30d in the redirect list API

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Add "7d"/"30d" columns to both dashboard UIs

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js` (Lit, Umbraco 17+/18)
- Modify: `App_Plugins/RedirectManager/dashboard.html` (AngularJS, Umbraco 13)

- [ ] **Step 1: Add the two column headers to the Lit dashboard**

Current (`redirect-dashboard.js` lines 1151-1159):

```javascript
                                    <th style="width:60px;" class="center">Status</th>
                                    <th>Old URL</th>
                                    <th>New URL</th>
                                    <th>Domain</th>
                                    <th>Notes</th>
                                    <th class="center">Match</th>
                                    <th class="center">Active</th>
                                    <th class="center" title="Hit count">Hits</th>
                                    <th></th>
```

Replace with:

```javascript
                                    <th style="width:60px;" class="center">Status</th>
                                    <th>Old URL</th>
                                    <th>New URL</th>
                                    <th>Domain</th>
                                    <th>Notes</th>
                                    <th class="center">Match</th>
                                    <th class="center">Active</th>
                                    <th class="center" title="Hit count">Hits</th>
                                    <th class="center" title="Hits in the last 7 days">7d</th>
                                    <th class="center" title="Hits in the last 30 days">30d</th>
                                    <th></th>
```

- [ ] **Step 2: Add the two data cells to the Lit dashboard**

Current (`redirect-dashboard.js` lines 1208-1212):

```javascript
                                        <td class="center" title="${this.getLastHitTitle(redirect)}">
                                            <span class="hit-count ${(redirect.hitCount || 0) > 0 ? 'has-hits' : ''}">
                                                ${(redirect.hitCount || 0).toLocaleString()}
                                            </span>
                                        </td>
```

Replace with:

```javascript
                                        <td class="center" title="${this.getLastHitTitle(redirect)}">
                                            <span class="hit-count ${(redirect.hitCount || 0) > 0 ? 'has-hits' : ''}">
                                                ${(redirect.hitCount || 0).toLocaleString()}
                                            </span>
                                        </td>
                                        <td class="center">
                                            <span class="hit-count ${(redirect.hits7d || 0) > 0 ? 'has-hits' : ''}">
                                                ${(redirect.hits7d || 0).toLocaleString()}
                                            </span>
                                        </td>
                                        <td class="center">
                                            <span class="hit-count ${(redirect.hits30d || 0) > 0 ? 'has-hits' : ''}">
                                                ${(redirect.hits30d || 0).toLocaleString()}
                                            </span>
                                        </td>
```

- [ ] **Step 3: Add the two column headers to the AngularJS dashboard**

Current (`dashboard.html` lines 78-86):

```html
                            <th style="width:60px;text-align:center;">Status</th>
                            <th>Old URL</th>
                            <th>New URL</th>
                            <th>Domain</th>
                            <th>Notes</th>
                            <th style="text-align:center;">Match</th>
                            <th style="text-align:center;">Active</th>
                            <th style="text-align:center;" title="Hit count">Hits</th>
                            <th></th>
```

Replace with:

```html
                            <th style="width:60px;text-align:center;">Status</th>
                            <th>Old URL</th>
                            <th>New URL</th>
                            <th>Domain</th>
                            <th>Notes</th>
                            <th style="text-align:center;">Match</th>
                            <th style="text-align:center;">Active</th>
                            <th style="text-align:center;" title="Hit count">Hits</th>
                            <th style="text-align:center;" title="Hits in the last 7 days">7d</th>
                            <th style="text-align:center;" title="Hits in the last 30 days">30d</th>
                            <th></th>
```

- [ ] **Step 4: Add the two data cells to the AngularJS dashboard**

Current (`dashboard.html` lines 117-123):

```html
                            <td style="text-align:center;"
                                title="{{redirect.lastHitDate ? ('Last hit: ' + (redirect.lastHitDate | date:'medium')) : 'Never hit'}}">
                                <span class="hit-count"
                                      ng-class="{'live': (redirect.hitCount || 0) > 0}">
                                    {{redirect.hitCount || 0}}
                                </span>
                            </td>
```

Replace with:

```html
                            <td style="text-align:center;"
                                title="{{redirect.lastHitDate ? ('Last hit: ' + (redirect.lastHitDate | date:'medium')) : 'Never hit'}}">
                                <span class="hit-count"
                                      ng-class="{'live': (redirect.hitCount || 0) > 0}">
                                    {{redirect.hitCount || 0}}
                                </span>
                            </td>
                            <td style="text-align:center;">
                                <span class="hit-count"
                                      ng-class="{'live': (redirect.hits7d || 0) > 0}">
                                    {{redirect.hits7d || 0}}
                                </span>
                            </td>
                            <td style="text-align:center;">
                                <span class="hit-count"
                                      ng-class="{'live': (redirect.hits30d || 0) > 0}">
                                    {{redirect.hits30d || 0}}
                                </span>
                            </td>
```

- [ ] **Step 5: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js App_Plugins/RedirectManager/dashboard.html
git commit -m "$(cat <<'EOF'
feat: show 7-day and 30-day hit counts in both dashboard UIs

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Version bump and docs

**Files:**
- Modify: `Umbraco.RedirectManager.csproj`
- Modify: `README.md`

- [ ] **Step 1: Bump the version**

Current (`Umbraco.RedirectManager.csproj` line 11):

```xml
    <Version>1.3.1</Version>
```

Replace with:

```xml
    <Version>1.4.0</Version>
```

(Minor version bump — this adds new schema/columns and a new API field, not
just a fix or a UI-only tweak.)

- [ ] **Step 2: Mention the new columns in the README feature list**

Current (`README.md` line 19):

```markdown
- **Hit-count analytics**: Every redirect tracks how many times it has fired and when it was last hit, visible directly in the redirect list.
```

Replace with:

```markdown
- **Hit-count analytics**: Every redirect tracks how many times it has fired and when it was last hit, plus rolling 7-day and 30-day totals, visible directly in the redirect list — useful for spotting stale redirects to retire or rules that aren't firing when they should.
```

- [ ] **Step 3: Update the table count in the README's Database section**

Current (`README.md` line 63):

```markdown
The plugin creates two tables automatically:
```

Replace with:

```markdown
The plugin creates three tables automatically:
```

- [ ] **Step 4: Add the new table to the README's Database section**

Current (`README.md` lines 79-82), find the end of the `RedirectManagerEntries`
table description and the start of the `RedirectManagerMissedRequests`
paragraph:

```markdown
| CreatedDate | datetime | |
| UpdatedDate | datetime | |

**`RedirectManagerMissedRequests`**
```

Replace with:

```markdown
| CreatedDate | datetime | |
| UpdatedDate | datetime | |

**`RedirectManagerHitDaily`**

One row per redirect per UTC day, used to compute the 7-day/30-day rolling
totals shown in the dashboard. Rows older than 35 days are pruned
automatically.

**`RedirectManagerMissedRequests`**
```

- [ ] **Step 5: Build to confirm the version-sync MSBuild target still runs cleanly**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
grep '"version"' App_Plugins/RedirectManager/umbraco-package.json
```

Expected: build succeeds, and the `umbraco-package.json` line now reads
`"version": "1.4.0"` (synced automatically by the existing
`UpdateUmbracoPackageVersion` MSBuild target).

- [ ] **Step 6: Commit**

```bash
git add Umbraco.RedirectManager.csproj README.md App_Plugins/RedirectManager/umbraco-package.json
git commit -m "$(cat <<'EOF'
chore: bump to 1.4.0 and document windowed hit tracking

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Manual verification against a live Umbraco site

Unlike prior sub-projects in this repo, a real test site is available and
was already used to diagnose the flush-failure bug this session
(`/Users/bhan/Desktop/u18`, backed by the `sql2022` Docker container and the
`redirectmanager-nuget` local BaGet feed). This task is executed for real,
not deferred.

**Files:** none (verification only)

- [ ] **Step 1: Confirm the local NuGet feed and SQL Server containers are up**

```bash
docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Ports}}"
```

Expected: both `redirectmanager-nuget` and `sql2022` listed as running. If
not: `docker compose -f docker/docker-compose.yml up -d` from the repo root.

- [ ] **Step 2: Push the new package version to the local feed**

```bash
./scripts/push-to-feed.sh
```

Expected: build succeeds, pack succeeds, and the script reports the package
was pushed to `http://localhost:5555/v3/index.json`.

- [ ] **Step 3: Update the test site's package reference and restore**

```bash
cd /Users/bhan/Desktop/u18/MyProject
dotnet add package BT.RedirectManager --version 1.4.0
dotnet build -c Debug
```

Expected: restore picks up `1.4.0` from the local feed, build succeeds.

- [ ] **Step 4: Start the test site and confirm the migration applies**

Start the site the same way it was run earlier this session (e.g. `dotnet run`
or via the IDE), then check the newest file in
`/Users/bhan/Desktop/u18/MyProject/umbraco/Logs/` for the package migration
plan completing without error. Confirm the new table exists:

```bash
docker exec sql2022 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Vortex29' -C -d RedirectManagerTest18 \
  -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'RedirectManagerHitDaily';"
```

Expected: one row, `RedirectManagerHitDaily`.

- [ ] **Step 5: Fire a redirect and confirm both the all-time and daily-bucket counters update**

With the site running, request an existing redirect (e.g. `/1`, the same one
used earlier this session) a few times, **without stopping the app**, then
wait at least 35 seconds and check:

```bash
docker exec sql2022 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Vortex29' -C -d RedirectManagerTest18 \
  -Q "SELECT Id, OldUrl, HitCount, LastHitDate FROM RedirectManagerEntries WHERE OldUrl = '/1';
      SELECT RedirectId, HitDate, HitCount FROM RedirectManagerHitDaily WHERE RedirectId = 3;"
```

Expected: `RedirectManagerEntries.HitCount` increased by the number of
requests made, and `RedirectManagerHitDaily` has exactly one row for today's
date with a matching `HitCount`.

- [ ] **Step 6: Confirm the dashboard shows the 7d/30d columns with correct values**

Open the Redirect Manager dashboard in the backoffice (Settings → Redirect
Manager) and visually confirm: the "7d" and "30d" columns are present, and
for the `/1` redirect they show the same total as the DB query in Step 5
(since all its hits are from today, `Hits = Hits7d = Hits30d` in this test).

- [ ] **Step 7: Confirm the flush-failure merge-back fix (regression check for this session's bug)**

This directly re-tests the scenario that started this whole feature: fire
one more hit against a *different* redirect (e.g. `/2` if one exists, or
create a throwaway one), then stop the app **immediately** (within a few
seconds, before the 30-second flush timer fires) and restart it. Because the
hit was never flushed before shutdown and the in-memory tracker is gone on
restart, this specific hit is expected to be lost — that's the accepted
`StopAsync`-best-effort tradeoff documented in the original spec, not a
regression. The actual regression check is: fire a hit, **do not** stop the
app, wait 35+ seconds, and confirm the count updates (already covered by
Step 5) — do this at least twice in a row to build confidence the flush
isn't a one-time fluke.

- [ ] **Step 8: Record the result**

Report back: did all of the above match expectations? Any deviation means
returning to Task 1-8 rather than proceeding to Task 10.

---

### Task 10: Publish — NuGet.org and Umbraco Marketplace

**Files:** none (release only)

This step is only executed after explicit go-ahead at the time, since it
pushes a public, effectively irreversible release — confirm with the user
immediately before running Step 2.

- [ ] **Step 1: Confirm working tree is clean and all prior tasks are committed**

```bash
git status --short
git log --oneline -10
```

Expected: no uncommitted changes; the last several commits are the ones from
Tasks 1-8.

- [ ] **Step 2: Tag and push — triggers the existing GitHub Actions publish workflow**

```bash
git tag v1.4.0
git push origin main
git push origin v1.4.0
```

This repo's `.github/workflows/publish-nuget.yml` triggers on `v*.*.*` tag
pushes: it builds, packs, and pushes to `https://api.nuget.org/v3/index.json`
via NuGet Trusted Publishing (OIDC) — no manual `dotnet nuget push` to the
real registry needed.

- [ ] **Step 3: Watch the workflow run**

```bash
gh run watch --exit-status
```

(or check the Actions tab on GitHub). Expected: the `Publish to NuGet`
workflow completes successfully.

- [ ] **Step 4: Confirm the package is live on NuGet.org**

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/bt.redirectmanager/index.json"
```

Expected: `"1.4.0"` present in the `versions` array (may take a few minutes
to appear after the workflow finishes).

- [ ] **Step 5: Umbraco Marketplace**

No separate manual publish step exists for this repo — `umbraco-marketplace.json`
is already packed into the `.nupkg` (`Umbraco.RedirectManager.csproj` line 48)
and the package already carries the `umbraco-marketplace` tag
(`PackageTags`, line 15). Umbraco Marketplace discovers and updates listings
from NuGet.org automatically for packages published this way — once Step 4
confirms `1.4.0` is live on NuGet.org, no further action is needed here
unless the Marketplace listing doesn't refresh within a day or two, in which
case it's a manual check on the marketplace.umbraco.com publisher portal
(requires the user's login — flag it back rather than attempting it).

---

## Out of scope for this plan

- Sorting/filtering the dashboard by 7d/30d hits (e.g. a "0 hits in 30 days"
  quick filter) — flagged in the spec as a natural follow-up, not built now.
- Per-event/hourly hit history.
- Any change to how the all-time `HitCount`/`LastHitDate` columns work.
- Element Library / content-as-config-store storage model — explicitly not
  pursued per user decision.
