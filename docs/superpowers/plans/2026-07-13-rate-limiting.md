# Rate Limiting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in, per-IP rate limiter for redirect-matching requests: once a client IP exceeds a configurable number of redirect requests within a time window, either log a warning (default) or return `429 Too Many Requests` (opt-in), controlled entirely via a new `RedirectManager:RateLimit` appsettings section. Disabled by default — zero behavior change unless an admin explicitly configures it.

**Architecture:** A new `RedirectRateLimitOptions`/`RateLimitMode` pair in `Models/`, bound via `IOptions<T>` (same pattern as the existing `RedirectBackupOptions`). A new singleton `IRedirectRateLimiter`/`RedirectRateLimiter` in `Services/` — a pure, in-memory, per-IP fixed-window counter (`ConcurrentDictionary`, no locks, no DB), deterministically unit-testable since it takes `utcNow` as a parameter instead of reading the clock internally. `RedirectMiddleware` gains two new constructor dependencies and a `TryApplyRateLimit` helper called once at the top of each of its three match-confirmed blocks (exact/wildcard/regex), before any existing hit-tracker call, so blocked requests never pollute hit-count stats.

**Tech Stack:** Same as the rest of the package — `Microsoft.Extensions.Options` for config binding, `System.Collections.Concurrent.ConcurrentDictionary` for the counter, xUnit + NSubstitute for tests (the rate limiter itself gets pure, mock-free unit tests; the middleware wiring gets NSubstitute-mocked tests consistent with the existing `RedirectMiddlewareTests`).

Reference spec: `docs/superpowers/specs/2026-07-13-rate-limiting-design.md`

This is sub-project 8 of 9 in the current roadmap batch. No version bump/release happens here — that is a separate step once all 9 sub-projects are done. There is no dashboard UI change in this sub-project (backend-only protective/logging feature, per the spec).

**Note on manual verification:** the actual rate-limit/429 behavior under real traffic can't be meaningfully exercised without a live Umbraco site under load — that check is deferred to the project-wide manual verification pass after all 9 sub-projects are done, same as prior sub-projects' deferred dashboard checks.

---

### Task 1: `RedirectRateLimitOptions` + `IRedirectRateLimiter`/`RedirectRateLimiter` (with unit tests)

**Files:**
- Create: `Models/RedirectRateLimitOptions.cs`
- Create: `Services/IRedirectRateLimiter.cs`
- Create: `Services/RedirectRateLimiter.cs`
- Create: `Umbraco.RedirectManager.Tests/Services/RedirectRateLimiterTests.cs`

- [ ] **Step 1: Create the options class**

```csharp
namespace Umbraco.RedirectManager.Models;

public class RedirectRateLimitOptions
{
    public bool Enabled { get; set; } = false;
    public int MaxRequestsPerWindow { get; set; } = 30;
    public int WindowSeconds { get; set; } = 60;
    public RateLimitMode Mode { get; set; } = RateLimitMode.LogOnly;
}

public enum RateLimitMode
{
    LogOnly,
    Block
}
```

- [ ] **Step 2: Create the service interface**

```csharp
namespace Umbraco.RedirectManager.Services;

public interface IRedirectRateLimiter
{
    // Records a redirect-matching request from this client IP at utcNow, and
    // returns whether it pushes that IP's current fixed window over the
    // configured MaxRequestsPerWindow threshold. utcNow is a parameter
    // (rather than read internally) so callers -- including tests -- have
    // full, deterministic control over window timing.
    bool ShouldRateLimit(string clientIp, DateTime utcNow);
}
```

