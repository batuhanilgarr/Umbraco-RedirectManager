# Culture / Multi-Site Scoping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a nullable `Culture` field to redirect rules and scope live matching by the request's resolved culture — resolved via Umbraco's own Domain-and-Culture configuration (`IDomainService`) — alongside the existing `Domain` scoping, without changing behavior for any site that doesn't configure Culture and Hostnames.

**Architecture:** A new `RedirectEntry.Culture` column (migration + model). A new singleton `IRedirectCultureResolver` resolves a request's domain to a registered culture via `IDomainService.GetAll(false)`, cached 30s. `RedirectService`'s `GetByOldUrl`/`GetByOldUrlAndIsRegex`/`FindOverlappingExactRules` gain a `culture` parameter — folded in as an additional filter condition alongside the existing domain scoping (not a new fallback tier). `RedirectMiddleware` resolves culture once per request and threads it through all three match tiers, with a small `IsCultureInScope` helper mirroring the existing domain check for the wildcard/regex tiers. DTOs and both dashboards get a `Culture` field, mirroring `Domain`'s existing treatment exactly.

**Tech Stack:** Same as the rest of the package. `Umbraco.Cms.Core.Services.IDomainService`/`Umbraco.Cms.Core.Models.IDomain` (verified via direct reflection against both target Umbraco assemblies to have an identical shape in 13.9.2 and 17.1.0 — no `#if` split needed for this part). `Microsoft.Extensions.Caching.Memory.IMemoryCache` for the resolver's cache (same 30s TTL convention as `RedirectService`'s own caches).

Reference spec: `docs/superpowers/specs/2026-07-13-culture-scoping-design.md`

This is sub-project 9 of 9 — the last one in the current roadmap batch. No version bump/release happens here; once this merges, the batch-wide manual verification and publish steps (tracked separately) become the next work.

**Note on sequencing and temporarily-broken builds:** Tasks 3 and 4 each change a constructor/interface signature that the existing test project depends on. Following the same pattern already used successfully in the rate-limiting sub-project: Tasks 1–5 build and verify **only the main csproj** (`dotnet build Umbraco.RedirectManager.csproj`), not the test project — the test project is expected to fail to compile in the middle of this sequence, and that's fixed in Tasks 6–7. Task 10 is the first point the full test suite is expected to build and pass again.

**Note on manual verification:** actually exercising Culture-and-Hostnames-based redirect scoping against a live multi-language Umbraco site is deferred to the project-wide manual verification pass after all 9 sub-projects are done, same as every prior sub-project's deferred dashboard checks.

---

### Task 1: `RedirectEntry.Culture` column + migration

**Files:**
- Modify: `Models/RedirectEntry.cs`
- Modify: `Migrations/RedirectManagerMigrationPlan.cs`

- [ ] **Step 1: Add the `Culture` property to the model**

Current (`Models/RedirectEntry.cs`):
```csharp
    [Column("Domain")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? Domain { get; set; }

    [Column("Description")]
```

Replace with:
```csharp
    [Column("Domain")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? Domain { get; set; }

    [Column("Culture")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(50)]
    public string? Culture { get; set; }

    [Column("Description")]
```

- [ ] **Step 2: Register the new migration step in the plan**

Current (`Migrations/RedirectManagerMigrationPlan.cs`):
```csharp
        To<AddAuditFieldColumns>(new Guid("D3B6A947-8F2C-4E15-9A03-6D7B1C5E9F82"));
    }
}
```

Replace with:
```csharp
        To<AddAuditFieldColumns>(new Guid("D3B6A947-8F2C-4E15-9A03-6D7B1C5E9F82"));
        To<AddCultureColumn>(new Guid("E4F7A208-3C5B-46D2-9A81-7F0C3E6B4D95"));
    }
}
```

- [ ] **Step 3: Add the async migration class (`#if NET10_0_OR_GREATER` section)**

Current:
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

#else
```

Replace with:
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

public class AddCultureColumn : AsyncMigrationBase
{
    public AddCultureColumn(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "Culture") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Culture");
        }

        return Task.CompletedTask;
    }
}

#else
```

- [ ] **Step 4: Add the sync migration class (`#else` section, at the end of the file)**

Current:
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

#endif
```

Replace with:
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

public class AddCultureColumn : MigrationBase
{
    public AddCultureColumn(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "Culture") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Culture");
        }
    }
}

#endif
```

- [ ] **Step 5: Build to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`. (Build only the main csproj — see the plan's sequencing note at the top; the test project isn't affected by this task yet.)

- [ ] **Step 6: Commit**

```bash
git add Models/RedirectEntry.cs Migrations/RedirectManagerMigrationPlan.cs
git commit -m "$(cat <<'EOF'
feat: add Culture column to RedirectEntry

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `IRedirectCultureResolver` + `RedirectCultureResolver` (with unit tests) + composer registration

**Files:**
- Create: `Services/IRedirectCultureResolver.cs`
- Create: `Services/RedirectCultureResolver.cs`
- Create: `Umbraco.RedirectManager.Tests/Services/RedirectCultureResolverTests.cs`
- Modify: `Composers/RedirectManagerComposer.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace Umbraco.RedirectManager.Services;

public interface IRedirectCultureResolver
{
    // Resolves the culture (e.g. "tr-tr") registered against this domain in
    // Umbraco's own Settings > Culture and Hostnames configuration
    // (Umbraco.Cms.Core.Services.IDomainService), or null if no such binding
    // is registered -- meaning only culture-agnostic rules will match.
    string? ResolveCulture(string? domain);
}
```

- [ ] **Step 2: Create the implementation**

```csharp
using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Core.Services;

namespace Umbraco.RedirectManager.Services;

public class RedirectCultureResolver : IRedirectCultureResolver
{
    private const string DomainCultureMapCacheKey = "RedirectManager.DomainCultureMap";

    private readonly IDomainService _domainService;
    private readonly IMemoryCache _memoryCache;

    public RedirectCultureResolver(IDomainService domainService, IMemoryCache memoryCache)
    {
        _domainService = domainService;
        _memoryCache = memoryCache;
    }

    public string? ResolveCulture(string? domain)
    {
        var normalizedDomain = DomainNormalizer.Normalize(domain);
        if (normalizedDomain == null)
            return null;

        var map = GetDomainCultureMap();
        return map.TryGetValue(normalizedDomain, out var culture) ? culture : null;
    }

    private IReadOnlyDictionary<string, string> GetDomainCultureMap()
    {
        return _memoryCache.GetOrCreate(DomainCultureMapCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // false: exclude wildcard domains. A wildcard domain represents a
            // content node's default culture assignment (its DomainName is a
            // node ID, not a real hostname) -- not meaningful for matching
            // against an incoming HTTP Host header.
            foreach (var registeredDomain in _domainService.GetAll(false))
            {
                var normalized = DomainNormalizer.Normalize(registeredDomain.DomainName);
                if (normalized == null || string.IsNullOrWhiteSpace(registeredDomain.LanguageIsoCode))
                    continue;

                map[normalized] = registeredDomain.LanguageIsoCode.Trim().ToLowerInvariant();
            }

            return (IReadOnlyDictionary<string, string>)map;
        }) ?? new Dictionary<string, string>();
    }
}
```

- [ ] **Step 3: Write the unit tests**

