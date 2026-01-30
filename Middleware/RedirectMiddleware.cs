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

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectMiddleware(RequestDelegate next, ILogger<RedirectMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IRedirectService redirectService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        
        if (ShouldSkipRedirect(path))
        {
            await _next(context);
            return;
        }

        var redirect = redirectService.GetByOldUrl(path);

        if (redirect != null && redirect.IsActive)
        {
            _logger.LogDebug("Redirect found for {OldUrl} -> {NewUrl} ({StatusCode})", 
                redirect.OldUrl, redirect.NewUrl, redirect.StatusCode);

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

        var regexRedirect = FindRegexRedirect(path, redirectService);
        if (regexRedirect != null)
        {
            _logger.LogDebug("Regex redirect found for {OldUrl} -> {NewUrl} ({StatusCode})",
                regexRedirect.Entry.OldUrl, regexRedirect.ComputedNewUrl, regexRedirect.Entry.StatusCode);

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
    }

    private RedirectMatch? FindRegexRedirect(string path, IRedirectService redirectService)
    {
        try
        {
            foreach (var r in redirectService.GetActiveRegexEntries())
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
}
