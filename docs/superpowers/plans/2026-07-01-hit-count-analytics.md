# Redirect Hit-Count Analytics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Track a per-redirect hit count and last-hit timestamp, without adding any DB round-trip or measurable latency to `RedirectMiddleware`'s hot path, and surface the numbers in both dashboard UIs.

**Architecture:** An in-memory `ConcurrentDictionary`-backed singleton (`IRedirectHitTracker`) absorbs hits with zero I/O; a `BackgroundService` (`RedirectHitFlushService`) drains it every 30 seconds and writes one atomic `UPDATE` per redirect via the existing `IScopeProvider` pattern. Schema gets two new nullable-safe columns via a new migration step following the existing pattern in `RedirectManagerMigrationPlan.cs`.

**Tech Stack:** ASP.NET Core (`BackgroundService`, DI), NPoco (via Umbraco's `IScopeProvider`), Umbraco CMS migrations, Lit (Umbraco 17+/18 dashboard), AngularJS (Umbraco 13 dashboard).

Reference spec: `docs/superpowers/specs/2026-07-01-hit-count-analytics-design.md`

---

### Task 1: Add `HitCount` and `LastHitDate` to the `RedirectEntry` model

**Files:**
- Modify: `Models/RedirectEntry.cs`

- [ ] **Step 1: Add the two new properties**

Current end of file (lines 43-46):

```csharp
    [Column("IsRegex")]
    [Constraint(Default = false)]
    public bool IsRegex { get; set; } = false;
}
```

Replace with:

```csharp
    [Column("IsRegex")]
    [Constraint(Default = false)]
    public bool IsRegex { get; set; } = false;

    [Column("HitCount")]
    [Constraint(Default = 0)]
    public int HitCount { get; set; } = 0;

    [Column("LastHitDate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? LastHitDate { get; set; }
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
feat: add HitCount and LastHitDate columns to RedirectEntry model

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add the `AddHitCountColumns` migration step

**Files:**
- Modify: `Migrations/RedirectManagerMigrationPlan.cs`

- [ ] **Step 1: Register the new migration step in `DefinePlan()`**

Current (lines 13-17):

```csharp
    protected override void DefinePlan()
    {
        To<CreateRedirectManagerTable>(new Guid("C1686EA6-A8CF-4B7E-B91F-D4519EB17FDA"));
        To<AddIsRegexAndDescriptionColumns>(new Guid("EE2670E3-75C8-4BF6-8D70-36B10D5ECC65"));
    }
```

Replace with:

```csharp
    protected override void DefinePlan()
    {
        To<CreateRedirectManagerTable>(new Guid("C1686EA6-A8CF-4B7E-B91F-D4519EB17FDA"));
        To<AddIsRegexAndDescriptionColumns>(new Guid("EE2670E3-75C8-4BF6-8D70-36B10D5ECC65"));
        To<AddHitCountColumns>(new Guid("4F2A8B31-6C7C-4A8E-9E22-2D4D6D9CDDF1"));
    }
```

- [ ] **Step 2: Add the async (net10.0+) migration class**

In the `#if NET10_0_OR_GREATER` block, immediately after the closing brace of
`AddIsRegexAndDescriptionColumns` (after line 64, before the `#else` on line
66), insert:

```csharp
public class AddHitCountColumns : AsyncMigrationBase
{
    public AddHitCountColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "HitCount") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "HitCount");
        }

        if (ColumnExists(RedirectEntry.TableName, "LastHitDate") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "LastHitDate");
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Add the sync (net8.0) migration class**

In the `#else` block, immediately after the closing brace of the sync
`AddIsRegexAndDescriptionColumns` class (after line 106, before `#endif` on
line 108), insert:

```csharp
public class AddHitCountColumns : MigrationBase
{
    public AddHitCountColumns(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "HitCount") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "HitCount");
        }

        if (ColumnExists(RedirectEntry.TableName, "LastHitDate") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "LastHitDate");
        }
    }
}
```

- [ ] **Step 4: Build to confirm both TFMs compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.
This is the only verification available — there is no runnable Umbraco host
in this repo to actually execute the migration against a live database (same
constraint documented in sub-project 1's plan). Running the migration for
real is covered in Task 8 (manual verification).

- [ ] **Step 5: Commit**

```bash
git add Migrations/RedirectManagerMigrationPlan.cs
git commit -m "$(cat <<'EOF'
feat: add migration for HitCount and LastHitDate columns

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Implement `IRedirectHitTracker`

**Files:**
- Create: `Services/RedirectHitTracker.cs`

- [ ] **Step 1: Write the interface and implementation**

```csharp
using System.Collections.Concurrent;
using System.Threading;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectHitTracker
{
    void RecordHit(int redirectId);
    IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll();
}

public class RedirectHitTracker : IRedirectHitTracker
{
    private readonly ConcurrentDictionary<int, HitAccumulator> _accumulators = new();

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

    public IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll()
    {
        var drained = new Dictionary<int, (int Count, DateTime LastHitUtc)>();

        foreach (var key in _accumulators.Keys.ToArray())
        {
            if (_accumulators.TryRemove(key, out var accumulator))
            {
                drained[key] = (accumulator.Count, accumulator.LastHitUtc);
            }
        }

        return drained;
    }

    private sealed class HitAccumulator
    {
        private int _count;
        private long _lastHitUtcTicks;

        public HitAccumulator(int initialCount, DateTime lastHitUtc)
        {
            _count = initialCount;
            _lastHitUtcTicks = lastHitUtc.Ticks;
        }

        public int Count => _count;
        public DateTime LastHitUtc => new(Interlocked.Read(ref _lastHitUtcTicks), DateTimeKind.Utc);

        public void Increment()
        {
            Interlocked.Increment(ref _count);
            Interlocked.Exchange(ref _lastHitUtcTicks, DateTime.UtcNow.Ticks);
        }
    }
}
```

Note: `_lastHitUtcTicks` uses `Interlocked.Exchange`/`Interlocked.Read` (not a
plain field write) because `DateTime` itself isn't atomically writable on
all platforms — reading/writing its underlying `long` tick count via
`Interlocked` is the standard thread-safe pattern for this.

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Services/RedirectHitTracker.cs
git commit -m "$(cat <<'EOF'
feat: add in-memory redirect hit tracker

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Implement `RedirectHitFlushService`

**Files:**
- Create: `Services/RedirectHitFlushService.cs`

- [ ] **Step 1: Write the background service**

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            Flush();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Flush();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

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
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Services/RedirectHitFlushService.cs
git commit -m "$(cat <<'EOF'
feat: add background service to flush redirect hit counts

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Register the tracker and flush service in the composer

**Files:**
- Modify: `Composers/RedirectManagerComposer.cs`

- [ ] **Step 1: Add the two registrations**

Current (lines 13-17):

```csharp
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IRedirectService, RedirectService>();
```

Replace with:

```csharp
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IRedirectService, RedirectService>();
        builder.Services.AddSingleton<IRedirectHitTracker, RedirectHitTracker>();
        builder.Services.AddHostedService<RedirectHitFlushService>();
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add Composers/RedirectManagerComposer.cs
git commit -m "$(cat <<'EOF'
feat: register redirect hit tracker and flush service

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Record hits from `RedirectMiddleware`

**Files:**
- Modify: `Middleware/RedirectMiddleware.cs`

`IRedirectHitTracker` is a singleton, so it's injected via the constructor
(like `ILogger`), not as an `InvokeAsync` method parameter (which is how the
scoped `IRedirectService` is injected today).

- [ ] **Step 1: Add the constructor dependency**

Current (lines 9-21):

```csharp
public class RedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RedirectMiddleware> _logger;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectMiddleware(RequestDelegate next, ILogger<RedirectMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
```

Replace with:

```csharp
public class RedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RedirectMiddleware> _logger;
    private readonly IRedirectHitTracker _hitTracker;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectMiddleware(RequestDelegate next, ILogger<RedirectMiddleware> logger, IRedirectHitTracker hitTracker)
    {
        _next = next;
        _logger = logger;
        _hitTracker = hitTracker;
    }
```

- [ ] **Step 2: Record the hit in the exact-match branch**

Current (lines 45-49):

```csharp
        if (redirect != null && redirect.IsActive)
        {
            _logger.LogDebug("Redirect found for {OldUrl} -> {NewUrl} ({StatusCode})", 
                redirect.OldUrl, redirect.NewUrl, redirect.StatusCode);

            switch (redirect.StatusCode)
```

Replace with:

```csharp
        if (redirect != null && redirect.IsActive)
        {
            _logger.LogDebug("Redirect found for {OldUrl} -> {NewUrl} ({StatusCode})", 
                redirect.OldUrl, redirect.NewUrl, redirect.StatusCode);
            _hitTracker.RecordHit(redirect.Id);

            switch (redirect.StatusCode)
```

One call site here covers all four status codes (301/302/404/410) for exact
matches, since every branch of the switch returns immediately after setting
the response — no need to duplicate the call inside each `case`.

- [ ] **Step 3: Record the hit in the regex-match branch**

Current (lines 74-80):

```csharp
        var regexRedirect = FindRegexRedirect(path, redirectService);
        if (regexRedirect != null)
        {
            _logger.LogDebug("Regex redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                regexRedirect.Entry.OldUrl, regexRedirect.ComputedNewUrl, regexRedirect.Entry.StatusCode);

            switch (regexRedirect.Entry.StatusCode)
```

Replace with:

```csharp
        var regexRedirect = FindRegexRedirect(path, redirectService);
        if (regexRedirect != null)
        {
            _logger.LogDebug("Regex redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                regexRedirect.Entry.OldUrl, regexRedirect.ComputedNewUrl, regexRedirect.Entry.StatusCode);
            _hitTracker.RecordHit(regexRedirect.Entry.Id);

            switch (regexRedirect.Entry.StatusCode)
```

Same reasoning: one call site covers all four status codes for regex
matches.

- [ ] **Step 4: Add the using statement**

Add to the top of the file, alongside the existing `using` statements:

```csharp
using Umbraco.RedirectManager.Services;
```

(Verify it isn't already present before adding — the file currently imports
`Umbraco.RedirectManager.Services` already for `IRedirectService`, so this
step may be a no-op; check the top of the file first.)

- [ ] **Step 5: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 6: Commit**

```bash
git add Middleware/RedirectMiddleware.cs
git commit -m "$(cat <<'EOF'
feat: record redirect hits from middleware

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Surface `HitCount` and `LastHitDate` through the API

**Files:**
- Modify: `Models/RedirectEntryDto.cs`
- Modify: `Controllers/RedirectApiController.cs`

- [ ] **Step 1: Add the two fields to `RedirectEntryDto`**

Only `RedirectEntryDto` needs the new fields — `CreateRedirectEntryDto` and
`UpdateRedirectEntryDto` are request bodies, and hit stats aren't
client-settable.

Current (`Models/RedirectEntryDto.cs` lines 3-12):

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
}
```

Replace with:

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
```

- [ ] **Step 2: Update the 4 `RedirectEntryDto` construction sites in the controller**

`Controllers/RedirectApiController.cs` builds a `RedirectEntryDto` inline in
four places, all with the identical field list. Add `HitCount` and
`LastHitDate` to each.

**Site 1 — `GetAll` (around line 38-47; exact line numbers shifted +3 since the API-authorization fix added 3 lines at the top of this file — match by code content, not line number):**

Current:

```csharp
        return Ok(redirects.Select(r => new RedirectEntryDto
        {
            Id = r.Id,
            OldUrl = r.OldUrl,
            NewUrl = r.NewUrl,
            Description = r.Description,
            StatusCode = r.StatusCode,
            IsActive = r.IsActive,
            IsRegex = r.IsRegex
        }));
```

Replace with:

```csharp
        return Ok(redirects.Select(r => new RedirectEntryDto
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
        }));
```

**Site 2 — `Get` (around line 57-66; match by code content, not line number):**

Current:

```csharp
        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex
        });
