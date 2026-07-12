# Overlap / Duplicate Warning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a newly created/updated **broad matcher** rule (true regex, or a wildcard `OldUrl` containing `*`) is active, detect whether it also matches the `OldUrl` of any existing **active exact** rule, and surface that as a non-blocking warning in both dashboards — without changing the existing hard duplicate-conflict behavior or the Create/Update response shape in a way that breaks existing consumers.

**Architecture:** A new `IRedirectService.FindOverlappingExactRules(oldUrl, isRegex, domain)` method queries active, truly-exact rules (`IsRegex=0 AND OldUrl NOT LIKE '%*%'`), filters them to the same domain-fallback scope used elsewhere in this class, and tests each candidate's `OldUrl` against the new rule's compiled pattern (regex directly, or via `WildcardPatternBuilder.BuildRegexPattern` for wildcards). `RedirectApiController.Create`/`.Update` call this (only when the saved entry is active and broad), cap the result at 5 entries (plus a "+N more" suffix), and attach it to a new optional `RedirectEntryDto.OverlapWarnings` field — `null`/absent everywhere else. Both dashboards then show a non-blocking warning alongside (Lit: merged into) the existing success notification when that field is present.

**Tech Stack:** Same as the rest of the package — NPoco/`IScopeProvider` for the new query, `System.Text.RegularExpressions.Regex` (with the same 100ms timeout pattern already used in `RedirectMiddleware`/`RedirectApiController`), xUnit + NSubstitute for the controller-level tests (mocking `IRedirectService`, consistent with `RedirectApiControllerTests` from the previous sub-project).

Reference spec: `docs/superpowers/specs/2026-07-12-overlap-warning-design.md`

This is sub-project 7 of 9 in the current roadmap batch. No version bump/release happens here — that is a separate step once all 9 sub-projects are done.

**Note on the dashboard UX detail found while planning:** the spec described "a second toast after the existing success toast" for both dashboards. On inspecting the actual code:
- The **AngularJS** dashboard (`redirect.controller.js`) uses Umbraco's built-in `notificationsService`, which naturally stacks multiple notifications — a literal second `notificationsService.warning(...)` call after the success one works as originally envisioned (Task 5).
- The **Lit** dashboard (`redirect-dashboard.js`) has its own single-slot `showMessage(text, type)` component (one `messageText`/`messageType` pair, no queue) — calling it twice in a row would just have the second call silently overwrite the first before it's ever shown. Task 4 below merges the two into **one** combined message (using the new `warning` style) instead, since that's the only way this component can actually convey both pieces of information at once. This is a small, code-driven adjustment to the spec's UX description, not a scope change — the information shown to the admin is the same either way.

---

### Task 1: `IRedirectService.FindOverlappingExactRules` + implementation

**Files:**
- Modify: `Services/IRedirectService.cs`
- Modify: `Services/RedirectService.cs`

- [ ] **Step 1: Add the method to the interface**

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

- [ ] **Step 2: Implement it in `RedirectService`**

Current (`Services/RedirectService.cs`, top of file):
```csharp
using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectService : IRedirectService
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IMemoryCache _memoryCache;

    private const string ActiveRegexCacheKey = "RedirectManager.ActiveRegexEntries";
    private const string ActiveWildcardCacheKey = "RedirectManager.ActiveWildcardEntries";

    public RedirectService(IScopeProvider scopeProvider, IMemoryCache memoryCache)
    {
        _scopeProvider = scopeProvider;
        _memoryCache = memoryCache;
    }
```

Replace with:
```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectService : IRedirectService
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IMemoryCache _memoryCache;

    private const string ActiveRegexCacheKey = "RedirectManager.ActiveRegexEntries";
    private const string ActiveWildcardCacheKey = "RedirectManager.ActiveWildcardEntries";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectService(IScopeProvider scopeProvider, IMemoryCache memoryCache)
    {
        _scopeProvider = scopeProvider;
        _memoryCache = memoryCache;
    }
```

Then, immediately after the existing `GetActiveWildcardEntries()` method (right before `GetHitWindowCounts()`), add the new method:

