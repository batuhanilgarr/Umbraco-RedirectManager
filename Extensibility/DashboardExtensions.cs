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
/// <param name="SortKey">
/// <see cref="DashboardSlot.MissedRowPanel"/> only. An enricher extension key (see
/// <c>MissedRequestDto.SetExtension</c>) the column can be sorted by. Letters, digits, '.', '_' and '-' only;
/// an invalid key is ignored (the column just is not sortable).
/// </param>
/// <param name="SortType">"number" (default), "string" or "date": how <paramref name="SortKey"/> values compare.</param>
/// <param name="SortDefault">Sort the 404 log by <paramref name="SortKey"/> (descending) until the user picks another column.</param>
public sealed record DashboardExtension(
    string Id,
    DashboardSlot Slot,
    string Label,
    string ModuleUrl,
    string ElementName,
    int Weight = 100,
    string? SortKey = null,
    string SortType = "number",
    bool SortDefault = false)
{
    private static readonly Regex SortKeyPattern =
        new(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>The sort key if it is valid and this is a row panel; otherwise null.</summary>
    public string? EffectiveSortKey =>
        Slot == DashboardSlot.MissedRowPanel && SortKey is { } k && SortKeyPattern.IsMatch(k) ? k : null;

    public string EffectiveSortType => SortType is "string" or "date" ? SortType : "number";
}

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
        new(@"^[a-z][a-z0-9]*(-[a-z0-9]+)+\z", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

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
        // Browsers decode %2e%2e and strip tabs/newlines inside URLs, so reject '%',
        // whitespace and control characters outright (query strings remain allowed).
        if (!url.StartsWith("/App_Plugins/", StringComparison.Ordinal) ||
            url.Any(c => c == '%' || char.IsControl(c) || char.IsWhiteSpace(c)) ||
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