```

Replace with:

```csharp
        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex,
            HitCount = redirect.HitCount,
            LastHitDate = redirect.LastHitDate
        });
```

**Site 3 — `Create` (around line 87-96; match by code content, not line number):**

Current:

```csharp
        var redirect = _redirectService.Create(dto);
        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex
        });
```

Replace with:

```csharp
        var redirect = _redirectService.Create(dto);
        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex,
            HitCount = redirect.HitCount,
            LastHitDate = redirect.LastHitDate
        });
```

**Site 4 — `Update` (around line 120-129; match by code content, not line number):**

Current:

```csharp
        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex
        });
```

(This is the fourth occurrence, inside the `Update` action — identical shape
to Site 2/3 above but bound to the `Update` method's local `redirect`
variable.) Replace with:

```csharp
        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex,
            HitCount = redirect.HitCount,
            LastHitDate = redirect.LastHitDate
        });
```

Since Site 2, 3, and 4 are textually identical, match each occurrence in
its own method context (`Get`, `Create`, `Update` respectively) rather than
a blind find-and-replace-all, to avoid missing one or double-patching.

- [ ] **Step 3: Build to confirm it compiles**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit**

```bash
git add Models/RedirectEntryDto.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: expose HitCount and LastHitDate in the redirect API

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Add a "Hits" column to both dashboard UIs

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js` (Lit, Umbraco 17+/18)
- Modify: `App_Plugins/RedirectManager/dashboard.html` (AngularJS, Umbraco 13)

- [ ] **Step 1: Add a hit-title helper method to the Lit component**

Current (`redirect-dashboard.js` lines 852-860):

```javascript
    getStatusLabel(code) {
        const labels = {
            301: '301 - Permanent',
            302: '302 - Temporary',
            404: '404 - Not Found',
            410: '410 - Gone'
        };
        return labels[code] || code;
    }
