# Unit / Integration Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the first automated test project for this package, covering `WildcardPatternBuilder`, `DomainNormalizer`, `RedirectMiddleware`'s matching logic, and `RedirectApiController`'s `Create`/`Update` validation and duplicate-check behavior — all with `IRedirectService` and other dependencies substituted, no real database involved.

**Architecture:** A new sibling project, `Umbraco.RedirectManager.Tests`, targeting `net10.0` only (test projects aren't packed/shipped, so no need to multi-target), referencing the main project directly. xUnit for the test runner/assertions, NSubstitute for faking interface dependencies (`IRedirectService`, `IRedirectHitTracker`, `IBackOfficeSecurityAccessor`, etc.). Pure-logic classes (`WildcardPatternBuilder`, `DomainNormalizer`) are tested directly with no fakes needed. `RedirectMiddleware` is tested by constructing it with fake tracker dependencies and a real `DefaultHttpContext`, passing a fake `IRedirectService` into `InvokeAsync` per test. `RedirectApiController` is tested by constructing it with all six dependencies faked, configuring only `IRedirectService` per test.

**Tech Stack:** xUnit, NSubstitute, `Microsoft.AspNetCore.Http.DefaultHttpContext` (already available transitively via the main project's `FrameworkReference` to `Microsoft.AspNetCore.App`), `Microsoft.Extensions.Logging.Abstractions.NullLogger<T>`.

Reference spec: `docs/superpowers/specs/2026-07-11-unit-integration-tests-design.md`

This is sub-project 6 of 9 in the current roadmap batch. No version bump/release happens here — that is a separate step once all 9 sub-projects are done.

**Note on manual verification:** unlike every prior sub-project in this repo, this one does NOT need a live Umbraco test site to verify — `dotnet test` runs the whole suite for real, right now, in this environment. Task 6 below is a genuine, executed verification step, not a deferred one.

---

### Task 1: Scaffold the `Umbraco.RedirectManager.Tests` project

**Files:**
- Create: `Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj`
- Create: `Umbraco.RedirectManager.Tests/UnitTest1.cs` (the `dotnet new xunit` template's default file — deleted in this same task, replaced by the real test files in later tasks)

- [ ] **Step 1: Scaffold the project via the xUnit template, from the repo root**

```bash
dotnet new xunit -n Umbraco.RedirectManager.Tests -o Umbraco.RedirectManager.Tests
```

Expected: creates `Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj` and a default `UnitTest1.cs` with current, compatible xUnit/`Microsoft.NET.Test.Sdk`/`coverlet.collector` package references (using the template rather than hand-pinning package versions avoids referencing a version that may no longer resolve).

- [ ] **Step 2: Delete the template's placeholder test file**

```bash
rm Umbraco.RedirectManager.Tests/UnitTest1.cs
```

- [ ] **Step 3: Add the NSubstitute package reference**

```bash
dotnet add Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj package NSubstitute
```

Expected: adds a current, resolvable `NSubstitute` version to the csproj.

- [ ] **Step 4: Add a project reference to the main package**

```bash
dotnet add Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj reference Umbraco.RedirectManager.csproj
```

- [ ] **Step 5: Confirm/set the target framework to `net10.0` only**

Open `Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj` and confirm `<TargetFramework>net10.0</TargetFramework>` (singular element, one TFM) is present. The `dotnet new xunit` template typically defaults to the newest installed SDK's TFM already (this environment has the net10.0 SDK installed, confirmed earlier in this project's history), but if the scaffolded file instead shows a different single TFM (e.g. `net9.0` or `net8.0`), change the `<TargetFramework>` value to `net10.0` to match the design spec's decision (a single, current TFM — no multi-targeting needed for a test-only project).

- [ ] **Step 6: Build to confirm the empty test project compiles and references the main project correctly**

```bash
dotnet build Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj
```

Expected: `Build succeeded.` with 0 errors (this pulls in the main `Umbraco.RedirectManager` project's `net10.0` build as a dependency, which itself must still build cleanly — if this step fails on errors originating in the main project rather than the test project, STOP and report BLOCKED, since that would indicate the main project itself is broken, not a problem with this task).

- [ ] **Step 7: Confirm the main package's own build is still unaffected**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`, unchanged from before this task (adding a new sibling test project must not affect the main project's own build).

- [ ] **Step 8: Commit**

```bash
git add Umbraco.RedirectManager.Tests/
git commit -m "$(cat <<'EOF'
chore: scaffold Umbraco.RedirectManager.Tests (xUnit + NSubstitute)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `WildcardPatternBuilderTests`

**Files:**
- Create: `Umbraco.RedirectManager.Tests/Services/WildcardPatternBuilderTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System.Text.RegularExpressions;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class WildcardPatternBuilderTests
{
    [Theory]
    [InlineData("/blog/hello-world", true)]
    [InlineData("/blog/", true)]
    [InlineData("/blogx/hello", false)]
    public void BuildRegexPattern_SingleWildcard_MatchesExpectedPaths(string path, bool expectedMatch)
    {
        var pattern = WildcardPatternBuilder.BuildRegexPattern("/blog/*");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        Assert.Equal(expectedMatch, regex.IsMatch(path));
    }

    [Fact]
    public void BuildRegexPattern_EscapesLiteralRegexMetacharacters()
    {
        var pattern = WildcardPatternBuilder.BuildRegexPattern("/a.b/*/c+d");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        Assert.True(regex.IsMatch("/a.b/xyz/c+d"));
        Assert.False(regex.IsMatch("/aXb/xyz/c+d")); // '.' must not act as "any character"
        Assert.False(regex.IsMatch("/a.b/xyz/cd"));  // '+' must not act as a quantifier
    }

    [Fact]
    public void BuildRegexPattern_NoWildcard_FallsBackToLiteralExactMatch()
    {
        var pattern = WildcardPatternBuilder.BuildRegexPattern("/exact/path");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        Assert.True(regex.IsMatch("/exact/path"));
        Assert.False(regex.IsMatch("/exact/path/extra"));
    }

    [Fact]
    public void BuildRegexPattern_CapturesTheWildcardSegment()
    {
        var pattern = WildcardPatternBuilder.BuildRegexPattern("/blog/*");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        var match = regex.Match("/blog/hello-world");

        Assert.True(match.Success);
        Assert.Equal("hello-world", match.Groups[1].Value);
    }
}
```

- [ ] **Step 2: Run just this test file's tests to confirm they pass**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~WildcardPatternBuilderTests"
```

Expected: all tests in this class pass (look for `Passed!` and a count matching the 6 test cases: 3 from the `[Theory]`'s `InlineData` rows, plus 3 `[Fact]`s).

- [ ] **Step 3: Commit**

```bash
git add Umbraco.RedirectManager.Tests/Services/WildcardPatternBuilderTests.cs
git commit -m "$(cat <<'EOF'
test: add WildcardPatternBuilder unit tests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `DomainNormalizerTests`

**Files:**
- Create: `Umbraco.RedirectManager.Tests/Services/DomainNormalizerTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class DomainNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NullOrWhitespace_ReturnsNull(string? input)
    {
        Assert.Null(DomainNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_LowercasesAndTrims()
    {
        Assert.Equal("example.com", DomainNormalizer.Normalize("  Example.COM  "));
    }

    [Fact]
    public void Normalize_StripsTrailingPort()
    {
        Assert.Equal("example.com", DomainNormalizer.Normalize("example.com:8080"));
    }

    [Fact]
    public void Normalize_StripsBareTrailingColon()
    {
        Assert.Equal("example.com", DomainNormalizer.Normalize("example.com:"));
    }

    [Fact]
    public void Normalize_BareIPv6Literal_IsNotCorruptedByInternalColons()
    {
        // No trailing port here -- the last ':' is inside the brackets, part
        // of the address itself. The guard must recognize this and leave the
        // value untouched rather than truncating at that internal colon.
        Assert.Equal("[::1]", DomainNormalizer.Normalize("[::1]"));
    }

    [Fact]
    public void Normalize_IPv6LiteralWithPort_StripsOnlyThePort()
    {
        // Here the last ':' genuinely is a port separator (it comes after the
        // closing ']'), so it should be stripped, same as any other host:port.
        Assert.Equal("[::1]", DomainNormalizer.Normalize("[::1]:8080"));
    }

    [Fact]
    public void Normalize_DoesNotStripWwwPrefix()
    {
        Assert.Equal("www.example.com", DomainNormalizer.Normalize("www.example.com"));
    }
}
```

- [ ] **Step 2: Run just this test file's tests to confirm they pass**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~DomainNormalizerTests"
```

Expected: all tests pass (3 `InlineData` rows plus 6 `[Fact]`s = 9 total).

- [ ] **Step 3: Commit**

```bash
git add Umbraco.RedirectManager.Tests/Services/DomainNormalizerTests.cs
git commit -m "$(cat <<'EOF'
test: add DomainNormalizer unit tests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `RedirectMiddlewareTests`

**Files:**
- Create: `Umbraco.RedirectManager.Tests/Middleware/RedirectMiddlewareTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.RedirectManager.Middleware;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Middleware;

public class RedirectMiddlewareTests
{
    private static RedirectMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        IRedirectHitTracker? hitTracker = null,
        IVariantBHitTracker? variantBHitTracker = null,
        IMissedRequestTracker? missedRequestTracker = null)
    {
        return new RedirectMiddleware(
            next ?? (_ => Task.CompletedTask),
            NullLogger<RedirectMiddleware>.Instance,
            hitTracker ?? Substitute.For<IRedirectHitTracker>(),
            variantBHitTracker ?? Substitute.For<IVariantBHitTracker>(),
            missedRequestTracker ?? Substitute.For<IMissedRequestTracker>());
    }

    private static DefaultHttpContext CreateContext(string path, string? queryString = null, string host = "example.com")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Host = new HostString(host);
        if (queryString != null)
        {
            context.Request.QueryString = new QueryString(queryString);
        }
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static RedirectEntry ExactRule(string oldUrl, string? newUrl, int statusCode = 301, bool isActive = true, bool preserveQueryString = false) =>
        new()
        {
            Id = 1,
            OldUrl = oldUrl,
            NewUrl = newUrl,
            StatusCode = statusCode,
            IsActive = isActive,
            IsRegex = false,
            PreserveQueryString = preserveQueryString
        };

    [Fact]
    public async Task InvokeAsync_SkipPath_CallsNextAndNeverQueriesRedirectService()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var redirectService = Substitute.For<IRedirectService>();
        var context = CreateContext("/umbraco/backoffice/something");

        await middleware.InvokeAsync(context, redirectService);

        Assert.True(nextCalled);
        redirectService.DidNotReceive().GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task InvokeAsync_ExactActive301Rule_SetsStatusAndLocationHeader()
    {
        var middleware = CreateMiddleware();
        var redirectService = Substitute.For<IRedirectService>();
        var rule = ExactRule("/old-page", "/new-page", statusCode: 301);
        redirectService.GetByOldUrl("/old-page", Arg.Any<string?>()).Returns(rule);
        var context = CreateContext("/old-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/new-page", context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData(404, "Not Found")]
    [InlineData(410, "Gone")]
    public async Task InvokeAsync_ExactActiveTerminalRule_SetsStatusAndBody(int statusCode, string expectedBody)
    {
        var hitTracker = Substitute.For<IRedirectHitTracker>();
        var middleware = CreateMiddleware(hitTracker: hitTracker);
        var redirectService = Substitute.For<IRedirectService>();
        var rule = ExactRule("/gone-page", null, statusCode: statusCode);
        redirectService.GetByOldUrl("/gone-page", Arg.Any<string?>()).Returns(rule);
        var context = CreateContext("/gone-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(statusCode, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        Assert.Equal(expectedBody, await reader.ReadToEndAsync());
        hitTracker.Received(1).RecordHit(1);
    }

    [Fact]
    public async Task InvokeAsync_PreserveQueryString_AppendsIncomingQueryToLocation()
    {
        var middleware = CreateMiddleware();
        var redirectService = Substitute.For<IRedirectService>();
        var rule = ExactRule("/promo", "/landing?ref=campaign", preserveQueryString: true);
        redirectService.GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>()).Returns(rule);
        var context = CreateContext("/promo", "?utm_source=google");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal("/landing?ref=campaign&utm_source=google", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_TrailingSlashMismatch_StillResolvesAsExactMatch()
    {
        var middleware = CreateMiddleware();
        var redirectService = Substitute.For<IRedirectService>();
        var rule = ExactRule("/sayfa", "/yeni-sayfa");
        redirectService.GetByOldUrl("/sayfa", Arg.Any<string?>()).Returns(rule);
        redirectService.GetByOldUrl("/sayfa/", Arg.Any<string?>()).Returns((RedirectEntry?)null);
        var context = CreateContext("/sayfa/");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/yeni-sayfa", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_NoExactMatch_FallsBackToWildcardMatch()
    {
        var middleware = CreateMiddleware();
        var redirectService = Substitute.For<IRedirectService>();
        redirectService.GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>()).Returns((RedirectEntry?)null);
        var wildcardRule = new RedirectEntry { Id = 2, OldUrl = "/blog/*", NewUrl = "/articles/*", StatusCode = 301, IsActive = true };
        redirectService.GetActiveWildcardEntries().Returns(new[] { wildcardRule });
        redirectService.GetActiveRegexEntries().Returns(Array.Empty<RedirectEntry>());
        var context = CreateContext("/blog/hello-world");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/articles/hello-world", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_NoExactOrWildcardMatch_FallsBackToRegexMatch()
    {
        var middleware = CreateMiddleware();
        var redirectService = Substitute.For<IRedirectService>();
        redirectService.GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>()).Returns((RedirectEntry?)null);
        redirectService.GetActiveWildcardEntries().Returns(Array.Empty<RedirectEntry>());
        var regexRule = new RedirectEntry { Id = 3, OldUrl = "^/archive/(.+)$", NewUrl = "/new-archive/$1", StatusCode = 301, IsActive = true, IsRegex = true };
        redirectService.GetActiveRegexEntries().Returns(new[] { regexRule });
        var context = CreateContext("/archive/2020");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/new-archive/2020", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_NothingMatches_AndDownstream404_RecordsMissedRequest()
    {
        var missedTracker = Substitute.For<IMissedRequestTracker>();
        var middleware = CreateMiddleware(
            next: context => { context.Response.StatusCode = 404; return Task.CompletedTask; },
            missedRequestTracker: missedTracker);
        var redirectService = Substitute.For<IRedirectService>();
        redirectService.GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>()).Returns((RedirectEntry?)null);
        redirectService.GetActiveWildcardEntries().Returns(Array.Empty<RedirectEntry>());
        redirectService.GetActiveRegexEntries().Returns(Array.Empty<RedirectEntry>());
        var context = CreateContext("/does-not-exist");

        await middleware.InvokeAsync(context, redirectService);

        missedTracker.Received(1).RecordMiss("/does-not-exist");
    }

    [Fact]
    public async Task InvokeAsync_NothingMatches_AndDownstreamNot404_DoesNotRecordMissedRequest()
    {
        var missedTracker = Substitute.For<IMissedRequestTracker>();
        var middleware = CreateMiddleware(
            next: context => { context.Response.StatusCode = 200; return Task.CompletedTask; },
            missedRequestTracker: missedTracker);
        var redirectService = Substitute.For<IRedirectService>();
        redirectService.GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>()).Returns((RedirectEntry?)null);
        redirectService.GetActiveWildcardEntries().Returns(Array.Empty<RedirectEntry>());
        redirectService.GetActiveRegexEntries().Returns(Array.Empty<RedirectEntry>());
        var context = CreateContext("/a-real-page");

        await middleware.InvokeAsync(context, redirectService);

        missedTracker.DidNotReceive().RecordMiss(Arg.Any<string>());
    }
}
```

- [ ] **Step 2: Run just this test file's tests to confirm they pass**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~RedirectMiddlewareTests"
```

Expected: all 10 tests pass (1 skip-path, 1 exact-301, 2 from the terminal-status `[Theory]`, 1 preserve-query-string, 1 trailing-slash, 1 wildcard-fallback, 1 regex-fallback, 2 missed-request).

If `InvokeAsync_PreserveQueryString_AppendsIncomingQueryToLocation` fails on the exact expected string, double check whether `QueryString` normalizes the leading `?` differently than expected in this ASP.NET Core version — read the actual failure message rather than guessing, since `AppendPreservedQueryString`'s own logic (trims a leading `?` before re-appending) is already covered by existing, already-shipped behavior and shouldn't need modification; the test asserts on that existing behavior, not new logic.

- [ ] **Step 3: Commit**

```bash
git add Umbraco.RedirectManager.Tests/Middleware/RedirectMiddlewareTests.cs
git commit -m "$(cat <<'EOF'
test: add RedirectMiddleware matching-logic tests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: `RedirectApiControllerTests`

**Files:**
- Create: `Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerTests.cs`

- [ ] **Step 1: Write the test file**

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
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns(new RedirectEntry { Id = 99, OldUrl = dto.OldUrl });

        var result = _controller.Create(dto);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Create_ValidNoDuplicate_ReturnsOkAndCallsCreate()
    {
        var dto = ValidCreateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
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
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns(new RedirectEntry { Id = 99, OldUrl = dto.OldUrl });

        var result = _controller.Update(1, dto);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Update_DuplicateIsTheSameRowBeingEdited_DoesNotReturnConflict()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
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
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns((RedirectEntry?)null);

        var result = _controller.Update(1, dto);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Update_ValidNoDuplicate_ReturnsOkAndCallsUpdate()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var updated = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode };
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns(updated);

        var result = _controller.Update(1, dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.Received(1).Update(1, dto, Arg.Any<string?>());
    }
}
```

- [ ] **Step 2: Run just this test file's tests to confirm they pass**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~RedirectApiControllerTests"
```

Expected: all 12 tests pass.

- [ ] **Step 3: Commit**

```bash
git add Umbraco.RedirectManager.Tests/Controllers/RedirectApiControllerTests.cs
git commit -m "$(cat <<'EOF'
test: add RedirectApiController Create/Update validation and duplicate-check tests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Run the full test suite and confirm the main package still builds

This is a REAL, executed verification step (not deferred) — unlike every prior sub-project in this repo, this one produces something that can actually be run right now, with no live Umbraco site needed.

**Files:** none

- [ ] **Step 1: Run the entire test suite**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj
```

Expected: all tests across all four test classes pass — 6 (`WildcardPatternBuilderTests`) + 9 (`DomainNormalizerTests`) + 10 (`RedirectMiddlewareTests`) + 12 (`RedirectApiControllerTests`) = 37 total tests, `Passed!` summary, 0 failed.

If any test fails, read the actual failure message and stack trace — do not weaken or delete a failing assertion to make it pass. If a test's expectation turns out to be wrong (e.g. a misunderstanding of exact middleware/controller behavior found in an earlier task), fix the test to correctly reflect the actual, already-shipped, already-reviewed production behavior it's meant to characterize. If a test failure instead reveals a genuine bug in already-shipped production code, STOP and report BLOCKED — fixing a production bug is out of scope for this test-writing sub-project and needs a decision from the user, not a silent fix bundled into a test commit.

- [ ] **Step 2: Confirm the main package still builds cleanly on both TFMs (final sanity check)**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: No commit needed for this task** — it's a verification-only task with no file changes. If Step 1 required fixing an incorrect test assertion, commit that fix as part of whichever earlier task's test file it belongs to (amend is not appropriate per this repo's git conventions — make a new, small follow-up commit instead, e.g. `test: fix incorrect assertion in RedirectMiddlewareTests`).

---

## Out of scope for this plan

- `RedirectService`'s own database-backed methods — no SQLite/in-memory DB test harness in this sub-project.
- CSV import/export parsing (`ImportCsv`, `ParseCsvLine`, `GetCol`, `RedirectCsvWriter`).
- The A/B-test cookie-assignment random-selection path in `RedirectMiddleware.ResolveRedirectTarget`.
- The A/B-test-specific validation branches in `RedirectApiController.ValidateRedirect` (Variant B URL/weight validation, regex+A/B incompatibility).
- Any CI/CD pipeline wiring to run these tests automatically on push/PR.
- Any appsettings-level configurability.
- Version bump, git tag, and NuGet publish — happens once, after all 9 sub-projects in this batch are done, as a separate step outside this plan.
