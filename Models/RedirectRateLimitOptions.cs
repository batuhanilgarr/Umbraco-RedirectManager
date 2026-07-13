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
