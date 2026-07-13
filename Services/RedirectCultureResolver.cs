using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Core.Services;

namespace Umbraco.RedirectManager.Services;

public class RedirectCultureResolver : IRedirectCultureResolver
{
    private const string DomainCultureMapCacheKey = "RedirectManager.DomainCultureMap";

    private readonly IDomainService _domainService;
    private readonly IMemoryCache _memoryCache;

    public RedirectCultureResolver(IDomainService domainService, IMemoryCache memoryCache)
    {
        _domainService = domainService;
        _memoryCache = memoryCache;
    }

    public async Task<string?> ResolveCultureAsync(string? domain)
    {
        var normalizedDomain = DomainNormalizer.Normalize(domain);
        if (normalizedDomain == null)
            return null;

        var map = await GetDomainCultureMapAsync();
        return map.TryGetValue(normalizedDomain, out var culture) ? culture : null;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetDomainCultureMapAsync()
    {
        if (_memoryCache.TryGetValue(DomainCultureMapCacheKey, out IReadOnlyDictionary<string, string>? cached) && cached != null)
            return cached;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Umbraco.Cms.Core's synchronous IDomainService.GetAll(bool) was obsoleted
        // in 17.1.0 and removed entirely in 18.0.1 -- GetAllAsync is the only
        // overload guaranteed to exist at runtime on this TFM, even though this
        // project compiles net10.0 against the 17.1.0 reference (which still has
        // both). net8.0 targets 13.9.2, which never had GetAllAsync.
#if NET10_0_OR_GREATER
        var registeredDomains = await _domainService.GetAllAsync(false);
#else
        var registeredDomains = await Task.FromResult(_domainService.GetAll(false));
#endif

        // false: exclude wildcard domains. A wildcard domain represents a
        // content node's default culture assignment (its DomainName is a
        // node ID, not a real hostname) -- not meaningful for matching
        // against an incoming HTTP Host header.
        foreach (var registeredDomain in registeredDomains)
        {
            var normalized = DomainNormalizer.Normalize(registeredDomain.DomainName);
            if (normalized == null || string.IsNullOrWhiteSpace(registeredDomain.LanguageIsoCode))
                continue;

            map[normalized] = registeredDomain.LanguageIsoCode.Trim().ToLowerInvariant();
        }

        var result = (IReadOnlyDictionary<string, string>)map;
        _memoryCache.Set(DomainCultureMapCacheKey, result, TimeSpan.FromSeconds(30));
        return result;
    }
}
