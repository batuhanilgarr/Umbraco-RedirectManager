using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Umbraco.RedirectManager.Extensibility;

public enum DashboardSlot
{
    /// <summary>An extra tab after "Overview"; the element fills the tab body.</summary>
    Tab,

    /// <summary>An extra last column on the 404 log; one element per row (must load lazily).</summary>
    MissedRowPanel
}

/// <param name="Id">Unique, stable id (e.g. "premium.migration").</param>
/// <param name="ModuleUrl">ES module URL; must start with <c>/App_Plugins/</c>.</param>
/// <param name="ElementName">Custom element the module defines (lower-case, contains a hyphen).</param>
public sealed record DashboardExtension(
    string Id,
    DashboardSlot Slot,
    string Label,
    string ModuleUrl,
    string ElementName,
    int Weight = 100);

public interface IDashboardExtensionProvider
{
    IEnumerable<DashboardExtension> GetExtensions();
}

public interface IDashboardExtensionRegistry
{
    IReadOnlyList<DashboardExtension> GetExtensions();
}

public sealed class DashboardExtensionRegistry : IDashboardExtensionRegistry
{
    private static readonly Regex ElementNamePattern =
        new(@"^[a-z][a-z0-9]*(-[a-z0-9]+)+$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private readonly IDashboardExtensionProvider[] _providers;
    private readonly ILogger<DashboardExtensionRegistry> _logger;

    public DashboardExtensionRegistry(
        IEnumerable<IDashboardExtensionProvider> providers,
        ILogger<DashboardExtensionRegistry> logger)
    {
        _providers = providers.ToArray();
        _logger = logger;
    }

    public IReadOnlyList<DashboardExtension> GetExtensions()
    {
        var accepted = new List<DashboardExtension>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in _providers)
        {
            IEnumerable<DashboardExtension> items;
            try
            {
                items = provider.GetExtensions().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard extension provider {Provider} failed", provider.GetType().FullName);
                continue;
            }

            foreach (var item in items)
            {
                if (!IsValid(item, out var problem))
                {
                    _logger.LogWarning("Ignoring dashboard extension '{Id}': {Problem}", item?.Id, problem);
                    continue;
                }

                if (!seen.Add(item.Id))
                {
                    _logger.LogWarning("Ignoring duplicate dashboard extension id '{Id}'", item.Id);
                    continue;
                }

                accepted.Add(item);
            }
        }

        return accepted.OrderBy(e => e.Weight).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    }

    private static bool IsValid(DashboardExtension? item, out string problem)
    {
        if (item is null) { problem = "null"; return false; }
        if (string.IsNullOrWhiteSpace(item.Id)) { problem = "Id is required"; return false; }
        if (string.IsNullOrWhiteSpace(item.Label)) { problem = "Label is required"; return false; }

        var url = item.ModuleUrl ?? string.Empty;
        if (!url.StartsWith("/App_Plugins/", StringComparison.Ordinal) ||
            url.Contains("..", StringComparison.Ordinal) ||
            url.Contains("//", StringComparison.Ordinal) ||
            url.Contains('\\'))
        {
            problem = "ModuleUrl must be a path under /App_Plugins/";
            return false;
        }

        if (string.IsNullOrEmpty(item.ElementName) || !ElementNamePattern.IsMatch(item.ElementName))
        {
            problem = "ElementName must be a lower-case custom element name containing a hyphen";
            return false;
        }

        problem = string.Empty;
        return true;
    }
}
