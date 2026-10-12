using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.RedirectManager.Extensibility;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Route("umbraco/api/redirectmanager")]
public class RedirectExtensibilityController : Controller
{
    private readonly IRedirectEditionProvider _edition;
    private readonly IDashboardExtensionRegistry _registry;
    private readonly IRedirectSuggestionService _suggestions;
    private readonly IRedirectCultureResolver _cultureResolver;

    public RedirectExtensibilityController(
        IRedirectEditionProvider edition,
        IDashboardExtensionRegistry registry,
        IRedirectSuggestionService suggestions,
        IRedirectCultureResolver cultureResolver)
    {
        _edition = edition;
        _registry = registry;
        _suggestions = suggestions;
        _cultureResolver = cultureResolver;
    }

    [HttpGet("capabilities")]
    public IActionResult GetCapabilities()
    {
        return Ok(new
        {
            edition = _edition.Edition.ToString(),
            features = _edition.EnabledFeatures,
            suggestionsAvailable = _suggestions.HasProviders,
            extensions = _registry.GetExtensions().Select(e => new
            {
                id = e.Id,
                slot = e.Slot == DashboardSlot.Tab ? "tab" : "missed-row-panel",
                label = e.Label,
                moduleUrl = e.ModuleUrl,
                elementName = e.ElementName,
                weight = e.Weight,
                sortKey = e.EffectiveSortKey,
                sortType = e.EffectiveSortKey is null ? null : e.EffectiveSortType,
                sortDefault = e.EffectiveSortKey is not null && e.SortDefault
            }).ToList()
        });
    }

    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions([FromQuery] string path, [FromQuery] string? domain, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest("path is required");

        var normalizedDomain = DomainNormalizer.Normalize(domain);
        var culture = await _cultureResolver.ResolveCultureAsync(normalizedDomain);
        var results = await _suggestions.GetSuggestionsAsync(
            new RedirectSuggestionRequest(path, normalizedDomain, culture), cancellationToken);
        return Ok(results);
    }
}