Current (`Services/RedirectService.cs`, end of `GetActiveWildcardEntries`):
```csharp
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

Replace with:
```csharp
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

        Regex pattern;
        try
        {
            pattern = new Regex(
                isRegex ? oldUrl : WildcardPatternBuilder.BuildRegexPattern(oldUrl),
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                RegexTimeout);
        }
        catch (ArgumentException)
        {
            return Enumerable.Empty<RedirectEntry>();
        }

        var overlaps = new List<RedirectEntry>();
        foreach (var candidate in inScope)
        {
            try
            {
                if (pattern.IsMatch(candidate.OldUrl))
                    overlaps.Add(candidate);
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip this candidate rather than fail the whole save; this is a
                // best-effort, non-blocking warning, not a correctness guarantee.
            }
        }

        return overlaps;
    }

    public IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts()
```

- [ ] **Step 3: Build to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 4: Commit**

```bash
git add Services/IRedirectService.cs Services/RedirectService.cs
git commit -m "$(cat <<'EOF'
feat: add FindOverlappingExactRules to detect broad-rule/exact-rule overlap

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Wire the overlap check into `Create`/`Update`, add `OverlapWarnings` to the DTO

**Files:**
- Modify: `Models/RedirectEntryDto.cs`
- Modify: `Controllers/RedirectApiController.cs`

- [ ] **Step 1: Add the new field to `RedirectEntryDto`**

Current (`Models/RedirectEntryDto.cs`):
```csharp
namespace Umbraco.RedirectManager.Models;

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

Replace with:
```csharp
namespace Umbraco.RedirectManager.Models;

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

    // Populated only by Create/Update, only when this entry is an active
    // broad matcher (regex or wildcard) that also matches one or more
    // existing active exact rules. Null/absent everywhere else (GetAll,
    // the /test match endpoint, exact rules, inactive rules).
    public List<string>? OverlapWarnings { get; set; }
}
```

- [ ] **Step 2: Wire it into `Create`**

Current (`Controllers/RedirectApiController.cs`):
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
        return Ok(ToDto(redirect));
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

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
        if (duplicate != null)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");

        var redirect = _redirectService.Create(dto, GetCurrentUserName());
        var resultDto = ToDto(redirect);
        resultDto.OverlapWarnings = BuildOverlapWarnings(redirect);
        return Ok(resultDto);
    }
```

- [ ] **Step 3: Wire it into `Update`**

Current (`Controllers/RedirectApiController.cs`):
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

        return Ok(ToDto(redirect));
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

- [ ] **Step 4: Add the `BuildOverlapWarnings` helper next to `ToDto`**

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

    private const int MaxOverlapWarnings = 5;

    private List<string>? BuildOverlapWarnings(RedirectEntry redirect)
    {
        var isBroadMatcher = redirect.IsRegex || redirect.OldUrl.Contains('*');
        if (!redirect.IsActive || !isBroadMatcher)
            return null;

        var overlaps = _redirectService.FindOverlappingExactRules(redirect.OldUrl, redirect.IsRegex, redirect.Domain).ToList();
        if (overlaps.Count == 0)
            return null;

        var warnings = overlaps.Take(MaxOverlapWarnings).Select(r => r.OldUrl).ToList();
        if (overlaps.Count > MaxOverlapWarnings)
            warnings.Add($"...and {overlaps.Count - MaxOverlapWarnings} more");

        return warnings;
    }
```

- [ ] **Step 5: Build to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 6: Commit**

```bash
git add Models/RedirectEntryDto.cs Controllers/RedirectApiController.cs
git commit -m "$(cat <<'EOF'
feat: surface overlap warnings on Create/Update via RedirectEntryDto.OverlapWarnings

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `RedirectApiControllerTests` — overlap-warning coverage

**Files:**
- Modify: `Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerTests.cs`

- [ ] **Step 1: Append these six tests to the existing `RedirectApiControllerTests` class**

Add these methods inside the existing class body (e.g. right after `Update_ValidNoDuplicate_ReturnsOkAndCallsUpdate`, before the closing `}` of the class):

