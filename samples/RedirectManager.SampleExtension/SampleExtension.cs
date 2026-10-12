using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.RedirectManager.Composers;
using Umbraco.RedirectManager.Extensibility;
using Umbraco.RedirectManager.Models;

namespace RedirectManager.SampleExtension;

// Registers one implementation of every Redirect Manager extension point. A real
// package (e.g. a commercial edition) does exactly this from its own composer.
[ComposeAfter(typeof(RedirectManagerComposer))]
public class SampleExtensionComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<SampleMissCounter>();
        builder.Services.AddSingleton<IMissedRequestObserver, SampleMissObserver>();
        builder.Services.AddSingleton<IMissedRequestEnricher, SampleEnricher>();
        builder.Services.AddSingleton<IRedirectSuggestionProvider, SampleSuggestionProvider>();
        builder.Services.AddSingleton<IDashboardExtensionProvider, SampleDashboardExtensions>();
    }
}

// Test hook: start the site with RM_SAMPLE_FAULT=throw|hang to prove that a misbehaving
// extension cannot break the host (requests, the dashboard, the core API).
internal static class SampleFault
{
    public static string? Mode => Environment.GetEnvironmentVariable("RM_SAMPLE_FAULT");
}

public sealed class SampleMissCounter
{
    public int Count;
    public string? LastReferrer;
}

// Observers run on the request path after a 404: keep them fast, enqueue heavy work.
public sealed class SampleMissObserver : IMissedRequestObserver
{
    private readonly SampleMissCounter _counter;

    public SampleMissObserver(SampleMissCounter counter) => _counter = counter;

    public void OnMissedRequest(MissedRequestContext context)
    {
        if (SampleFault.Mode == "throw") throw new InvalidOperationException("sample observer fault");

        Interlocked.Increment(ref _counter.Count);
        _counter.LastReferrer = context.Referrer;
    }
}

// Adds extra "columns" to the 404 log rows (they arrive in the JSON as `extensions`).
public sealed class SampleEnricher : IMissedRequestEnricher
{
    private readonly SampleMissCounter _counter;

    public SampleEnricher(SampleMissCounter counter) => _counter = counter;

    public Task EnrichAsync(IReadOnlyList<MissedRequestDto> rows, CancellationToken cancellationToken)
    {
        if (SampleFault.Mode == "throw") throw new InvalidOperationException("sample enricher fault");
        if (SampleFault.Mode == "hang") return Task.Delay(TimeSpan.FromSeconds(30)); // ignores the token on purpose

        foreach (var row in rows)
        {
            row.SetExtension("sample.pathLength", row.Path.Length);
            row.SetExtension("sample.observedMisses", _counter.Count);
        }

        return Task.CompletedTask;
    }
}

// Deterministic on purpose: the parent section and the path without its extension.
public sealed class SampleSuggestionProvider : IRedirectSuggestionProvider
{
    public string Name => "Sample";

    public async Task<IReadOnlyList<RedirectSuggestion>> SuggestAsync(
        RedirectSuggestionRequest request, CancellationToken cancellationToken)
    {
        if (SampleFault.Mode == "throw") throw new InvalidOperationException("sample provider fault");
        if (SampleFault.Mode == "hang") await Task.Delay(TimeSpan.FromSeconds(30)); // ignores the token on purpose

        var suggestions = new List<RedirectSuggestion>();
        var trimmed = request.Path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');

        if (slash > 0)
        {
            suggestions.Add(new RedirectSuggestion(trimmed[..slash] + "/", 0.6, "Parent section"));
        }

        var dot = trimmed.LastIndexOf('.');
        if (dot > slash)
        {
            suggestions.Add(new RedirectSuggestion(trimmed[..dot], 0.8, "Same path without extension"));
        }

        return suggestions;
    }
}

public sealed class SampleDashboardExtensions : IDashboardExtensionProvider
{
    public IEnumerable<DashboardExtension> GetExtensions() => new[]
    {
        new DashboardExtension("sample.tab", DashboardSlot.Tab, "Sample extension",
            "/App_Plugins/RedirectManagerSample/sample-extension.js", "rm-sample-tab", 10),
        new DashboardExtension("sample.row", DashboardSlot.MissedRowPanel, "Suggestions",
            "/App_Plugins/RedirectManagerSample/sample-extension.js", "rm-sample-row", 10)
    };
}
