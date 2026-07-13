using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
        IRedirectRateLimiter? rateLimiter = null,
        IRedirectCultureResolver? cultureResolver = null)
    {
        // NSubstitute returns string.Empty (not null) from an unconfigured
        // member with a string? return type, so the default resolver is
        // explicitly configured to return null -- matching the "no culture
        // resolved" behavior every pre-existing test in this file implicitly
        // relies on.
        var resolver = cultureResolver ?? Substitute.For<IRedirectCultureResolver>();
        if (cultureResolver == null)
        {
            resolver.ResolveCultureAsync(Arg.Any<string?>()).Returns(Task.FromResult<string?>(null));
        }

        return new RedirectMiddleware(
            next ?? (_ => Task.CompletedTask),
            NullLogger<RedirectMiddleware>.Instance,
            hitTracker ?? Substitute.For<IRedirectHitTracker>(),
            variantBHitTracker ?? Substitute.For<IVariantBHitTracker>(),
            missedRequestTracker ?? Substitute.For<IMissedRequestTracker>(),
            Options.Create(rateLimitOptions ?? new RedirectRateLimitOptions()),
            rateLimiter ?? Substitute.For<IRedirectRateLimiter>(),
            resolver);
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

    [Fact]
    public async Task InvokeAsync_CultureScopedRule_MatchesWhenResolvedCultureMatches()
    {
        var cultureResolver = Substitute.For<IRedirectCultureResolver>();
        cultureResolver.ResolveCultureAsync(Arg.Any<string?>()).Returns(Task.FromResult<string?>("tr-tr"));
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
        cultureResolver.ResolveCultureAsync(Arg.Any<string?>()).Returns(Task.FromResult<string?>("en-us"));
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
        cultureResolver.ResolveCultureAsync(Arg.Any<string?>()).Returns(Task.FromResult<string?>("tr-tr"));
        var middleware = CreateMiddleware(cultureResolver: cultureResolver);
        var redirectService = Substitute.For<IRedirectService>();
        var rule = new RedirectEntry { Id = 1, OldUrl = "/old-page", NewUrl = "/new-page", StatusCode = 301, IsActive = true, Culture = null };
        redirectService.GetByOldUrl("/old-page", Arg.Any<string?>(), "tr-tr").Returns(rule);
        var context = CreateContext("/old-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/new-page", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_RedirectServiceThrows_PassesThroughToNextInsteadOfCrashing()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var redirectService = Substitute.For<IRedirectService>();
        redirectService.GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Throws(new InvalidOperationException("Invalid column name 'Culture' -- pending migration"));
        var context = CreateContext("/some-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_CultureResolverThrows_PassesThroughToNextInsteadOfCrashing()
    {
        var nextCalled = false;
        var cultureResolver = Substitute.For<IRedirectCultureResolver>();
        cultureResolver.ResolveCultureAsync(Arg.Any<string?>())
            .Returns(Task.FromException<string?>(new InvalidOperationException("Simulated schema mismatch")));
        var middleware = CreateMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; },
            cultureResolver: cultureResolver);
        var redirectService = Substitute.For<IRedirectService>();
        var context = CreateContext("/some-page");

        await middleware.InvokeAsync(context, redirectService);

        Assert.True(nextCalled);
        redirectService.DidNotReceive().GetByOldUrl(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>());
    }
}