- [ ] **Step 3: Create the implementation**

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectRateLimiter : IRedirectRateLimiter
{
    private readonly IOptions<RedirectRateLimitOptions> _options;
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new();

    public RedirectRateLimiter(IOptions<RedirectRateLimitOptions> options)
    {
        _options = options;
    }

    public bool ShouldRateLimit(string clientIp, DateTime utcNow)
    {
        var windowSeconds = _options.Value.WindowSeconds;
        var maxRequests = _options.Value.MaxRequestsPerWindow;

        var counter = _counters.AddOrUpdate(
            clientIp,
            _ => new WindowCounter(utcNow, 1),
            (_, existing) =>
            {
                if ((utcNow - existing.WindowStart).TotalSeconds >= windowSeconds)
                    return new WindowCounter(utcNow, 1);

                return new WindowCounter(existing.WindowStart, existing.Count + 1);
            });

        return counter.Count > maxRequests;
    }

    private sealed record WindowCounter(DateTime WindowStart, int Count);
}
```

- [ ] **Step 4: Write the unit tests**

```csharp
using Microsoft.Extensions.Options;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class RedirectRateLimiterTests
{
    private static RedirectRateLimiter CreateLimiter(int maxRequestsPerWindow = 3, int windowSeconds = 60)
    {
        return new RedirectRateLimiter(Options.Create(new RedirectRateLimitOptions
        {
            MaxRequestsPerWindow = maxRequestsPerWindow,
            WindowSeconds = windowSeconds
        }));
    }

    [Fact]
    public void ShouldRateLimit_FirstRequest_ReturnsFalse()
    {
        var limiter = CreateLimiter();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(limiter.ShouldRateLimit("1.2.3.4", now));
    }

    [Fact]
    public void ShouldRateLimit_ExactlyAtMax_ReturnsFalse()
    {
        var limiter = CreateLimiter(maxRequestsPerWindow: 3);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(limiter.ShouldRateLimit("1.2.3.4", now));
        Assert.False(limiter.ShouldRateLimit("1.2.3.4", now));
        Assert.False(limiter.ShouldRateLimit("1.2.3.4", now));
    }

    [Fact]
    public void ShouldRateLimit_ExceedsMax_ReturnsTrue()
    {
        var limiter = CreateLimiter(maxRequestsPerWindow: 3);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        limiter.ShouldRateLimit("1.2.3.4", now);
        limiter.ShouldRateLimit("1.2.3.4", now);
        limiter.ShouldRateLimit("1.2.3.4", now);

        Assert.True(limiter.ShouldRateLimit("1.2.3.4", now));
    }

    [Fact]
    public void ShouldRateLimit_WindowExpired_ResetsCounter()
    {
        var limiter = CreateLimiter(maxRequestsPerWindow: 1, windowSeconds: 60);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(limiter.ShouldRateLimit("1.2.3.4", start));
        Assert.True(limiter.ShouldRateLimit("1.2.3.4", start.AddSeconds(10)));

        var afterWindow = start.AddSeconds(61);
        Assert.False(limiter.ShouldRateLimit("1.2.3.4", afterWindow));
    }

    [Fact]
    public void ShouldRateLimit_DifferentIps_TrackedIndependently()
    {
        var limiter = CreateLimiter(maxRequestsPerWindow: 1);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(limiter.ShouldRateLimit("1.1.1.1", now));
        Assert.True(limiter.ShouldRateLimit("1.1.1.1", now));

        Assert.False(limiter.ShouldRateLimit("2.2.2.2", now));
    }
}
```

- [ ] **Step 5: Run the new test file to confirm all 5 pass**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~RedirectRateLimiterTests"
```

Expected: 5 tests pass.

- [ ] **Step 6: Build to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 7: Commit**

```bash
git add Models/RedirectRateLimitOptions.cs Services/IRedirectRateLimiter.cs Services/RedirectRateLimiter.cs Umbraco.RedirectManager.Tests/Services/RedirectRateLimiterTests.cs
git commit -m "$(cat <<'EOF'
feat: add RedirectRateLimiter, a per-IP fixed-window rate limiter

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Wire the rate limiter into `RedirectMiddleware` and the composer

**Files:**
- Modify: `Middleware/RedirectMiddleware.cs`
- Modify: `Composers/RedirectManagerComposer.cs`

- [ ] **Step 1: Add usings, fields, and constructor parameters**

Current (`Middleware/RedirectMiddleware.cs`, top of file):
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Middleware;

public class RedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RedirectMiddleware> _logger;
    private readonly IRedirectHitTracker _hitTracker;
    private readonly IVariantBHitTracker _variantBHitTracker;
    private readonly IMissedRequestTracker _missedRequestTracker;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly ConcurrentDictionary<string, Regex> WildcardRegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectMiddleware(
        RequestDelegate next,
        ILogger<RedirectMiddleware> logger,
        IRedirectHitTracker hitTracker,
        IVariantBHitTracker variantBHitTracker,
        IMissedRequestTracker missedRequestTracker)
    {
        _next = next;
        _logger = logger;
        _hitTracker = hitTracker;
        _variantBHitTracker = variantBHitTracker;
        _missedRequestTracker = missedRequestTracker;
    }
```

