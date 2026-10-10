using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Extensibility;

/// <summary>What the core knows about a request that ended in a 404.</summary>
public sealed record MissedRequestContext(
    string Path,
    string? Domain,
    string? Referrer,
    string? UserAgent,
    DateTime TimestampUtc);

/// <summary>
/// Called on the request path right after a 404 was recorded. <b>Must be fast
/// and non-blocking</b> — enqueue anything heavy. Exceptions are caught and
/// logged by the core. The core ships no observers, so by default nothing
/// beyond the existing miss counters is captured.
/// </summary>
public interface IMissedRequestObserver
{
    void OnMissedRequest(MissedRequestContext context);
}

public interface IMissedRequestObserverDispatcher
{
    void Dispatch(HttpContext httpContext, string path);
}

public sealed class MissedRequestObserverDispatcher : IMissedRequestObserverDispatcher
{
    private readonly IMissedRequestObserver[] _observers;
    private readonly ILogger<MissedRequestObserverDispatcher> _logger;

    public MissedRequestObserverDispatcher(
        IEnumerable<IMissedRequestObserver> observers,
        ILogger<MissedRequestObserverDispatcher> logger)
    {
        _observers = observers.ToArray();
        _logger = logger;
    }

    public void Dispatch(HttpContext httpContext, string path)
    {
        if (_observers.Length == 0)
        {
            return;
        }

        MissedRequestContext context;
        try
        {
            var headers = httpContext.Request.Headers;
            context = new MissedRequestContext(
                path,
                DomainNormalizer.Normalize(httpContext.Request.Host.Value),
                NullIfEmpty(headers.Referer.ToString()),
                NullIfEmpty(headers.UserAgent.ToString()),
                DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not build the missed-request context for {Path}", path);
            return;
        }

        foreach (var observer in _observers)
        {
            try
            {
                observer.OnMissedRequest(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Missed-request observer {Observer} failed", observer.GetType().FullName);
            }
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
