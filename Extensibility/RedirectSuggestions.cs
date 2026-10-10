using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Umbraco.RedirectManager.Extensibility;

public sealed record RedirectSuggestionRequest(string Path, string? Domain, string? Culture);

/// <param name="TargetUrl">Where the old URL should redirect to.</param>
/// <param name="Confidence">0..1 (the core clamps out-of-range values).</param>
/// <param name="Reason">Short human-readable explanation shown to the editor.</param>
public sealed record RedirectSuggestion(string TargetUrl, double Confidence, string Reason)
{
    /// <summary>Filled in by the core with the provider's <see cref="IRedirectSuggestionProvider.Name"/>.</summary>
    public string? Source { get; init; }
}

public interface IRedirectSuggestionProvider
{
    string Name { get; }

    Task<IReadOnlyList<RedirectSuggestion>> SuggestAsync(RedirectSuggestionRequest request, CancellationToken cancellationToken);
}

public interface IRedirectSuggestionService
{
    bool HasProviders { get; }

    Task<IReadOnlyList<RedirectSuggestion>> GetSuggestionsAsync(RedirectSuggestionRequest request, CancellationToken cancellationToken);
}

public sealed class RedirectExtensibilityOptions
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    public TimeSpan SuggestionProviderTimeout { get; set; } = DefaultTimeout;
    public TimeSpan EnricherTimeout { get; set; } = DefaultTimeout;
    public int MaxSuggestions { get; set; } = 5;

    /// <summary>
    /// Non-positive or out-of-range timeouts (which would make CancelAfter / WaitAsync
    /// throw) fall back to the 2 s default. Applied in one place for all extension timeouts.
    /// </summary>
    internal static TimeSpan NormalizeTimeout(TimeSpan value) =>
        value > TimeSpan.Zero && value.TotalMilliseconds < int.MaxValue ? value : DefaultTimeout;
}

public sealed class RedirectSuggestionService : IRedirectSuggestionService
{
    private readonly IRedirectSuggestionProvider[] _providers;
    private readonly RedirectExtensibilityOptions _options;
    private readonly ILogger<RedirectSuggestionService> _logger;

    public RedirectSuggestionService(
        IEnumerable<IRedirectSuggestionProvider> providers,
        IOptions<RedirectExtensibilityOptions> options,
        ILogger<RedirectSuggestionService> logger)
    {
        _providers = providers.ToArray();
        _options = options.Value;
        _logger = logger;
    }

    public bool HasProviders => _providers.Length > 0;

    public async Task<IReadOnlyList<RedirectSuggestion>> GetSuggestionsAsync(
        RedirectSuggestionRequest request, CancellationToken cancellationToken)
    {
        if (_providers.Length == 0)
        {
            return Array.Empty<RedirectSuggestion>();
        }

        // Task.WaitAsync hands back an already-completed task without looking at
        // the token, so honour caller cancellation explicitly.
        cancellationToken.ThrowIfCancellationRequested();

        var results = await Task.WhenAll(_providers.Select(p => RunProviderAsync(p, request, cancellationToken)))
            .ConfigureAwait(false);

        return results
            .SelectMany(r => r)
            .Where(s => !string.IsNullOrWhiteSpace(s.TargetUrl))
            // Case-insensitive on purpose: these are editor-facing suggestions, so near-duplicates
            // that differ only in casing are noise, even though URL paths are technically case-sensitive.
            .GroupBy(s => s.TargetUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.Confidence).First())
            .OrderByDescending(s => s.Confidence)
            .Take(Math.Max(1, _options.MaxSuggestions))
            .ToList();
    }

    private async Task<IReadOnlyList<RedirectSuggestion>> RunProviderAsync(
        IRedirectSuggestionProvider provider, RedirectSuggestionRequest request, CancellationToken cancellationToken)
    {
        var name = SafeName(provider);
        var timeout = RedirectExtensibilityOptions.NormalizeTimeout(_options.SuggestionProviderTimeout);

        try
        {
            // The linked token lets cooperative providers stop early; WaitAsync cuts
            // off providers that ignore it, so one slow provider never stalls the UI.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);

            var found = await provider.SuggestAsync(request, linked.Token)
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);

            return (found ?? Array.Empty<RedirectSuggestion>())
                .Select(s => s with { Confidence = NormalizeConfidence(s.Confidence), Source = name })
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redirect suggestion provider {Provider} failed or timed out", name);
            return Array.Empty<RedirectSuggestion>();
        }
    }

    // NaN and -Infinity map to 0 (no confidence), +Infinity to 1; System.Text.Json
    // throws on non-finite doubles, and Math.Clamp leaves NaN unchanged.
    private static double NormalizeConfidence(double value) =>
        double.IsNaN(value) ? 0d : Math.Clamp(value, 0d, 1d);

    private static string SafeName(IRedirectSuggestionProvider provider)
    {
        try
        {
            var name = provider.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch
        {
            // fall through to the type name
        }

        return provider.GetType().FullName ?? "unknown";
    }
}