```

Add immediately after it:

```javascript
    getLastHitTitle(redirect) {
        return redirect.lastHitDate
            ? `Last hit: ${new Date(redirect.lastHitDate).toLocaleString()}`
            : 'Never hit';
    }
```

- [ ] **Step 2: Add the "Hits" column header**

Current (`redirect-dashboard.js` lines 958-965):

```javascript
                                <th style="text-align: center;">Status</th>
                                <th style="text-align: center;">Old URL</th>
                                <th style="text-align: center;">New URL</th>
                                <th style="text-align: center;">Notes</th>
                                <th style="text-align: center;">Type</th>
                                <th style="text-align: center;">Match</th>
                                <th style="text-align: center;">Active</th>
                                <th style="text-align: center;">Actions</th>
```

Replace with:

```javascript
                                <th style="text-align: center;">Status</th>
                                <th style="text-align: center;">Old URL</th>
                                <th style="text-align: center;">New URL</th>
                                <th style="text-align: center;">Notes</th>
                                <th style="text-align: center;">Type</th>
                                <th style="text-align: center;">Match</th>
                                <th style="text-align: center;">Active</th>
                                <th style="text-align: center;">Hits</th>
                                <th style="text-align: center;">Actions</th>
```

- [ ] **Step 3: Add the "Hits" data cell**

Current (`redirect-dashboard.js` lines 996-1000):

```javascript
                                    <td>
                                        <span class="${redirect.isActive ? 'active-yes' : 'active-no'}">
                                            ${redirect.isActive ? 'Yes' : 'No'}
                                        </span>
                                    </td>
