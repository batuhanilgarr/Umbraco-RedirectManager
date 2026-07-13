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
