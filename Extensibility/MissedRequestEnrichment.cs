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
        var timeout = RedirectExtensibilityOptions.NormalizeTimeout(_options.EnricherTimeout);

        // Sequential on purpose. Each enricher gets its own staging copies so an enricher that
        // times out (and keeps running) can only ever touch orphaned objects, never the real
        // rows that the next enricher or the serializer is using.
        foreach (var enricher in _enrichers)
        {
            try
            {
                var staging = rows.Select(Copy).ToList();

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linked.CancelAfter(timeout);

                await enricher.EnrichAsync(staging, linked.Token)
                    .WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);

                for (var i = 0; i < staging.Count && i < rows.Count; i++)
                {
                    if (staging[i].Extensions is not { } added)
                    {
                        continue;
                    }

                    foreach (var (key, value) in added.ToArray())
                    {
                        rows[i].SetExtension(key, value);
                    }
                }
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

    private static MissedRequestDto Copy(MissedRequestDto row) => new()
    {
        Id = row.Id,
        Path = row.Path,
        Domain = row.Domain,
        HitCount = row.HitCount,
        FirstSeenDate = row.FirstSeenDate,
        LastSeenDate = row.LastSeenDate,
        Category = row.Category
    };
}
