using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Extensibility;

/// <summary>
/// Adds extra columns to the 404-log rows the dashboard receives, by calling
/// <see cref="MissedRequestDto.SetExtension"/>. Extension keys should be
/// namespaced (e.g. "intelligence.priority"). The core ships no enrichers.
/// </summary>
public interface IMissedRequestEnricher
{
    Task EnrichAsync(IReadOnlyList<MissedRequestDto> rows, CancellationToken cancellationToken);
}

public interface IMissedRequestEnrichmentService
{
    Task EnrichAsync(IReadOnlyList<MissedRequestDto> rows, CancellationToken cancellationToken);
}

public sealed class MissedRequestEnrichmentService : IMissedRequestEnrichmentService
{
    private readonly IMissedRequestEnricher[] _enrichers;
    private readonly RedirectExtensibilityOptions _options;
    private readonly ILogger<MissedRequestEnrichmentService> _logger;

    public MissedRequestEnrichmentService(
        IEnumerable<IMissedRequestEnricher> enrichers,
        IOptions<RedirectExtensibilityOptions> options,
        ILogger<MissedRequestEnrichmentService> logger)
    {
        _enrichers = enrichers.ToArray();
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnrichAsync(IReadOnlyList<MissedRequestDto> rows, CancellationToken cancellationToken)
    {
        // Sequential on purpose: enrichers write into the same row objects.
        foreach (var enricher in _enrichers)
        {
            try
            {
                await enricher.EnrichAsync(rows, cancellationToken)
                    .WaitAsync(_options.EnricherTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Missed-request enricher {Enricher} failed or timed out", enricher.GetType().FullName);
            }
        }
    }
}