```

Replace with:

```javascript
                                    <td>
                                        <span class="${redirect.isActive ? 'active-yes' : 'active-no'}">
                                            ${redirect.isActive ? 'Yes' : 'No'}
                                        </span>
                                    </td>
                                    <td style="text-align: center;" title="${this.getLastHitTitle(redirect)}">
                                        ${redirect.hitCount || 0}
                                    </td>
```

This cell must be inserted between the existing "Active" `<td>` and the
"Actions" `<td class="actions actions-cell">`, matching the header order
from Step 2.

- [ ] **Step 4: Add the "Hits" column to the AngularJS dashboard**

Current (`dashboard.html` lines 67-75):

```html
                    <tr>
                        <th>Status</th>
                        <th>Old URL</th>
                        <th>New URL</th>
                        <th>Notes</th>
                        <th>Type</th>
                        <th>Match</th>
                        <th>Active</th>
                        <th>Actions</th>
                    </tr>
```

Replace with:

```html
                    <tr>
                        <th>Status</th>
                        <th>Old URL</th>
                        <th>New URL</th>
                        <th>Notes</th>
                        <th>Type</th>
                        <th>Match</th>
                        <th>Active</th>
                        <th>Hits</th>
                        <th>Actions</th>
                    </tr>
```

- [ ] **Step 5: Add the "Hits" data cell to the AngularJS dashboard**

Current (`dashboard.html` lines 89-94):

```html
                        <td>
                            <span ng-class="{'redirect-active': redirect.isActive, 'redirect-inactive': !redirect.isActive}">
                                {{redirect.isActive ? 'Yes' : 'No'}}
                            </span>
                        </td>
                        <td class="redirect-actions">
