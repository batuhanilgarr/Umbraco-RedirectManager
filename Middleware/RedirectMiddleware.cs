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

        if (redirect != null && redirect.IsActive)
        {
            if (TryApplyRateLimit(context))
                return;

            _logger.LogDebug("Redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                redirect.OldUrl, redirect.NewUrl, redirect.StatusCode);

            switch (redirect.StatusCode)
            {
                case 301:
                case 302:
                    var targetUrl = AppendPreservedQueryString(
                        ResolveRedirectTarget(context, redirect), redirect.PreserveQueryString, context.Request.QueryString);
                    context.Response.StatusCode = redirect.StatusCode;
                    context.Response.Headers.Location = targetUrl ?? "/";
                    return;

                case 404:
                    _hitTracker.RecordHit(redirect.Id);
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Not Found");
                    return;

                case 410:
                    _hitTracker.RecordHit(redirect.Id);
                    context.Response.StatusCode = 410;
                    await context.Response.WriteAsync("Gone");
                    return;
            }
        }

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
                case 301:
                    context.Response.StatusCode = 301;
                    context.Response.Headers.Location = AppendPreservedQueryString(
                        wildcardRedirect.ComputedNewUrl, wildcardRedirect.Entry.PreserveQueryString, context.Request.QueryString) ?? "/";
                    return;

                case 302:
                    context.Response.StatusCode = 302;
                    context.Response.Headers.Location = AppendPreservedQueryString(
                        wildcardRedirect.ComputedNewUrl, wildcardRedirect.Entry.PreserveQueryString, context.Request.QueryString) ?? "/";
                    return;

                case 404:
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Not Found");
                    return;

                case 410:
                    context.Response.StatusCode = 410;
                    await context.Response.WriteAsync("Gone");
                    return;
            }
        }

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

                case 404:
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Not Found");
                    return;

                case 410:
                    context.Response.StatusCode = 410;
                    await context.Response.WriteAsync("Gone");
                    return;
            }
        }

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
    // regex rules (out of scope — see design spec) or to 404/410 (there's no
    // second URL to split traffic toward). A visitor is assigned a variant
    // once and stays on it via a cookie, so repeat visits are consistent.
    private string? ResolveRedirectTarget(HttpContext context, Umbraco.RedirectManager.Models.RedirectEntry redirect)
    {
        if (string.IsNullOrWhiteSpace(redirect.VariantBUrl) || redirect.VariantBWeight is not int weight)
        {
            _hitTracker.RecordHit(redirect.Id);
            return redirect.NewUrl;
        }

        var cookieName = $"rm_ab_{redirect.Id}";
        var assignment = context.Request.Cookies[cookieName];

        bool useVariantB;
        if (assignment == "B")
        {
            useVariantB = true;
        }
        else if (assignment == "A")
        {
            useVariantB = false;
        }
        else
        {
            useVariantB = Random.Shared.Next(100) < Math.Clamp(weight, 0, 100);
            context.Response.Cookies.Append(cookieName, useVariantB ? "B" : "A", new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            });
        }

        if (useVariantB)
        {
            _variantBHitTracker.RecordHit(redirect.Id);
            return redirect.VariantBUrl;
        }

        _hitTracker.RecordHit(redirect.Id);
        return redirect.NewUrl;
    }

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

    private RedirectMatch? FindRegexMatchIn(IEnumerable<Umbraco.RedirectManager.Models.RedirectEntry> candidates, string path)
    {
        foreach (var r in candidates)
        {
            if (string.IsNullOrWhiteSpace(r.OldUrl))
                continue;

            var regex = RegexCache.GetOrAdd(r.OldUrl, pattern =>
                new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout));

            if (!regex.IsMatch(path))
                continue;

            var newUrl = r.NewUrl;

            if ((r.StatusCode == 301 || r.StatusCode == 302) && !string.IsNullOrWhiteSpace(newUrl))
            {
                try
                {
                    newUrl = regex.Replace(path, newUrl);
                }
                catch
                {
                    // If replace fails, fall back to original NewUrl
                }
            }

            return new RedirectMatch(r, newUrl);
        }

        return null;
    }

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

    private RedirectMatch? FindWildcardMatchIn(IEnumerable<Umbraco.RedirectManager.Models.RedirectEntry> candidates, string path)
    {
        foreach (var r in candidates)
        {
            if (string.IsNullOrWhiteSpace(r.OldUrl))
                continue;

            var regex = WildcardRegexCache.GetOrAdd(r.OldUrl, pattern =>
                new Regex(WildcardPatternBuilder.BuildRegexPattern(pattern), RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout));

            if (!regex.IsMatch(path))
                continue;

            var newUrl = r.NewUrl;

            if ((r.StatusCode == 301 || r.StatusCode == 302) && !string.IsNullOrWhiteSpace(newUrl))
            {
                try
                {
                    newUrl = regex.Replace(path, newUrl.Replace("*", "$1", StringComparison.Ordinal));
                }
                catch
                {
                    // If replace fails, fall back to original NewUrl
                }
            }

            return new RedirectMatch(r, newUrl);
        }

        return null;
    }

    private sealed class RedirectMatch
    {
        public RedirectMatch(Umbraco.RedirectManager.Models.RedirectEntry entry, string? computedNewUrl)
        {
            Entry = entry;
            ComputedNewUrl = computedNewUrl;
        }

        public Umbraco.RedirectManager.Models.RedirectEntry Entry { get; }
        public string? ComputedNewUrl { get; }
    }

    private static bool ShouldSkipRedirect(string path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        var skipPaths = new[]
        {
            "/umbraco",
            "/api",
            "/install",
            "/app_plugins",
            "/media",
            "/scripts",
            "/css",
            "/images",
            "/fonts"
        };

        return skipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    // Lets an exact-match rule fire regardless of a trailing-slash mismatch
    // between the request path and the stored OldUrl (e.g. a rule for
    // "/sayfa" also fires for "/sayfa/"). Returns null for the root path,
    // where toggling a trailing slash is meaningless.
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