```csharp
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class RedirectCultureResolverTests
{
    private static IDomain CreateDomain(string domainName, string? languageIsoCode)
    {
        var domain = Substitute.For<IDomain>();
        domain.DomainName.Returns(domainName);
        domain.LanguageIsoCode.Returns(languageIsoCode);
        return domain;
    }

    [Fact]
    public void ResolveCulture_RegisteredDomain_ReturnsItsCultureLowercased()
    {
        var domainService = Substitute.For<IDomainService>();
        domainService.GetAll(false).Returns(new[] { CreateDomain("tr.example.com", "tr-TR") });
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("tr-tr", resolver.ResolveCulture("tr.example.com"));
    }

    [Fact]
    public void ResolveCulture_UnregisteredDomain_ReturnsNull()
    {
        var domainService = Substitute.For<IDomainService>();
        domainService.GetAll(false).Returns(new[] { CreateDomain("tr.example.com", "tr-TR") });
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(resolver.ResolveCulture("other.example.com"));
    }

    [Fact]
    public void ResolveCulture_NullDomain_ReturnsNull()
    {
        var domainService = Substitute.For<IDomainService>();
        domainService.GetAll(false).Returns(new[] { CreateDomain("tr.example.com", "tr-TR") });
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(resolver.ResolveCulture(null));
    }

    [Fact]
    public void ResolveCulture_QueriesDomainServiceExcludingWildcards()
    {
        var domainService = Substitute.For<IDomainService>();
        domainService.GetAll(false).Returns(new[] { CreateDomain("tr.example.com", "tr-TR") });
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        resolver.ResolveCulture("tr.example.com");

        domainService.Received(1).GetAll(false);
    }
}
```

- [ ] **Step 4: Run the new test file to confirm all 4 pass**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~RedirectCultureResolverTests"
```

Expected: 4 tests pass.

- [ ] **Step 5: Register the resolver in the composer**

Current (`Composers/RedirectManagerComposer.cs`):
```csharp
        builder.Services.Configure<RedirectRateLimitOptions>(builder.Config.GetSection("RedirectManager:RateLimit"));
        builder.Services.AddSingleton<IRedirectRateLimiter, RedirectRateLimiter>();

        builder.Services.AddHttpClient();
```

Replace with:
```csharp
        builder.Services.Configure<RedirectRateLimitOptions>(builder.Config.GetSection("RedirectManager:RateLimit"));
        builder.Services.AddSingleton<IRedirectRateLimiter, RedirectRateLimiter>();

        builder.Services.AddSingleton<IRedirectCultureResolver, RedirectCultureResolver>();

        builder.Services.AddHttpClient();
```

- [ ] **Step 6: Build the main csproj to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`. `IDomainService`/`IDomain` were verified via direct reflection against both target Umbraco assemblies to have an identical shape, so no `#if` split is needed here.

- [ ] **Step 7: Commit**

```bash
git add Services/IRedirectCultureResolver.cs Services/RedirectCultureResolver.cs Umbraco.RedirectManager.Tests/Services/RedirectCultureResolverTests.cs Composers/RedirectManagerComposer.cs
git commit -m "$(cat <<'EOF'
feat: add RedirectCultureResolver, resolving culture from Umbraco's domain/culture bindings

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Thread `culture` through `RedirectService`'s matching/duplicate-check/overlap methods

**Files:**
- Modify: `Models/RedirectEntryDto.cs`
- Modify: `Services/IRedirectService.cs`
- Modify: `Services/RedirectService.cs`
- Modify: `Controllers/RedirectApiController.cs`

**Sequencing note (found during implementation of an earlier draft of this plan):** `RedirectService.Create`/`.Update` need to read `dto.Culture`, and `RedirectApiController.BuildOverlapWarnings` calls `FindOverlappingExactRules` (which, like `FindOverlappingExactRules`'s own pre-existing `domain` parameter, has no default value for the new `culture` parameter). Both of these would break the main package's own build the moment this task's interface/service changes land, if left for Task 5 as originally structured. So this task pulls in two small, self-contained slices that would otherwise create a temporarily-broken main build: adding `Culture` to `CreateRedirectEntryDto`/`UpdateRedirectEntryDto` (Step 1 below), and fixing `BuildOverlapWarnings`'s one call site (Step 9 below). Task 5 still does the rest of the DTO/controller work (`RedirectEntryDto.Culture`, `ToDto` mapping, and threading `dto.Culture` into the `Create`/`Update` duplicate-check calls) — none of which are required for the main csproj to build after this task.

- [ ] **Step 1: Add `Culture` to `CreateRedirectEntryDto` and `UpdateRedirectEntryDto`**

Current (`Models/RedirectEntryDto.cs`):
```csharp
public class CreateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
```

Replace with:
```csharp
public class CreateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Culture { get; set; }
    public string? Description { get; set; }
```

Current:
```csharp
public class UpdateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
```

Replace with:
```csharp
public class UpdateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Culture { get; set; }
    public string? Description { get; set; }
```

(Leave `RedirectEntryDto` — the response DTO — untouched here; its `Culture` property and `ToDto` mapping are added in Task 5, since nothing in this task's compile path needs it.)

- [ ] **Step 2: Update the interface**

Current (`Services/IRedirectService.cs`):
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
    IEnumerable<RedirectEntry> FindOverlappingExactRules(string oldUrl, bool isRegex, string? domain);
    RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
    bool CanAccessTable();
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
    RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null, string? culture = null);
    RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null, string? culture = null);
    IEnumerable<RedirectEntry> GetActiveRegexEntries();
    IEnumerable<RedirectEntry> GetActiveWildcardEntries();
    IEnumerable<RedirectEntry> FindOverlappingExactRules(string oldUrl, bool isRegex, string? domain, string? culture);
    RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
    bool CanAccessTable();
}
```