```csharp
    [Fact]
    public void Create_WildcardRuleOverlapsExistingExactRule_PopulatesOverlapWarnings()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "/blog/*";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);
        _redirectService.FindOverlappingExactRules(created.OldUrl, created.IsRegex, created.Domain)
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
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 2, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = true, Domain = dto.Domain };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);
        _redirectService.FindOverlappingExactRules(created.OldUrl, created.IsRegex, created.Domain)
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
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 3, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);

        var result = _controller.Create(dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.DidNotReceive().FindOverlappingExactRules(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>());
    }

    [Fact]
    public void Create_InactiveWildcardRule_DoesNotCallFindOverlappingExactRules()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "/blog/*";
        dto.IsActive = false;
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 4, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = false, IsRegex = false, Domain = dto.Domain };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);

        var result = _controller.Create(dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.DidNotReceive().FindOverlappingExactRules(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>());
    }

    [Fact]
    public void Create_MoreThanFiveOverlaps_CapsListAndAppendsMoreSuffix()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "/blog/*";
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 5, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);
        var overlaps = Enumerable.Range(1, 7)
            .Select(i => new RedirectEntry { Id = 100 + i, OldUrl = $"/blog/post-{i}" })
            .ToArray();
        _redirectService.FindOverlappingExactRules(created.OldUrl, created.IsRegex, created.Domain)
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
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var updated = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode, IsActive = true, IsRegex = false, Domain = dto.Domain };
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns(updated);
        _redirectService.FindOverlappingExactRules(updated.OldUrl, updated.IsRegex, updated.Domain)
            .Returns(new[] { new RedirectEntry { Id = 20, OldUrl = "/blog/post-9" } });

        var result = _controller.Update(1, dto);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultDto = Assert.IsType<RedirectEntryDto>(ok.Value);
        Assert.Equal(new[] { "/blog/post-9" }, resultDto.OverlapWarnings);
    }
```

