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
    private readonly IMissedRequestTracker _missedRequestTracker;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectMiddleware(
        RequestDelegate next,
        ILogger<RedirectMiddleware> logger,
        IRedirectHitTracker hitTracker,
        IMissedRequestTracker missedRequestTracker)
    {
        _next = next;
        _logger = logger;
        _hitTracker = hitTracker;
        _missedRequestTracker = missedRequestTracker;
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
            _logger.LogDebug("Redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                redirect.OldUrl, redirect.NewUrl, redirect.StatusCode);
            _hitTracker.RecordHit(redirect.Id);

            switch (redirect.StatusCode)
            {
                case 301:
                    context.Response.StatusCode = 301;
                    context.Response.Headers.Location = redirect.NewUrl ?? "/";
                    return;

                case 302:
                    context.Response.StatusCode = 302;
                    context.Response.Headers.Location = redirect.NewUrl ?? "/";
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
            _logger.LogDebug("Regex redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                regexRedirect.Entry.OldUrl, regexRedirect.ComputedNewUrl, regexRedirect.Entry.StatusCode);
            _hitTracker.RecordHit(regexRedirect.Entry.Id);

            switch (regexRedirect.Entry.StatusCode)
            {
                case 301:
                    context.Response.StatusCode = 301;
                    context.Response.Headers.Location = regexRedirect.ComputedNewUrl ?? "/";
                    return;

                case 302:
                    context.Response.StatusCode = 302;
                    context.Response.Headers.Location = regexRedirect.ComputedNewUrl ?? "/";
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
}
