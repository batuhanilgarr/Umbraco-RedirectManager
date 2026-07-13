namespace Umbraco.RedirectManager.Services;

public interface IRedirectCultureResolver
{
    // Resolves the culture (e.g. "tr-tr") registered against this domain in
    // Umbraco's own Settings > Culture and Hostnames configuration
    // (Umbraco.Cms.Core.Services.IDomainService), or null if no such binding
    // is registered -- meaning only culture-agnostic rules will match.
    Task<string?> ResolveCultureAsync(string? domain);
}
