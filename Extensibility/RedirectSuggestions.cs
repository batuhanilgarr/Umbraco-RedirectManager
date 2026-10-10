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
    public TimeSpan SuggestionProviderTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan EnricherTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public int MaxSuggestions { get; set; } = 5;
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
            .GroupBy(s => s.TargetUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.Confidence).First())
            .OrderByDescending(s => s.Confidence)
            .Take(Math.Max(1, _options.MaxSuggestions))
            .ToList();
    }

    private async Task<IReadOnlyList<RedirectSuggestion>> RunProviderAsync(
        IRedirectSuggestionProvider provider, RedirectSuggestionRequest request, CancellationToken cancellationToken)
    {
        // The linked token lets cooperative providers stop early; WaitAsync cuts
        // off providers that ignore it, so one slow provider never stalls the UI.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_options.SuggestionProviderTimeout);

        try
        {
            var found = await provider.SuggestAsync(request, linked.Token)
                .WaitAsync(_options.SuggestionProviderTimeout, cancellationToken)
                .ConfigureAwait(false);

            return (found ?? Array.Empty<RedirectSuggestion>())
                .Select(s => s with { Confidence = Math.Clamp(s.Confidence, 0d, 1d), Source = provider.Name })
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redirect suggestion provider {Provider} failed or timed out", provider.Name);
            return Array.Empty<RedirectSuggestion>();
        }
    }
}