- [ ] **Step 2: Run just this test class to confirm they pass**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~RedirectApiControllerTests"
```

Expected: all 18 tests pass (the 12 pre-existing ones plus these 6 new ones).

- [ ] **Step 3: Commit**

```bash
git add Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerTests.cs
git commit -m "$(cat <<'EOF'
test: add overlap-warning coverage to RedirectApiControllerTests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Lit dashboard — merged warning toast

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js`

- [ ] **Step 1: Add the `notif-warning` CSS rule next to the existing notif styles**

Current:
```css
        .notif-success { background: #f0fdf4; border-color: #bbf7d0; color: #166534; }
        .notif-error   { background: #fef2f2; border-color: #fecaca; color: #991b1b; }
        .notif-info    { background: #eff6ff; border-color: #bfdbfe; color: #1e40af; }
```

Replace with:
```css
        .notif-success { background: #f0fdf4; border-color: #bbf7d0; color: #166534; }
        .notif-error   { background: #fef2f2; border-color: #fecaca; color: #991b1b; }
        .notif-info    { background: #eff6ff; border-color: #bfdbfe; color: #1e40af; }
        .notif-warning { background: #fffbeb; border-color: #fde68a; color: #92400e; }
```

- [ ] **Step 2: Merge the overlap warning into the save handler's single message slot**

Current:
```javascript
            if (response.ok) {
                const saved = await response.json();

                if (this.editingRedirect) {
                    this.redirects = this.redirects.map(r => r.id === saved.id ? saved : r);
                    this.showMessage('Redirect updated', 'success');
                } else {
                    this.redirects = [saved, ...this.redirects];
                    this.showMessage('Redirect created', 'success');
                }

                this.closeModal();
            } else {
                const error = await response.text();
                this.showMessage(error || 'Failed to save redirect', 'error');
            }
        } catch (error) {
            console.error('Failed to save redirect:', error);
            this.showMessage('Failed to save redirect', 'error');
        }
    }
```

Replace with:
```javascript
            if (response.ok) {
                const saved = await response.json();
                const overlapNote = (saved.overlapWarnings && saved.overlapWarnings.length > 0)
                    ? ` Heads up: this rule also matches existing active rule(s): ${saved.overlapWarnings.join(', ')}`
                    : '';

                if (this.editingRedirect) {
                    this.redirects = this.redirects.map(r => r.id === saved.id ? saved : r);
                    this.showMessage(`Redirect updated.${overlapNote}`, overlapNote ? 'warning' : 'success');
                } else {
                    this.redirects = [saved, ...this.redirects];
                    this.showMessage(`Redirect created.${overlapNote}`, overlapNote ? 'warning' : 'success');
                }

                this.closeModal();
            } else {
                const error = await response.text();
                this.showMessage(error || 'Failed to save redirect', 'error');
            }
        } catch (error) {
            console.error('Failed to save redirect:', error);
            this.showMessage('Failed to save redirect', 'error');
        }
    }
```

Note: `Redirect updated.` / `Redirect created.` (with the trailing period now always present, whether or not `overlapNote` is appended) is a deliberate one-character change from today's exact text (`'Redirect updated'` / `'Redirect created'`, no period) so the combined sentence reads naturally either way. This is purely cosmetic and doesn't change behavior.

- [ ] **Step 3: Manual verification (deferred — no live Umbraco site running yet in this sub-project; to be executed during the batch-wide manual verification pass after all 9 sub-projects are done)**

Checklist to run later: create a wildcard rule (e.g. `/blog/*`) while an active exact rule for a path under it (e.g. `/blog/post-1`) already exists; confirm the Lit dashboard shows one amber "warning" toast mentioning `/blog/post-1` instead of the plain green success toast, and that the new rule was still saved successfully (appears in the list). Then create/update a rule with no overlap and confirm the plain green success toast still appears as before.

- [ ] **Step 4: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "$(cat <<'EOF'
feat: show overlap warning in the Lit dashboard's save toast

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: AngularJS dashboard — second warning notification

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect.controller.js`

- [ ] **Step 1: Read the saved entry from the create/update responses and show a second notification when it has overlap warnings**

Current:
```javascript
            if (redirect.id) {
                redirectResource.update(redirect.id, redirect).then(function () {
                    notificationsService.success("Success", "Redirect updated successfully");
                    vm.closeModal();
                    vm.loadRedirects();
                }, function (error) {
                    notificationsService.error("Error", error.data || "Failed to update redirect");
                    model.submitButtonState = "error";
                });
            } else {
                redirectResource.create(redirect).then(function () {
                    notificationsService.success("Success", "Redirect created successfully");
                    vm.closeModal();
                    vm.loadRedirects();
                }, function (error) {
                    notificationsService.error("Error", error.data || "Failed to create redirect");
                    model.submitButtonState = "error";
                });
            }
        };
```

Replace with:
```javascript
            if (redirect.id) {
                redirectResource.update(redirect.id, redirect).then(function (response) {
                    notificationsService.success("Success", "Redirect updated successfully");
                    vm.notifyOverlapWarnings(response.data);
                    vm.closeModal();
                    vm.loadRedirects();
                }, function (error) {
                    notificationsService.error("Error", error.data || "Failed to update redirect");
                    model.submitButtonState = "error";
                });
            } else {
                redirectResource.create(redirect).then(function (response) {
                    notificationsService.success("Success", "Redirect created successfully");
                    vm.notifyOverlapWarnings(response.data);
                    vm.closeModal();
                    vm.loadRedirects();
                }, function (error) {
                    notificationsService.error("Error", error.data || "Failed to create redirect");
                    model.submitButtonState = "error";
                });
            }
        };

        vm.notifyOverlapWarnings = function (saved) {
            if (saved && saved.overlapWarnings && saved.overlapWarnings.length > 0) {
                notificationsService.warning(
                    "Overlap warning",
                    "This rule also matches existing active rule(s): " + saved.overlapWarnings.join(", ")
                );
            }
        };
```

- [ ] **Step 2: Manual verification (deferred — same batch-wide pass as Task 4)**

Checklist to run later: same scenario as Task 4's manual check, but in the AngularJS dashboard — confirm the success notification and a separate amber "Overlap warning" notification both appear (Umbraco's `notificationsService` stacks them), and that saving a non-overlapping rule shows only the success notification as before.

- [ ] **Step 3: Commit**

```bash
git add App_Plugins/RedirectManager/redirect.controller.js
git commit -m "$(cat <<'EOF'
feat: show overlap warning notification in the AngularJS dashboard

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Run the full test suite and confirm the main package still builds

**Files:** none

- [ ] **Step 1: Run the entire test suite**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj
```

Expected: 43 total tests pass (the 37 from the previous sub-project plus the 6 new overlap-warning tests from Task 3), `Passed!` summary, 0 failed.

If any test fails, read the actual failure message and stack trace — do not weaken or delete a failing assertion to make it pass. If a test failure reveals a genuine bug in already-shipped production code unrelated to this sub-project, STOP and report BLOCKED rather than silently patching it.

- [ ] **Step 2: Confirm the main package still builds cleanly on both TFMs (final sanity check)**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: No commit needed for this task** — it's a verification-only task with no file changes.

---

## Out of scope for this plan

- Exact-rule-falls-under-existing-broad-rule warnings (the reverse direction).
- Regex-vs-regex or wildcard-vs-wildcard overlap detection.
- Any warning surfaced outside of the Create/Update save flow (no standing overlap report, no background scan).
- Blocking/preventing the save in any way.
- CSV import's duplicate/overlap behavior.
- Any appsettings-level configurability (no toggle, no configurable cap — hardcoded at 5).
- Version bump, git tag, and NuGet publish — happens once, after all 9 sub-projects in this batch are done, as a separate step outside this plan.