Note: `culture` has a default value (`= null`) on `GetByOldUrl`/`GetByOldUrlAndIsRegex`, matching how `domain` already does — existing call sites (including this file's own existing test stubs, which never anticipate a culture argument) keep compiling and behaving identically. `FindOverlappingExactRules` deliberately has **no** default for `culture`, matching how its own `domain` parameter already has no default either — existing call sites (including the 6 pre-existing `FindOverlappingExactRules` stub configurations in `RedirectApiControllerTests.cs`) will need a 4th argument added, which Task 6 handles.

- [ ] **Step 3: `GetByOldUrl` — add culture as an additional filter alongside the existing domain fallback**

Current (`Services/RedirectService.cs`):
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

Replace with:
```csharp
    public RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null, string? culture = null)
    {
        using var scope = _scopeProvider.CreateScope();
        var normalizedUrl = NormalizeUrl(oldUrl);
        var normalizedDomain = DomainNormalizer.Normalize(domain);
        var normalizedCulture = NormalizeCulture(culture);
        var now = DateTime.UtcNow;

        RedirectEntry? result = null;
        if (normalizedDomain != null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND Domain = @1 AND IsActive = 1 AND IsRegex = 0 AND (ValidFrom IS NULL OR ValidFrom <= @2) AND (ValidUntil IS NULL OR ValidUntil >= @2) AND (Culture = @3 OR Culture IS NULL OR Culture = '')",
                normalizedUrl, normalizedDomain, now, normalizedCulture);
        }

        if (result == null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND (Domain IS NULL OR Domain = '') AND IsActive = 1 AND IsRegex = 0 AND (ValidFrom IS NULL OR ValidFrom <= @1) AND (ValidUntil IS NULL OR ValidUntil >= @1) AND (Culture = @2 OR Culture IS NULL OR Culture = '')",
                normalizedUrl, now, normalizedCulture);
        }

        scope.Complete();
        return result;
    }
```

- [ ] **Step 4: `GetByOldUrlAndIsRegex` — add culture as a strict-equality duplicate-check dimension**

Current:
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

Replace with:
```csharp
    public RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null, string? culture = null)
    {
        using var scope = _scopeProvider.CreateScope();
        var value = NormalizeOldUrl(oldUrl, isRegex);
        var normalizedDomain = DomainNormalizer.Normalize(domain);
        var normalizedCulture = NormalizeCulture(culture);

        RedirectEntry? result;
        if (normalizedDomain != null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsRegex = @1 AND Domain = @2 AND ((Culture IS NULL AND @3 IS NULL) OR Culture = @3)",
                value, isRegex ? 1 : 0, normalizedDomain, normalizedCulture);
        }
        else
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsRegex = @1 AND (Domain IS NULL OR Domain = '') AND ((Culture IS NULL AND @2 IS NULL) OR Culture = @2)",
                value, isRegex ? 1 : 0, normalizedCulture);
        }

        scope.Complete();
        return result;
    }
```

Note the different comparison style here versus `GetByOldUrl`: this method is the **hard duplicate check** (does an identical rule already exist?), so culture must match via strict tuple equality (a rule with `Culture = "tr-tr"` and one with `Culture = null` are NOT duplicates of each other, even at the same `OldUrl`/`IsRegex`/`Domain`) — `(Culture IS NULL AND @param IS NULL) OR Culture = @param` correctly treats "both null" as equal, and only compares non-null values otherwise. This is different from `GetByOldUrl`'s live-matching semantics, where a `Culture = null` rule is intentionally a wildcard that matches *any* request culture.

- [ ] **Step 5: `FindOverlappingExactRules` — add culture to the in-scope check**

Current:
```csharp
    public IEnumerable<RedirectEntry> FindOverlappingExactRules(string oldUrl, bool isRegex, string? domain)
    {
        using var scope = _scopeProvider.CreateScope();
        var candidates = scope.Database.Fetch<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE IsActive = 1 AND IsRegex = 0 AND OldUrl NOT LIKE '%*%'");
        scope.Complete();

        var normalizedDomain = DomainNormalizer.Normalize(domain);
        var inScope = candidates.Where(c =>
            string.IsNullOrEmpty(c.Domain) ||
            string.IsNullOrEmpty(normalizedDomain) ||
            string.Equals(c.Domain, normalizedDomain, StringComparison.OrdinalIgnoreCase));
```

Replace with:
```csharp
    public IEnumerable<RedirectEntry> FindOverlappingExactRules(string oldUrl, bool isRegex, string? domain, string? culture)
    {
        using var scope = _scopeProvider.CreateScope();
        var candidates = scope.Database.Fetch<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE IsActive = 1 AND IsRegex = 0 AND OldUrl NOT LIKE '%*%'");
        scope.Complete();

        var normalizedDomain = DomainNormalizer.Normalize(domain);
        var normalizedCulture = NormalizeCulture(culture);
        var inScope = candidates.Where(c =>
            (string.IsNullOrEmpty(c.Domain) ||
             string.IsNullOrEmpty(normalizedDomain) ||
             string.Equals(c.Domain, normalizedDomain, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(c.Culture) ||
             string.IsNullOrEmpty(normalizedCulture) ||
             string.Equals(c.Culture, normalizedCulture, StringComparison.OrdinalIgnoreCase)));
```

(The rest of the method — pattern compilation and matching — is unchanged.)

- [ ] **Step 6: Persist `Culture` in `Create`**

Current:
```csharp
        var entry = new RedirectEntry
        {
            OldUrl = NormalizeOldUrl(dto.OldUrl, isRegex),
            NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, isRegex),
            Domain = DomainNormalizer.Normalize(dto.Domain),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
```

Replace with:
```csharp
        var entry = new RedirectEntry
        {
            OldUrl = NormalizeOldUrl(dto.OldUrl, isRegex),
            NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, isRegex),
            Domain = DomainNormalizer.Normalize(dto.Domain),
            Culture = NormalizeCulture(dto.Culture),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
```

- [ ] **Step 7: Persist `Culture` in `Update`**

Current:
```csharp
        existing.IsRegex = dto.IsRegex;
        existing.OldUrl = NormalizeOldUrl(dto.OldUrl, existing.IsRegex);
        existing.NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, existing.IsRegex);
        existing.Domain = DomainNormalizer.Normalize(dto.Domain);
        existing.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
```

Replace with:
```csharp
        existing.IsRegex = dto.IsRegex;
        existing.OldUrl = NormalizeOldUrl(dto.OldUrl, existing.IsRegex);
        existing.NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, existing.IsRegex);
        existing.Domain = DomainNormalizer.Normalize(dto.Domain);
        existing.Culture = NormalizeCulture(dto.Culture);
        existing.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
```

- [ ] **Step 8: Add the `NormalizeCulture` helper**

Current:
```csharp
    private static string NormalizeNewUrl(string newUrl, bool isRegex)
    {
        if (isRegex)
        {
            return newUrl?.Trim() ?? string.Empty;
        }

        return NormalizeUrl(newUrl);
    }

    private static int ValidateStatusCode(int statusCode)
```

Replace with:
```csharp
    private static string NormalizeNewUrl(string newUrl, bool isRegex)
    {
        if (isRegex)
        {
            return newUrl?.Trim() ?? string.Empty;
        }

        return NormalizeUrl(newUrl);
    }

    // Trimmed and lowercased -- unlike Domain, culture codes have no
    // port/IPv6-style structural quirks to handle, so a dedicated normalizer
    // class isn't needed. Lowercasing keeps comparisons collation-agnostic
    // across the DB providers this package supports (SQLite's default TEXT
    // comparison is case-sensitive, unlike SQL Server's common default
    // collation) -- both the persisted value and RedirectCultureResolver's
    // resolved value are lowercased, so comparisons never depend on DB
    // collation behavior.
    private static string? NormalizeCulture(string? culture)
    {
        return string.IsNullOrWhiteSpace(culture) ? null : culture.Trim().ToLowerInvariant();
    }

    private static int ValidateStatusCode(int statusCode)
```

- [ ] **Step 9: Fix `BuildOverlapWarnings`'s call site in `RedirectApiController.cs` (otherwise the main csproj itself fails to build, not just the test project)**

Current (`Controllers/RedirectApiController.cs`):
```csharp
        var overlaps = _redirectService.FindOverlappingExactRules(redirect.OldUrl, redirect.IsRegex, redirect.Domain).ToList();
```

Replace with:
```csharp
        var overlaps = _redirectService.FindOverlappingExactRules(redirect.OldUrl, redirect.IsRegex, redirect.Domain, redirect.Culture).ToList();
```

`redirect` here is a `RedirectEntry` (not a DTO), and `RedirectEntry.Culture` already exists from Task 1, so this compiles regardless of Task 5's DTO/`ToDto` work not having happened yet.

- [ ] **Step 10: Build the main csproj to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0` — the **main csproj** must build cleanly after this task (Steps 1 and 9 above exist specifically to make that true). Only the **test project** (`Umbraco.RedirectManager.Tests`) is expected to still fail to build at this point (its `RedirectApiControllerTests.cs` stubs `FindOverlappingExactRules` with the old 3-arg signature) — that's fixed in Task 6, not this one. Build only the main csproj, not the test project.

- [ ] **Step 11: Commit**

```bash
git add Models/RedirectEntryDto.cs Services/IRedirectService.cs Services/RedirectService.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: thread culture through RedirectService's matching and duplicate-check queries

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Resolve and thread `culture` through `RedirectMiddleware`

**Files:**
- Modify: `Middleware/RedirectMiddleware.cs`

- [ ] **Step 1: Add the constructor dependency**

Current (`Middleware/RedirectMiddleware.cs`, top of file):
```csharp
public class RedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RedirectMiddleware> _logger;
    private readonly IRedirectHitTracker _hitTracker;
    private readonly IVariantBHitTracker _variantBHitTracker;
    private readonly IMissedRequestTracker _missedRequestTracker;
    private readonly IOptions<RedirectRateLimitOptions> _rateLimitOptions;
    private readonly IRedirectRateLimiter _rateLimiter;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly ConcurrentDictionary<string, Regex> WildcardRegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectMiddleware(
        RequestDelegate next,
        ILogger<RedirectMiddleware> logger,
        IRedirectHitTracker hitTracker,
        IVariantBHitTracker variantBHitTracker,
        IMissedRequestTracker missedRequestTracker,
        IOptions<RedirectRateLimitOptions> rateLimitOptions,
        IRedirectRateLimiter rateLimiter)
    {
        _next = next;
        _logger = logger;
        _hitTracker = hitTracker;
        _variantBHitTracker = variantBHitTracker;
        _missedRequestTracker = missedRequestTracker;
        _rateLimitOptions = rateLimitOptions;
        _rateLimiter = rateLimiter;
    }
```

Replace with:
```csharp
public class RedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RedirectMiddleware> _logger;
    private readonly IRedirectHitTracker _hitTracker;
    private readonly IVariantBHitTracker _variantBHitTracker;
    private readonly IMissedRequestTracker _missedRequestTracker;
    private readonly IOptions<RedirectRateLimitOptions> _rateLimitOptions;
    private readonly IRedirectRateLimiter _rateLimiter;
    private readonly IRedirectCultureResolver _cultureResolver;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly ConcurrentDictionary<string, Regex> WildcardRegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectMiddleware(
        RequestDelegate next,
        ILogger<RedirectMiddleware> logger,
        IRedirectHitTracker hitTracker,
        IVariantBHitTracker variantBHitTracker,
        IMissedRequestTracker missedRequestTracker,
        IOptions<RedirectRateLimitOptions> rateLimitOptions,
        IRedirectRateLimiter rateLimiter,
        IRedirectCultureResolver cultureResolver)
    {
        _next = next;
        _logger = logger;
        _hitTracker = hitTracker;
        _variantBHitTracker = variantBHitTracker;
        _missedRequestTracker = missedRequestTracker;
        _rateLimitOptions = rateLimitOptions;
        _rateLimiter = rateLimiter;
        _cultureResolver = cultureResolver;
    }
```

- [ ] **Step 2: Resolve culture once per request and thread it through the exact-tier lookups**

Current:
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

        // Path + query string (e.g. /raporlar.aspx?type=11) so rules with query string match
        var pathAndQuery = path;
        if (context.Request.QueryString.HasValue)
        {
            var query = context.Request.QueryString.Value;
            pathAndQuery = path + (query!.StartsWith("?", StringComparison.Ordinal) ? query : "?" + query);
        }

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

Replace with:
```csharp
    public async Task InvokeAsync(HttpContext context, IRedirectService redirectService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var domain = DomainNormalizer.Normalize(context.Request.Host.Value);
        var culture = _cultureResolver.ResolveCulture(domain);

        if (ShouldSkipRedirect(path))
        {
            await _next(context);
            return;
        }

        // Path + query string (e.g. /raporlar.aspx?type=11) so rules with query string match
        var pathAndQuery = path;
        if (context.Request.QueryString.HasValue)
        {
            var query = context.Request.QueryString.Value;
            pathAndQuery = path + (query!.StartsWith("?", StringComparison.Ordinal) ? query : "?" + query);
        }

        var redirect = redirectService.GetByOldUrl(pathAndQuery, domain, culture);
        if (redirect == null && pathAndQuery != path)
            redirect = redirectService.GetByOldUrl(path, domain, culture);
        if (redirect == null)
        {
            var toggledPath = ToggleTrailingSlash(path);
            if (toggledPath != null)
                redirect = redirectService.GetByOldUrl(toggledPath, domain, culture);
        }
```

- [ ] **Step 3: Thread culture into the wildcard/regex tier calls**

Current:
```csharp
        var wildcardRedirect = FindWildcardRedirect(path, domain, redirectService);
```

Replace with:
```csharp
        var wildcardRedirect = FindWildcardRedirect(path, domain, culture, redirectService);
```

Current:
```csharp
        var regexRedirect = FindRegexRedirect(path, domain, redirectService);
```

Replace with:
```csharp
        var regexRedirect = FindRegexRedirect(path, domain, culture, redirectService);
```

- [ ] **Step 4: Update `FindRegexRedirect`'s signature and domain/culture filtering**

Current:
```csharp
    private RedirectMatch? FindRegexRedirect(string path, string? domain, IRedirectService redirectService)
    {
        try
        {
            // GetActiveRegexEntries() returns the same cached list object on every
            // call within the cache's TTL (invalidation swaps the cache entry
            // rather than mutating it in place), so it's safe to enumerate twice
            // below without an extra .ToList() copy on this hot path.
            var entries = redirectService.GetActiveRegexEntries();

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
```

Replace with:
```csharp
    private RedirectMatch? FindRegexRedirect(string path, string? domain, string? culture, IRedirectService redirectService)
    {
        try
        {
            // GetActiveRegexEntries() returns the same cached list object on every
            // call within the cache's TTL (invalidation swaps the cache entry
            // rather than mutating it in place), so it's safe to enumerate twice
            // below without an extra .ToList() copy on this hot path.
            var entries = redirectService.GetActiveRegexEntries();

            if (domain != null)
            {
                var domainMatch = FindRegexMatchIn(entries.Where(r => r.Domain == domain && IsCultureInScope(r.Culture, culture)), path);
                if (domainMatch != null)
                    return domainMatch;
            }

            return FindRegexMatchIn(entries.Where(r => string.IsNullOrEmpty(r.Domain) && IsCultureInScope(r.Culture, culture)), path);
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

- [ ] **Step 5: Update `FindWildcardRedirect`'s signature and domain/culture filtering**

Current:
```csharp
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
```

Replace with:
```csharp
    private RedirectMatch? FindWildcardRedirect(string path, string? domain, string? culture, IRedirectService redirectService)
    {
        try
        {
            var entries = redirectService.GetActiveWildcardEntries();

            if (domain != null)
            {
                var domainMatch = FindWildcardMatchIn(entries.Where(r => r.Domain == domain && IsCultureInScope(r.Culture, culture)), path);
                if (domainMatch != null)
                    return domainMatch;
            }

            return FindWildcardMatchIn(entries.Where(r => string.IsNullOrEmpty(r.Domain) && IsCultureInScope(r.Culture, culture)), path);
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
```

- [ ] **Step 6: Add the `IsCultureInScope` helper**

Current:
```csharp
    private static bool ShouldSkipRedirect(string path)
    {
```

Replace with:
```csharp
    // A candidate rule with no Culture set applies regardless of the
    // request's resolved culture (culture-agnostic, the default for every
    // existing rule). A candidate scoped to a specific culture only matches
    // when it equals the request's resolved culture -- including when the
    // request's culture couldn't be resolved at all (null), in which case
    // only culture-agnostic rules pass, mirroring how an unresolved domain
    // already only lets global/no-domain rules through.
    private static bool IsCultureInScope(string? candidateCulture, string? requestCulture)
    {
        return string.IsNullOrEmpty(candidateCulture) ||
               string.Equals(candidateCulture, requestCulture, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipRedirect(string path)
    {
```

- [ ] **Step 7: Build the main csproj to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`. (The test project is still expected to fail to build — see the plan's sequencing note. Build only the main csproj.)

- [ ] **Step 8: Commit**

```bash
git add Middleware/RedirectMiddleware.cs
git commit -m "$(cat <<'EOF'
feat: resolve and thread culture through RedirectMiddleware's match tiers

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: DTOs and `RedirectApiController` wiring

**Files:**
- Modify: `Models/RedirectEntryDto.cs`
- Modify: `Controllers/RedirectApiController.cs`

**Note:** `CreateRedirectEntryDto.Culture`/`UpdateRedirectEntryDto.Culture` and `BuildOverlapWarnings`'s `FindOverlappingExactRules` call were already added in Task 3 (a sequencing fix made necessary so the main csproj would build after Task 3 — see that task's note). This task only adds `RedirectEntryDto.Culture` (the response DTO) and threads `dto.Culture` into the `Create`/`Update` duplicate-check calls.

- [ ] **Step 1: Add `Culture` to `RedirectEntryDto`**

Current (`Models/RedirectEntryDto.cs`):
```csharp
public class RedirectEntryDto
{
    public int Id { get; set; }
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
```

Replace with:
```csharp
public class RedirectEntryDto
{
    public int Id { get; set; }
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Culture { get; set; }
    public string? Description { get; set; }
```

- [ ] **Step 2: Map `Culture` in `ToDto`**

Current (`Controllers/RedirectApiController.cs`):
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
            Culture = r.Culture,
            Description = r.Description,
```

- [ ] **Step 3: Pass `dto.Culture` into the duplicate check in `Create`**

Current:
```csharp
    [HttpPost("create")]
    public IActionResult Create([FromBody] CreateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var validationError = ValidateRedirect(dto.OldUrl, dto.NewUrl, dto.StatusCode, dto.IsRegex, dto.VariantBUrl, dto.VariantBWeight);
        if (validationError != null)
            return BadRequest(validationError);

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
        if (duplicate != null)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");

        var redirect = _redirectService.Create(dto, GetCurrentUserName());
        var resultDto = ToDto(redirect);
        resultDto.OverlapWarnings = BuildOverlapWarnings(redirect);
        return Ok(resultDto);
    }
```

Replace with:
```csharp
    [HttpPost("create")]
    public IActionResult Create([FromBody] CreateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var validationError = ValidateRedirect(dto.OldUrl, dto.NewUrl, dto.StatusCode, dto.IsRegex, dto.VariantBUrl, dto.VariantBWeight);
        if (validationError != null)
            return BadRequest(validationError);

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture);
        if (duplicate != null)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");

        var redirect = _redirectService.Create(dto, GetCurrentUserName());
        var resultDto = ToDto(redirect);
        resultDto.OverlapWarnings = BuildOverlapWarnings(redirect);
        return Ok(resultDto);
    }
```

- [ ] **Step 4: Same for `Update`**

Current:
```csharp
    [HttpPut("update/{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var validationError = ValidateRedirect(dto.OldUrl, dto.NewUrl, dto.StatusCode, dto.IsRegex, dto.VariantBUrl, dto.VariantBWeight);
        if (validationError != null)
            return BadRequest(validationError);

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
        if (duplicate != null && duplicate.Id != id)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");

        var redirect = _redirectService.Update(id, dto, GetCurrentUserName());
        if (redirect == null)
            return NotFound();

        var resultDto = ToDto(redirect);
        resultDto.OverlapWarnings = BuildOverlapWarnings(redirect);
        return Ok(resultDto);
    }
```

Replace with:
```csharp
    [HttpPut("update/{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var validationError = ValidateRedirect(dto.OldUrl, dto.NewUrl, dto.StatusCode, dto.IsRegex, dto.VariantBUrl, dto.VariantBWeight);
        if (validationError != null)
            return BadRequest(validationError);

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture);
        if (duplicate != null && duplicate.Id != id)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");

        var redirect = _redirectService.Update(id, dto, GetCurrentUserName());
        if (redirect == null)
            return NotFound();

        var resultDto = ToDto(redirect);
        resultDto.OverlapWarnings = BuildOverlapWarnings(redirect);
        return Ok(resultDto);
    }
```

- [ ] **Step 5: Build the main csproj to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`. (This is the last task in the sequence that leaves the test project non-building — Task 6 fixes it.)

- [ ] **Step 6: Commit**

```bash
git add Models/RedirectEntryDto.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: add Culture to redirect DTOs and thread it through RedirectApiController

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Fix and extend `RedirectApiControllerTests` for `Culture`

**Files:**
- Modify: `Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerTests.cs` (full-file replacement — see below)

The 6 pre-existing `FindOverlappingExactRules(...)` stub calls in this file need a 4th argument now that the interface method has no default for `culture` (matching how it already has none for `domain`). Because this touches 6 scattered call sites plus 5 new test methods, this task replaces the **entire file** rather than presenting fragmented diffs.

- [ ] **Step 1: Replace the entire file contents**

Current: the file as it exists after sub-project 7 (Çakışma/duplicate uyarısı) — 315 lines, ending in `Update_WildcardRuleOverlapsExistingExactRule_PopulatesOverlapWarnings`.

Replace the entire file with:

```csharp
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Umbraco.Cms.Core.Security;
using Umbraco.RedirectManager.Controllers;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Controllers;

public class RedirectApiControllerTests
{
    private readonly IRedirectService _redirectService = Substitute.For<IRedirectService>();
    private readonly RedirectApiController _controller;

    public RedirectApiControllerTests()
    {
        _controller = new RedirectApiController(
            _redirectService,
            Substitute.For<IMissedRequestService>(),
            Substitute.For<IRedirectTelemetryPinger>(),
            Substitute.For<IRedirectTelemetrySettingsStore>(),
            Substitute.For<IRedirectVersionChecker>(),
            Substitute.For<IBackOfficeSecurityAccessor>());
    }

    private static CreateRedirectEntryDto ValidCreateDto() => new()
    {
        OldUrl = "/old-page",
        NewUrl = "/new-page",
        StatusCode = 301,
        IsActive = true,
        IsRegex = false
    };

    private static UpdateRedirectEntryDto ValidUpdateDto() => new()
    {
        OldUrl = "/old-page",
        NewUrl = "/new-page",
        StatusCode = 301,
        IsActive = true,
        IsRegex = false
    };

    [Fact]
    public void Create_EmptyOldUrl_ReturnsBadRequest()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "   ";

        var result = _controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Old URL is required", badRequest.Value);
    }

    [Fact]
    public void Create_301WithoutNewUrl_ReturnsBadRequest()
    {
        var dto = ValidCreateDto();
        dto.NewUrl = null;

        var result = _controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("New URL is required for redirect status codes", badRequest.Value);
    }

    [Fact]
    public void Create_InvalidRegexPattern_ReturnsBadRequest()
    {
        var dto = ValidCreateDto();
        dto.IsRegex = true;
        dto.OldUrl = "(unbalanced";

        var result = _controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid regex pattern", badRequest.Value);
    }

    [Fact]
    public void Create_NewUrlWithInvalidFormat_ReturnsBadRequest()
    {
        var dto = ValidCreateDto();
        dto.NewUrl = "not-a-valid-target";

        var result = _controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("New URL must start with '/' or 'http(s)://'", badRequest.Value);
    }

    [Fact]
    public void Create_DuplicateExists_ReturnsConflict()
    {
        var dto = ValidCreateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns(new RedirectEntry { Id = 99, OldUrl = dto.OldUrl });

        var result = _controller.Create(dto);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Create_ValidNoDuplicate_ReturnsOkAndCallsCreate()
    {
        var dto = ValidCreateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);

        var result = _controller.Create(dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.Received(1).Create(dto, Arg.Any<string?>());
    }

    [Fact]
    public void Update_EmptyOldUrl_ReturnsBadRequest()
    {
        var dto = ValidUpdateDto();
        dto.OldUrl = "";

        var result = _controller.Update(1, dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Old URL is required", badRequest.Value);
    }

    [Fact]
    public void Update_301WithoutNewUrl_ReturnsBadRequest()
    {
        var dto = ValidUpdateDto();
        dto.NewUrl = null;

        var result = _controller.Update(1, dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("New URL is required for redirect status codes", badRequest.Value);
    }

    [Fact]
    public void Update_DuplicateExistsForDifferentId_ReturnsConflict()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns(new RedirectEntry { Id = 99, OldUrl = dto.OldUrl });

        var result = _controller.Update(1, dto);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Update_DuplicateIsTheSameRowBeingEdited_DoesNotReturnConflict()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns(new RedirectEntry { Id = 1, OldUrl = dto.OldUrl });
        var updated = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode };
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns(updated);

        var result = _controller.Update(1, dto);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Update_RowDoesNotExist_ReturnsNotFound()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns((RedirectEntry?)null);

        var result = _controller.Update(1, dto);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Update_ValidNoDuplicate_ReturnsOkAndCallsUpdate()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var updated = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode };
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns(updated);

        var result = _controller.Update(1, dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.Received(1).Update(1, dto, Arg.Any<string?>());
    }

    [Fact]
    public void Create_WildcardRuleOverlapsExistingExactRule_PopulatesOverlapWarnings()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "/blog/*";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain, Culture = dto.Culture };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);
        _redirectService.FindOverlappingExactRules(created.OldUrl, created.IsRegex, created.Domain, created.Culture)
            .Returns(new[] { new RedirectEntry { Id = 10, OldUrl = "/blog/post-1" } });

        var result = _controller.Create(dto);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultDto = Assert.IsType<RedirectEntryDto>(ok.Value);
        Assert.Equal(new[] { "/blog/post-1" }, resultDto.OverlapWarnings);
    }

    [Fact]
    public void Create_RegexRuleWithNoOverlap_OverlapWarningsIsNull()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "^/archive/(.+)$";
        dto.IsRegex = true;
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 2, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = true, Domain = dto.Domain, Culture = dto.Culture };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);
        _redirectService.FindOverlappingExactRules(created.OldUrl, created.IsRegex, created.Domain, created.Culture)
            .Returns(Array.Empty<RedirectEntry>());

        var result = _controller.Create(dto);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultDto = Assert.IsType<RedirectEntryDto>(ok.Value);
        Assert.Null(resultDto.OverlapWarnings);
    }

    [Fact]
    public void Create_ExactRule_DoesNotCallFindOverlappingExactRules()
    {
        var dto = ValidCreateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 3, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain, Culture = dto.Culture };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);

        var result = _controller.Create(dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.DidNotReceive().FindOverlappingExactRules(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public void Create_InactiveWildcardRule_DoesNotCallFindOverlappingExactRules()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "/blog/*";
        dto.IsActive = false;
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 4, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = false, IsRegex = false, Domain = dto.Domain, Culture = dto.Culture };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);

        var result = _controller.Create(dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.DidNotReceive().FindOverlappingExactRules(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public void Create_MoreThanFiveOverlaps_CapsListAndAppendsMoreSuffix()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "/blog/*";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 5, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain, Culture = dto.Culture };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);
        var overlaps = Enumerable.Range(1, 7)
            .Select(i => new RedirectEntry { Id = 100 + i, OldUrl = $"/blog/post-{i}" })
            .ToArray();
        _redirectService.FindOverlappingExactRules(created.OldUrl, created.IsRegex, created.Domain, created.Culture)
            .Returns(overlaps);

        var result = _controller.Create(dto);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultDto = Assert.IsType<RedirectEntryDto>(ok.Value);
        Assert.NotNull(resultDto.OverlapWarnings);
        Assert.Equal(6, resultDto.OverlapWarnings!.Count);
        Assert.Equal(
            new[] { "/blog/post-1", "/blog/post-2", "/blog/post-3", "/blog/post-4", "/blog/post-5" },
            resultDto.OverlapWarnings.Take(5));
        Assert.Equal("...and 2 more", resultDto.OverlapWarnings[5]);
    }

    [Fact]
    public void Update_WildcardRuleOverlapsExistingExactRule_PopulatesOverlapWarnings()
    {
        var dto = ValidUpdateDto();
        dto.OldUrl = "/blog/*";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var updated = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain, Culture = dto.Culture };
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns(updated);
        _redirectService.FindOverlappingExactRules(updated.OldUrl, updated.IsRegex, updated.Domain, updated.Culture)
            .Returns(new[] { new RedirectEntry { Id = 20, OldUrl = "/blog/post-9" } });

        var result = _controller.Update(1, dto);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultDto = Assert.IsType<RedirectEntryDto>(ok.Value);
        Assert.Equal(new[] { "/blog/post-9" }, resultDto.OverlapWarnings);
    }

    [Fact]
    public void Create_ValidWithCulture_ReturnsResultWithCulture()
    {
        var dto = ValidCreateDto();
        dto.Culture = "tr-TR";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, Culture = "tr-tr" };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);

        var result = _controller.Create(dto);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultDto = Assert.IsType<RedirectEntryDto>(ok.Value);
        Assert.Equal("tr-tr", resultDto.Culture);
    }

    [Fact]
    public void Create_DuplicateExistsForSameCulture_ReturnsConflict()
    {
        var dto = ValidCreateDto();
        dto.Culture = "tr-TR";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns(new RedirectEntry { Id = 99, OldUrl = dto.OldUrl, Culture = dto.Culture });

        var result = _controller.Create(dto);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Create_ExistingDuplicateIsForDifferentCulture_DoesNotReturnConflict()
    {
        var dto = ValidCreateDto();
        dto.Culture = "tr-TR";
        // Stub configured for a DIFFERENT culture ("en-US") than what dto
        // actually carries ("tr-TR") -- if the controller correctly threads
        // dto.Culture through to GetByOldUrlAndIsRegex, this stub simply won't
        // match, proving culture (not just OldUrl/IsRegex/Domain) genuinely
        // differentiates rules.
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, "en-US")
            .Returns(new RedirectEntry { Id = 99, OldUrl = dto.OldUrl, Culture = "en-US" });
        var created = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, Culture = dto.Culture };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);

        var result = _controller.Create(dto);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_WildcardRuleWithCulture_PassesCultureToFindOverlappingExactRules()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "/blog/*";
        dto.Culture = "tr-TR";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain, Culture = dto.Culture };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);
        _redirectService.FindOverlappingExactRules(created.OldUrl, created.IsRegex, created.Domain, created.Culture)
            .Returns(new[] { new RedirectEntry { Id = 10, OldUrl = "/blog/post-1" } });

        var result = _controller.Create(dto);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultDto = Assert.IsType<RedirectEntryDto>(ok.Value);
        Assert.Equal(new[] { "/blog/post-1" }, resultDto.OverlapWarnings);
    }

    [Fact]
    public void Update_ValidWithCulture_ReturnsResultWithCulture()
    {
        var dto = ValidUpdateDto();
        dto.Culture = "tr-TR";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain, dto.Culture)
            .Returns((RedirectEntry?)null);
        var updated = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, Culture = "tr-tr" };
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns(updated);

        var result = _controller.Update(1, dto);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultDto = Assert.IsType<RedirectEntryDto>(ok.Value);
        Assert.Equal("tr-tr", resultDto.Culture);
    }
}
```

Note on why the 12 pre-existing pre-sub-project-9 tests keep working unmodified in spirit (only mechanically touched to add `, dto.Culture`/`, updated.Culture`/`, created.Culture` where `GetByOldUrlAndIsRegex`/`FindOverlappingExactRules` are stubbed): every `ValidCreateDto()`/`ValidUpdateDto()` call leaves `Culture` at its default (`null`), so these additions are behaviorally inert for those tests — they exist purely so the file compiles against the new 4-parameter signatures.

- [ ] **Step 2: Run this test class to confirm all 23 tests pass**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~RedirectApiControllerTests"
```

Expected: 23 tests pass (18 pre-existing + 5 new: `Create_ValidWithCulture_ReturnsResultWithCulture`, `Create_DuplicateExistsForSameCulture_ReturnsConflict`, `Create_ExistingDuplicateIsForDifferentCulture_DoesNotReturnConflict`, `Create_WildcardRuleWithCulture_PassesCultureToFindOverlappingExactRules`, `Update_ValidWithCulture_ReturnsResultWithCulture`).

- [ ] **Step 3: Commit**

```bash
git add Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerTests.cs
git commit -m "$(cat <<'EOF'
test: add Culture coverage to RedirectApiControllerTests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Extend `RedirectMiddlewareTests` for `Culture`

**Files:**
- Modify: `Umbraco.RedirectManager.Tests/Middleware/RedirectMiddlewareTests.cs`

- [ ] **Step 1: Add the culture resolver to the test helper**

Current (`CreateMiddleware` helper, as it exists after the rate-limiting sub-project):
```csharp
    private static RedirectMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        IRedirectHitTracker? hitTracker = null,
        IVariantBHitTracker? variantBHitTracker = null,
        IMissedRequestTracker? missedRequestTracker = null,
        RedirectRateLimitOptions? rateLimitOptions = null,
        IRedirectRateLimiter? rateLimiter = null)
    {
        return new RedirectMiddleware(
            next ?? (_ => Task.CompletedTask),
            NullLogger<RedirectMiddleware>.Instance,
            hitTracker ?? Substitute.For<IRedirectHitTracker>(),
            variantBHitTracker ?? Substitute.For<IVariantBHitTracker>(),
            missedRequestTracker ?? Substitute.For<IMissedRequestTracker>(),
            Options.Create(rateLimitOptions ?? new RedirectRateLimitOptions()),
            rateLimiter ?? Substitute.For<IRedirectRateLimiter>());
    }
```

Replace with:
```csharp
    private static RedirectMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        IRedirectHitTracker? hitTracker = null,
        IVariantBHitTracker? variantBHitTracker = null,
        IMissedRequestTracker? missedRequestTracker = null,
        RedirectRateLimitOptions? rateLimitOptions = null,
        IRedirectRateLimiter? rateLimiter = null,
        IRedirectCultureResolver? cultureResolver = null)
    {
        return new RedirectMiddleware(
            next ?? (_ => Task.CompletedTask),
            NullLogger<RedirectMiddleware>.Instance,
            hitTracker ?? Substitute.For<IRedirectHitTracker>(),
            variantBHitTracker ?? Substitute.For<IVariantBHitTracker>(),
            missedRequestTracker ?? Substitute.For<IMissedRequestTracker>(),
            Options.Create(rateLimitOptions ?? new RedirectRateLimitOptions()),
            rateLimiter ?? Substitute.For<IRedirectRateLimiter>(),
            cultureResolver ?? Substitute.For<IRedirectCultureResolver>());
    }
```

An unconfigured `Substitute.For<IRedirectCultureResolver>()` returns `null` from `ResolveCulture(...)` — exactly matching the "no culture resolved" behavior every pre-existing test already implicitly relies on (none of them anticipate a non-null culture), so all pre-existing tests in this file keep passing unmodified.

- [ ] **Step 2: Append these three tests, right after the existing rate-limiting tests, before the closing `}` of the class**

```csharp
    [Fact]
    public async Task InvokeAsync_CultureScopedRule_MatchesWhenResolvedCultureMatches()
    {
        var cultureResolver = Substitute.For<IRedirectCultureResolver>();
        cultureResolver.ResolveCulture(Arg.Any<string?>()).Returns("tr-tr");
        var middleware = CreateMiddleware(cultureResolver: cultureResolver);
        var redirectService = Substitute.For<IRedirectService>();
        var rule = new RedirectEntry { Id = 1, OldUrl = "/eski-sayfa", NewUrl = "/yeni-sayfa", StatusCode = 301, IsActive = true, Culture = "tr-tr" };
        redirectService.GetByOldUrl("/eski-sayfa", Arg.Any<string?>(), "tr-tr").Returns(rule);
        var context = CreateContext("/eski-sayfa");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/yeni-sayfa", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_ResolvedCulture_IsPassedThroughToGetByOldUrl()
    {
        var cultureResolver = Substitute.For<IRedirectCultureResolver>();
        cultureResolver.ResolveCulture(Arg.Any<string?>()).Returns("en-us");
        var middleware = CreateMiddleware(cultureResolver: cultureResolver);
        var redirectService = Substitute.For<IRedirectService>();
        redirectService.GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>()).Returns((RedirectEntry?)null);
        redirectService.GetActiveWildcardEntries().Returns(Array.Empty<RedirectEntry>());
        redirectService.GetActiveRegexEntries().Returns(Array.Empty<RedirectEntry>());
        var context = CreateContext("/some-page");

        await middleware.InvokeAsync(context, redirectService);

        redirectService.Received().GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>(), "en-us");
    }

    [Fact]
    public async Task InvokeAsync_CultureAgnosticRule_MatchesRegardlessOfResolvedCulture()
    {
        var cultureResolver = Substitute.For<IRedirectCultureResolver>();
        cultureResolver.ResolveCulture(Arg.Any<string?>()).Returns("tr-tr");
        var middleware = CreateMiddleware(cultureResolver: cultureResolver);
        var redirectService = Substitute.For<IRedirectService>();
        var rule = new RedirectEntry { Id = 1, OldUrl = "/old-page", NewUrl = "/new-page", StatusCode = 301, IsActive = true, Culture = null };
        redirectService.GetByOldUrl("/old-page", Arg.Any<string?>(), "tr-tr").Returns(rule);
        var context = CreateContext("/old-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/new-page", context.Response.Headers.Location.ToString());
    }
```

- [ ] **Step 3: Run the full middleware test class to confirm all 18 tests pass (15 pre-existing + 3 new)**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~RedirectMiddlewareTests"
```

Expected: 18 tests pass.

- [ ] **Step 4: Commit**

```bash
git add Umbraco.RedirectManager.Tests/Middleware/RedirectMiddlewareTests.cs
git commit -m "$(cat <<'EOF'
test: add Culture coverage to RedirectMiddlewareTests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Lit dashboard — Culture input + list column

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add `culture` to the empty form-data defaults**

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
            preserveQueryString: false,
            validFrom: '',
            validUntil: ''
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
            culture: '',
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

- [ ] **Step 2: Populate `culture` when opening the edit modal**

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
            preserveQueryString: !!redirect.preserveQueryString,
            validFrom: this.toDatetimeLocalValue(redirect.validFrom),
            validUntil: this.toDatetimeLocalValue(redirect.validUntil)
        };
        this.showModal = true;
```

Replace with:
```javascript
    openEditModal(redirect) {
        this.editingRedirect = redirect;
        this.formData = {
            oldUrl: redirect.oldUrl,
            newUrl: redirect.newUrl || '',
            domain: redirect.domain || '',
            culture: redirect.culture || '',
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
```

(`saveRedirect`'s payload is built via `{ ...this.formData, ... }`, and `handleInputChange` sets `formData` fields generically by input `name` — no other JS wiring is needed beyond the form-data defaults, edit population, and the template changes below.)

- [ ] **Step 3: Add a "Culture" column header to the list table**

Current:
```html
                                    <th>Old URL</th>
                                    <th>New URL</th>
                                    <th>Domain</th>
                                    <th>Notes</th>
```

Replace with:
```html
                                    <th>Old URL</th>
                                    <th>New URL</th>
                                    <th>Domain</th>
                                    <th>Culture</th>
                                    <th>Notes</th>
```

- [ ] **Step 4: Add the corresponding list cell**

Current:
```html
                                        <td>
                                            ${redirect.domain
                                                ? html`<span class="domain-pill">${redirect.domain}</span>`
                                                : html`<span class="domain-pill all-domains">All domains</span>`}
                                        </td>
                                        <td title="${redirect.description || ''}">
```

Replace with:
```html
                                        <td>
                                            ${redirect.domain
                                                ? html`<span class="domain-pill">${redirect.domain}</span>`
                                                : html`<span class="domain-pill all-domains">All domains</span>`}
                                        </td>
                                        <td>
                                            ${redirect.culture
                                                ? html`<span class="domain-pill">${redirect.culture}</span>`
                                                : html`<span class="domain-pill all-domains">All cultures</span>`}
                                        </td>
                                        <td title="${redirect.description || ''}">
```

(Reuses the existing `.domain-pill`/`.domain-pill.all-domains` CSS classes — no new CSS needed, keeping the same visual language for both scoping dimensions.)

- [ ] **Step 5: Add the Culture form input, right after the Domain input**

Current:
```html
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
```

Replace with:
```html
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

                                <!-- Culture -->
                                <div class="form-group">
                                    <label>Culture <span class="lbl-opt">(optional)</span></label>
                                    <input type="text"
                                           name="culture"
                                           .value=${this.formData.culture}
                                           @input=${this.handleInputChange}
                                           placeholder="e.g. tr-TR" />
                                    <small>Leave blank to apply to all cultures. Resolved from Umbraco's Culture and Hostnames configuration for the request's domain.</small>
                                </div>
```

- [ ] **Step 6: Syntax check**

```bash
node --check App_Plugins/RedirectManager/redirect-dashboard.js
```

Expected: no output, exit code 0.

- [ ] **Step 7: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "$(cat <<'EOF'
feat: add Culture input and list column to the Lit dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: AngularJS dashboard — Culture input + list column

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`
- Modify: `App_Plugins/RedirectManager/modal.html`
- Modify: `App_Plugins/RedirectManager/dashboard.html`

- [ ] **Step 1: Add `culture` to the new-redirect defaults**

Current (`redirect.controller.js`):
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

Replace with:
```javascript
        vm.openAddModal = function (prefillOldUrl) {
            vm.modalModel = {
                title: "Add New Redirect",
                redirect: {
                    oldUrl: prefillOldUrl || "",
                    newUrl: "",
                    domain: "",
                    culture: "",
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

- [ ] **Step 2: Populate `culture` when opening the edit modal**

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
                    preserveQueryString: !!redirect.preserveQueryString,
                    validFrom: redirect.validFrom ? new Date(redirect.validFrom) : null,
                    validUntil: redirect.validUntil ? new Date(redirect.validUntil) : null
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
                    culture: redirect.culture || "",
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

(`vm.saveRedirect` submits `model.redirect` wholesale via `redirectResource.create`/`.update` — no further JS wiring needed beyond these two default/population objects.)

- [ ] **Step 3: Add the Culture control group to the modal, right after Domain**

Current (`modal.html`):
```html
            <umb-control-group label="Domain"
                               description="Leave blank to apply this redirect to all domains. If both a domain-specific and an all-domains redirect exist for the same Old URL, the domain-specific one wins.">
                <input type="text"
                       ng-model="model.redirect.domain"
                       class="umb-property-editor umb-textstring"
                       placeholder="example.com">
            </umb-control-group>
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

            <umb-control-group label="Culture"
                               description="Leave blank to apply this redirect to all cultures. Resolved from Umbraco's Culture and Hostnames configuration for the request's domain.">
                <input type="text"
                       ng-model="model.redirect.culture"
                       class="umb-property-editor umb-textstring"
                       placeholder="tr-TR">
            </umb-control-group>
```

- [ ] **Step 4: Add the "Culture" column header**

Current (`dashboard.html`):
```html
                            <th>Old URL</th>
                            <th>New URL</th>
                            <th>Domain</th>
                            <th>Notes</th>
```

Replace with:
```html
                            <th>Old URL</th>
                            <th>New URL</th>
                            <th>Domain</th>
                            <th>Culture</th>
                            <th>Notes</th>
```

- [ ] **Step 5: Add the corresponding list cell**

Current:
```html
                            <td class="redirect-url">{{redirect.oldUrl}}</td>
                            <td class="redirect-url">{{redirect.newUrl || '—'}}</td>
                            <td>
                                <span ng-if="redirect.domain"
                                      class="domain-pill">{{redirect.domain}}</span>
                                <span ng-if="!redirect.domain"
                                      class="domain-pill all-domains">All domains</span>
                            </td>
                            <td class="redirect-notes" style="max-width:160px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:11px;color:#888;">
                                {{redirect.description || '—'}}
                            </td>
```

Replace with:
```html
                            <td class="redirect-url">{{redirect.oldUrl}}</td>
                            <td class="redirect-url">{{redirect.newUrl || '—'}}</td>
                            <td>
                                <span ng-if="redirect.domain"
                                      class="domain-pill">{{redirect.domain}}</span>
                                <span ng-if="!redirect.domain"
                                      class="domain-pill all-domains">All domains</span>
                            </td>
                            <td>
                                <span ng-if="redirect.culture"
                                      class="domain-pill">{{redirect.culture}}</span>
                                <span ng-if="!redirect.culture"
                                      class="domain-pill all-domains">All cultures</span>
                            </td>
                            <td class="redirect-notes" style="max-width:160px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:11px;color:#888;">
                                {{redirect.description || '—'}}
                            </td>
```

- [ ] **Step 6: Syntax check the JS file**

```bash
node --check App_Plugins/RedirectManager/redirect.controller.js
```

Expected: no output, exit code 0.

- [ ] **Step 7: Commit**

```bash
git add App_Plugins/RedirectManager/redirect.controller.js App_Plugins/RedirectManager/modal.html App_Plugins/RedirectManager/dashboard.html
git commit -m "$(cat <<'EOF'
feat: add Culture input and list column to the AngularJS dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Run the full test suite and confirm the main package still builds

**Files:** none

- [ ] **Step 1: Run the entire test suite**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj
```

Expected: **65** total tests pass (53 from before this sub-project + 4 new `RedirectCultureResolverTests` + 5 new culture tests in `RedirectApiControllerTests` + 3 new culture tests in `RedirectMiddlewareTests`), `Passed!` summary, 0 failed. This is the first point in this sub-project where the full suite is expected to compile and run — see the sequencing note at the top of this plan.

If any test fails, read the actual failure message and stack trace — do not weaken or delete a failing assertion to make it pass. If a test failure reveals a genuine bug in already-shipped production code unrelated to this sub-project, STOP and report BLOCKED rather than silently patching it.

- [ ] **Step 2: Confirm the main package still builds cleanly on both TFMs (final sanity check)**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: No commit needed for this task** — it's a verification-only task with no file changes.

---

## Out of scope for this plan

- Any deeper integration with Umbraco's published-content culture variants (`IPublishedContent.Cultures`, `UmbracoContext.PublishedRequest`) — only the domain/hostname → culture registry (`IDomainService`) is read.
- A culture dropdown/picker populated from `ILocalizationService`'s registered languages — plain free-text input, matching `Domain`.
- A separate "SiteId" scoping concept beyond Domain + Culture.
- CSV import/export changes to include the new `Culture` column.
- Version bump, git tag, and NuGet publish — happens once, after all 9 sub-projects in this batch are done, as a separate step outside this plan.