Replace with:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Middleware;

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

- [ ] **Step 2: Insert the rate-limit check at the top of the exact-match block**

Current:
```csharp
        if (redirect != null && redirect.IsActive)
        {
            _logger.LogDebug("Redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                redirect.OldUrl, redirect.NewUrl, redirect.StatusCode);

            switch (redirect.StatusCode)
            {
```

Replace with:
```csharp
        if (redirect != null && redirect.IsActive)
        {
            if (TryApplyRateLimit(context))
                return;

            _logger.LogDebug("Redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                redirect.OldUrl, redirect.NewUrl, redirect.StatusCode);

            switch (redirect.StatusCode)
            {
```

- [ ] **Step 3: Insert the rate-limit check at the top of the wildcard-match block**

Current:
```csharp
        var wildcardRedirect = FindWildcardRedirect(path, domain, redirectService);
        if (wildcardRedirect != null)
        {
            _logger.LogDebug("Wildcard redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                wildcardRedirect.Entry.OldUrl, wildcardRedirect.ComputedNewUrl, wildcardRedirect.Entry.StatusCode);
            _hitTracker.RecordHit(wildcardRedirect.Entry.Id);

            switch (wildcardRedirect.Entry.StatusCode)
            {
```

Replace with:
```csharp
        var wildcardRedirect = FindWildcardRedirect(path, domain, redirectService);
        if (wildcardRedirect != null)
        {
            if (TryApplyRateLimit(context))
                return;

            _logger.LogDebug("Wildcard redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                wildcardRedirect.Entry.OldUrl, wildcardRedirect.ComputedNewUrl, wildcardRedirect.Entry.StatusCode);
            _hitTracker.RecordHit(wildcardRedirect.Entry.Id);

            switch (wildcardRedirect.Entry.StatusCode)
            {
```

- [ ] **Step 4: Insert the rate-limit check at the top of the regex-match block**

Current:
```csharp
        var regexRedirect = FindRegexRedirect(path, domain, redirectService);
        if (regexRedirect != null)
        {
            _logger.LogDebug("Regex redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                regexRedirect.Entry.OldUrl, regexRedirect.ComputedNewUrl, regexRedirect.Entry.StatusCode);
            _hitTracker.RecordHit(regexRedirect.Entry.Id);

            switch (regexRedirect.Entry.StatusCode)
            {
```

Replace with:
```csharp
        var regexRedirect = FindRegexRedirect(path, domain, redirectService);
        if (regexRedirect != null)
        {
            if (TryApplyRateLimit(context))
                return;

            _logger.LogDebug("Regex redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                regexRedirect.Entry.OldUrl, regexRedirect.ComputedNewUrl, regexRedirect.Entry.StatusCode);
            _hitTracker.RecordHit(regexRedirect.Entry.Id);

            switch (regexRedirect.Entry.StatusCode)
            {
```

- [ ] **Step 5: Add the `TryApplyRateLimit` helper, right after `InvokeAsync`**

Current:
```csharp
        await _next(context);

        if (context.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            _missedRequestTracker.RecordMiss(path);
        }
    }

    // A/B test resolution for an exact-match 301/302 rule. Not applied to
```

Replace with:
```csharp
        await _next(context);

        if (context.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            _missedRequestTracker.RecordMiss(path);
        }
    }

    // Returns true (and writes a 429 response) when this matched-redirect
    // request pushes its client IP over the configured threshold in Block
    // mode. In LogOnly mode (the default once enabled), it only logs a
    // warning and always returns false, so the redirect is still served
    // normally. Disabled entirely (the overall default) unless configured.
    private bool TryApplyRateLimit(HttpContext context)
    {
        if (!_rateLimitOptions.Value.Enabled)
            return false;

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_rateLimiter.ShouldRateLimit(clientIp, DateTime.UtcNow))
            return false;

        if (_rateLimitOptions.Value.Mode == RateLimitMode.Block)
        {
            context.Response.StatusCode = 429;
            context.Response.Headers["Retry-After"] = _rateLimitOptions.Value.WindowSeconds.ToString();
            return true;
        }

        _logger.LogWarning(
            "Redirect rate limit exceeded for {ClientIp} (more than {MaxRequestsPerWindow} redirect requests in {WindowSeconds}s)",
            clientIp, _rateLimitOptions.Value.MaxRequestsPerWindow, _rateLimitOptions.Value.WindowSeconds);
        return false;
    }

    // A/B test resolution for an exact-match 301/302 rule. Not applied to
```

