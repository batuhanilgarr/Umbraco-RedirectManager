using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Middleware;

public class RedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RedirectMiddleware> _logger;

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

        await _next(context);
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