```

Replace with:

```html
                        <td>
                            <span ng-class="{'redirect-active': redirect.isActive, 'redirect-inactive': !redirect.isActive}">
                                {{redirect.isActive ? 'Yes' : 'No'}}
                            </span>
                        </td>
                        <td title="{{redirect.lastHitDate ? ('Last hit: ' + (redirect.lastHitDate | date:'medium')) : 'Never hit'}}">
                            {{redirect.hitCount || 0}}
                        </td>
                        <td class="redirect-actions">
```

- [ ] **Step 6: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js App_Plugins/RedirectManager/dashboard.html
git commit -m "$(cat <<'EOF'
feat: show hit count and last-hit date in both dashboard UIs

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Manual verification — DEFERRED (documented, not executed)

Same constraint as sub-project 1's Task 2: there is no automated test
project and no runnable Umbraco host in this repo. No local Umbraco test
site is available (confirmed with user on 2026-07-01). This task documents
what must be run manually before the batched `1.3.0` release ships, but is
not executed as part of this implementation pass.

**Files:** none

- [ ] **Step 1 (deferred): Push the built package to the local BaGet feed and install into a test site**

```bash
docker compose -f docker/docker-compose.yml up -d
./scripts/push-to-feed.sh
```

Then, in a test Umbraco site pointed at `http://localhost:5555/v3/index.json`,
update the package and start the site so the new migration runs.

- [ ] **Step 2 (deferred): Confirm the migration applied cleanly**

Check the site's startup logs for the `BT.RedirectManager` package migration
plan completing without error, and confirm (via a DB browser or the
dashboard) that `RedirectManagerEntries` now has `HitCount` and
`LastHitDate` columns.

- [ ] **Step 3 (deferred): Confirm hits are recorded and flushed**

Trigger a redirect a few times (visit an old URL a few times in a browser),
wait at least 30 seconds, then reload the Redirect Manager dashboard and
confirm the "Hits" column shows the expected count and the tooltip shows a
recent "Last hit" timestamp.

- [ ] **Step 4 (deferred): Confirm hot-path latency is unaffected**

Compare response time for a redirected request before and after this change
(e.g. via browser dev tools' Network tab) — expect no observable difference,
since `RecordHit` only touches an in-memory dictionary.

---

## Out of scope for this plan

- Sub-projects 3 (404 auto-log) and 4 (domain-scoped redirects) — separate
  specs and plans.
- Version bump, git tag, and NuGet publish — deferred until all 4
  sub-projects are complete, released together as `1.3.0`.
- Historical/time-series hit data, a "reset stats" action, and sortable/
  filterable hit-count columns — all explicitly out of scope per the
  approved spec.
