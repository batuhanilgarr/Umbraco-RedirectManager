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

    public Task<IReadOnlyList<RedirectSuggestion>> SuggestAsync(
        RedirectSuggestionRequest request, CancellationToken cancellationToken)
    {
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

        return Task.FromResult<IReadOnlyList<RedirectSuggestion>>(suggestions);
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
