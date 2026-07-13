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

    public string? ResolveCulture(string? domain)
    {
        var normalizedDomain = DomainNormalizer.Normalize(domain);
        if (normalizedDomain == null)
            return null;

        var map = GetDomainCultureMap();
        return map.TryGetValue(normalizedDomain, out var culture) ? culture : null;
    }

    private IReadOnlyDictionary<string, string> GetDomainCultureMap()
    {
        return _memoryCache.GetOrCreate(DomainCultureMapCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // false: exclude wildcard domains. A wildcard domain represents a
            // content node's default culture assignment (its DomainName is a
            // node ID, not a real hostname) -- not meaningful for matching
            // against an incoming HTTP Host header.
            foreach (var registeredDomain in _domainService.GetAll(false))
            {
                var normalized = DomainNormalizer.Normalize(registeredDomain.DomainName);
                if (normalized == null || string.IsNullOrWhiteSpace(registeredDomain.LanguageIsoCode))
                    continue;

                map[normalized] = registeredDomain.LanguageIsoCode.Trim().ToLowerInvariant();
            }

            return (IReadOnlyDictionary<string, string>)map;
        }) ?? new Dictionary<string, string>();
    }
}