- [ ] **Step 6: Register the new options and service in the composer**

Current (`Composers/RedirectManagerComposer.cs`):
```csharp
        builder.Services.AddSingleton<IVariantBHitTracker, VariantBHitTracker>();
        builder.Services.AddHostedService<VariantBHitFlushService>();

        builder.Services.AddHttpClient();
```

Replace with:
```csharp
        builder.Services.AddSingleton<IVariantBHitTracker, VariantBHitTracker>();
        builder.Services.AddHostedService<VariantBHitFlushService>();

        builder.Services.Configure<RedirectRateLimitOptions>(builder.Config.GetSection("RedirectManager:RateLimit"));
        builder.Services.AddSingleton<IRedirectRateLimiter, RedirectRateLimiter>();

        builder.Services.AddHttpClient();
```

- [ ] **Step 7: Build to confirm both TFMs still compile**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`. (The existing `Umbraco.RedirectManager.Tests` project will fail to build at this point, since `RedirectMiddlewareTests.cs`'s `CreateMiddleware` helper doesn't yet pass the two new constructor arguments — that's expected and fixed in Task 3. Build only the main csproj in this step, not the test project.)

- [ ] **Step 8: Commit**

```bash
git add Middleware/RedirectMiddleware.cs Composers/RedirectManagerComposer.cs
git commit -m "$(cat <<'EOF'
feat: wire per-IP rate limiting into RedirectMiddleware's match tiers

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Update `RedirectMiddlewareTests` for the new constructor parameters, add rate-limit test coverage

**Files:**
- Modify: `Umbraco.RedirectManager.Tests/Middleware/RedirectMiddlewareTests.cs`

- [ ] **Step 1: Add the missing usings and update `CreateMiddleware`'s signature**

