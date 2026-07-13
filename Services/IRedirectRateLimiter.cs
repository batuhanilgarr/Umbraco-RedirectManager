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
