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