Current (top of file):
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
```

Replace with:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

`RedirectRateLimitOptions`'s own default (`Enabled = false`) means every one of the 10 pre-existing tests in this file continues to pass unmodified — rate limiting never engages unless a test explicitly opts in via the new optional parameters.

- [ ] **Step 2: Append these five new tests, right after the existing `InvokeAsync_NothingMatches_AndDownstreamNot404_DoesNotRecordMissedRequest` test, before the closing `}` of the class**

```csharp
    [Fact]
    public async Task InvokeAsync_RateLimitDisabled_NeverConsultsLimiterEvenIfWouldBlock()
    {
        var rateLimiter = Substitute.For<IRedirectRateLimiter>();
        rateLimiter.ShouldRateLimit(Arg.Any<string>(), Arg.Any<DateTime>()).Returns(true);
        var middleware = CreateMiddleware(
            rateLimitOptions: new RedirectRateLimitOptions { Enabled = false },
            rateLimiter: rateLimiter);
        var redirectService = Substitute.For<IRedirectService>();
        var rule = ExactRule("/old-page", "/new-page", statusCode: 301);
        redirectService.GetByOldUrl("/old-page", Arg.Any<string?>()).Returns(rule);
        var context = CreateContext("/old-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        rateLimiter.DidNotReceive().ShouldRateLimit(Arg.Any<string>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task InvokeAsync_RateLimitEnabledBlockMode_OverLimit_Returns429WithRetryAfter()
    {
        var rateLimiter = Substitute.For<IRedirectRateLimiter>();
        rateLimiter.ShouldRateLimit(Arg.Any<string>(), Arg.Any<DateTime>()).Returns(true);
        var hitTracker = Substitute.For<IRedirectHitTracker>();
        var middleware = CreateMiddleware(
            hitTracker: hitTracker,
            rateLimitOptions: new RedirectRateLimitOptions { Enabled = true, Mode = RateLimitMode.Block, WindowSeconds = 60 },
            rateLimiter: rateLimiter);
        var redirectService = Substitute.For<IRedirectService>();
        var rule = ExactRule("/gone-page", null, statusCode: 410);
        redirectService.GetByOldUrl("/gone-page", Arg.Any<string?>()).Returns(rule);
        var context = CreateContext("/gone-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("60", context.Response.Headers["Retry-After"].ToString());
        hitTracker.DidNotReceive().RecordHit(Arg.Any<int>());
    }

    [Fact]
    public async Task InvokeAsync_RateLimitEnabledLogOnlyMode_OverLimit_StillRedirectsNormally()
    {
        var rateLimiter = Substitute.For<IRedirectRateLimiter>();
        rateLimiter.ShouldRateLimit(Arg.Any<string>(), Arg.Any<DateTime>()).Returns(true);
        var middleware = CreateMiddleware(
            rateLimitOptions: new RedirectRateLimitOptions { Enabled = true, Mode = RateLimitMode.LogOnly },
            rateLimiter: rateLimiter);
        var redirectService = Substitute.For<IRedirectService>();
        var rule = ExactRule("/old-page", "/new-page", statusCode: 301);
        redirectService.GetByOldUrl("/old-page", Arg.Any<string?>()).Returns(rule);
        var context = CreateContext("/old-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/new-page", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_RateLimitEnabledUnderLimit_RedirectsNormallyAndConsultsLimiter()
    {
        var rateLimiter = Substitute.For<IRedirectRateLimiter>();
        rateLimiter.ShouldRateLimit(Arg.Any<string>(), Arg.Any<DateTime>()).Returns(false);
        var middleware = CreateMiddleware(
            rateLimitOptions: new RedirectRateLimitOptions { Enabled = true },
            rateLimiter: rateLimiter);
        var redirectService = Substitute.For<IRedirectService>();
        var rule = ExactRule("/old-page", "/new-page", statusCode: 301);
        redirectService.GetByOldUrl("/old-page", Arg.Any<string?>()).Returns(rule);
        var context = CreateContext("/old-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        rateLimiter.Received(1).ShouldRateLimit(Arg.Any<string>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task InvokeAsync_NoMatch_NeverConsultsRateLimiter()
    {
        var rateLimiter = Substitute.For<IRedirectRateLimiter>();
        var middleware = CreateMiddleware(
            rateLimitOptions: new RedirectRateLimitOptions { Enabled = true },
            rateLimiter: rateLimiter);
        var redirectService = Substitute.For<IRedirectService>();
        redirectService.GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>()).Returns((RedirectEntry?)null);
        redirectService.GetActiveWildcardEntries().Returns(Array.Empty<RedirectEntry>());
        redirectService.GetActiveRegexEntries().Returns(Array.Empty<RedirectEntry>());
        var context = CreateContext("/does-not-exist");

        await middleware.InvokeAsync(context, redirectService);

        rateLimiter.DidNotReceive().ShouldRateLimit(Arg.Any<string>(), Arg.Any<DateTime>());
    }
```

- [ ] **Step 3: Run the full middleware test class to confirm all 15 tests pass (10 pre-existing + 5 new)**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj --filter "FullyQualifiedName~RedirectMiddlewareTests"
```

Expected: 15 tests pass.

- [ ] **Step 4: Commit**

```bash
git add Umbraco.RedirectManager.Tests/Middleware/RedirectMiddlewareTests.cs
git commit -m "$(cat <<'EOF'
test: add rate-limit coverage to RedirectMiddlewareTests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Run the full test suite and confirm the main package still builds

**Files:** none

- [ ] **Step 1: Run the entire test suite**

```bash
dotnet test Umbraco.RedirectManager.Tests/Umbraco.RedirectManager.Tests.csproj
```

Expected: 53 total tests pass (43 from before this sub-project + 5 new `RedirectRateLimiterTests` + 5 new `RedirectMiddlewareTests`), `Passed!` summary, 0 failed.

If any test fails, read the actual failure message and stack trace — do not weaken or delete a failing assertion to make it pass. If a test failure reveals a genuine bug in already-shipped production code unrelated to this sub-project, STOP and report BLOCKED rather than silently patching it.

- [ ] **Step 2: Confirm the main package still builds cleanly on both TFMs (final sanity check)**

```bash
dotnet build Umbraco.RedirectManager.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors on both `net8.0` and `net10.0`.

- [ ] **Step 3: No commit needed for this task** — it's a verification-only task with no file changes.

---

## Out of scope for this plan

- Distributed/shared rate-limiting state across multiple app instances.
- Any dashboard UI surfacing rate-limit activity.
- Rate limiting genuinely-missed (404 passthrough) requests.
- Rate limiting skip-listed paths.
- Per-rule rate limiting (separate counter per `RedirectEntry.Id`).
- Sliding-window or token-bucket algorithms.
- Version bump, git tag, and NuGet publish — happens once, after all 9 sub-projects in this batch are done, as a separate step outside this plan.
