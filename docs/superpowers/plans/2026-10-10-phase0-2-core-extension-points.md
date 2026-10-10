# Phase 0.2 — Core Extension Points (v1.11.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Give the MIT core the seams the future commercial `BT.RedirectManager.Premium` package plugs into (edition, 404 observers, suggestion providers, 404-row enrichers, dashboard UI extensions) — with **zero user-visible change** when nothing is registered.

**Architecture:** A new `Umbraco.RedirectManager.Extensibility` namespace holds small public interfaces plus fail-isolating core services (observer dispatcher, suggestion service, enrichment service, dashboard-extension registry, default `StandardEditionProvider`). Extensions register through normal DI multi-registration from their own composer. A new `RedirectExtensibilityController` exposes `GET capabilities` and `GET suggestions`; the Lit dashboard reads capabilities once and, only if extensions exist, renders extra tabs / a per-row panel by dynamically importing the extension's module from `/App_Plugins/`. A sample extension under `samples/` is the conformance harness and living documentation, verified in a real Umbraco site. Premium targets Umbraco 17+ only, so the AngularJS dashboard is **not** touched (the backend seams still compile for net8.0).

**Tech Stack:** C# (net8.0 + net10.0), xUnit + NSubstitute, Lit (plain JS, no build step).

**Spec:** `docs/superpowers/specs/2026-10-10-monetization-roadmap-design.md` (Phase 0, sub-project 0.2)

**Guardrails (from the spec) this plan must not violate**
- No behavior change for existing users: with no extension registered, API JSON, DOM and middleware behavior are identical.
- Fail-open: an extension that throws or hangs can never break request handling, redirect resolution or core API calls.
- Core registers **no** observers/providers/enrichers/UI extensions itself.

**Design decisions made (and why)**
- Observers are **synchronous by contract** (`void OnMissedRequest`) — the 404 path is hot; extensions must enqueue heavy work.
- One `IMissedRequestObserverDispatcher` / `IMissedRequestEnrichmentService` dependency instead of `IEnumerable<…>` constructor params, so the middleware/controller constructors grow by one parameter each and isolation logic lives in one tested place.
- `GET suggestions?path=&domain=` takes the path as text (no DB lookup by row id) — the dashboard already has it.
- `ModuleUrl` must start with `/App_Plugins/` (no scheme, no `..`) — the registry drops anything else, so a bad registration can never make the backoffice load a remote script.
- Rows get a `Extensions` bag that is **omitted from JSON when null**, so the 404-log API payload is byte-identical today.
- The per-row panel adds a column only when at least one `missed-row-panel` extension exists. Extensions must load lazily (never fetch on connect for every row — a log can hold thousands).

---

## File Structure

| File | Responsibility |
|---|---|
| `Extensibility/RedirectEdition.cs` | `RedirectEdition` enum, `IRedirectEditionProvider`, default `StandardEditionProvider` |
| `Extensibility/MissedRequestObserver.cs` | `MissedRequestContext`, `IMissedRequestObserver`, dispatcher (+interface) |
| `Extensibility/RedirectSuggestions.cs` | request/result records, `IRedirectSuggestionProvider`, `IRedirectSuggestionService` + impl, `RedirectExtensibilityOptions` |
| `Extensibility/MissedRequestEnrichment.cs` | `IMissedRequestEnricher`, enrichment service (+interface) |
| `Extensibility/DashboardExtensions.cs` | `DashboardExtension`, `DashboardSlot`, `IDashboardExtensionProvider`, validating registry (+interface) |
| `Controllers/RedirectExtensibilityController.cs` | `GET capabilities`, `GET suggestions` |
| `Models/MissedRequestDto.cs` (modify) | `Extensions` bag |
| `Middleware/RedirectMiddleware.cs` (modify) | call dispatcher after a recorded 404 |
| `Controllers/RedirectApiController.cs` (modify) | run enrichment in `GetMissed` |
| `Composers/RedirectManagerComposer.cs` (modify) | DI registrations |
| `App_Plugins/RedirectManager/redirect-dashboard.js` (modify) | capabilities fetch, `rm-extension-host`, extra tabs, row panel column |
| `samples/RedirectManager.SampleExtension/**` | conformance harness + documentation by example |
| `docs/EXTENDING.md` | the public contract and stability policy |
| `scripts/dev/cdp.mjs` | reusable Chrome-DevTools-Protocol helper for live backoffice verification |

---

### Task 1: Contracts and fail-isolating core services (TDD)

**Files:**
- Create: the five `Extensibility/*.cs` files above
- Modify: `Models/MissedRequestDto.cs`
- Test: `Umbraco.RedirectManager.Tests/Extensibility/*Tests.cs`

- [ ] **Step 1: Write the failing tests** (create `Umbraco.RedirectManager.Tests/Extensibility/ExtensibilityServicesTests.cs`)

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.RedirectManager.Extensibility;
using Umbraco.RedirectManager.Models;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Extensibility;

public class ExtensibilityServicesTests
{
    // ---------- edition ----------

    [Fact]
    public void Default_edition_is_Standard_with_no_enabled_features()
    {
        var edition = new StandardEditionProvider();

        Assert.Equal(RedirectEdition.Standard, edition.Edition);
        Assert.Empty(edition.EnabledFeatures);
    }

    // ---------- observers ----------

    private static DefaultHttpContext Context(string host = "Example.com:8080", string? referer = "https://ref.test/a", string? ua = "UA/1.0")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        if (referer != null) ctx.Request.Headers.Referer = referer;
        if (ua != null) ctx.Request.Headers.UserAgent = ua;
        return ctx;
    }

    [Fact]
    public void Dispatcher_passes_normalised_context_to_every_observer()
    {
        var a = Substitute.For<IMissedRequestObserver>();
        var b = Substitute.For<IMissedRequestObserver>();
        var dispatcher = new MissedRequestObserverDispatcher(new[] { a, b }, NullLogger<MissedRequestObserverDispatcher>.Instance);

        dispatcher.Dispatch(Context(), "/old-page");

        foreach (var observer in new[] { a, b })
        {
            observer.Received(1).OnMissedRequest(Arg.Is<MissedRequestContext>(c =>
                c.Path == "/old-page" && c.Domain == "example.com" &&
                c.Referrer == "https://ref.test/a" && c.UserAgent == "UA/1.0"));
        }
    }

    [Fact]
    public void Dispatcher_leaves_missing_headers_null()
    {
        var observer = Substitute.For<IMissedRequestObserver>();
        var dispatcher = new MissedRequestObserverDispatcher(new[] { observer }, NullLogger<MissedRequestObserverDispatcher>.Instance);

        dispatcher.Dispatch(Context(referer: null, ua: null), "/x");

        observer.Received(1).OnMissedRequest(Arg.Is<MissedRequestContext>(c => c.Referrer == null && c.UserAgent == null));
    }

    [Fact]
    public void A_throwing_observer_does_not_stop_the_others_or_the_request()
    {
        var bad = Substitute.For<IMissedRequestObserver>();
        bad.When(o => o.OnMissedRequest(Arg.Any<MissedRequestContext>())).Do(_ => throw new InvalidOperationException("boom"));
        var good = Substitute.For<IMissedRequestObserver>();
        var dispatcher = new MissedRequestObserverDispatcher(new[] { bad, good }, NullLogger<MissedRequestObserverDispatcher>.Instance);

        var ex = Record.Exception(() => dispatcher.Dispatch(Context(), "/x"));

        Assert.Null(ex);
        good.Received(1).OnMissedRequest(Arg.Any<MissedRequestContext>());
    }

    [Fact]
    public void Dispatcher_with_no_observers_is_a_no_op()
    {
        var dispatcher = new MissedRequestObserverDispatcher(Array.Empty<IMissedRequestObserver>(), NullLogger<MissedRequestObserverDispatcher>.Instance);

        Assert.Null(Record.Exception(() => dispatcher.Dispatch(Context(), "/x")));
    }

    // ---------- suggestions ----------

    private static RedirectSuggestionService Suggestions(TimeSpan? timeout, int max, params IRedirectSuggestionProvider[] providers) =>
        new(providers,
            Options.Create(new RedirectExtensibilityOptions
            {
                SuggestionProviderTimeout = timeout ?? TimeSpan.FromSeconds(2),
                MaxSuggestions = max
            }),
            NullLogger<RedirectSuggestionService>.Instance);

    private static IRedirectSuggestionProvider Provider(string name, params RedirectSuggestion[] results)
    {
        var p = Substitute.For<IRedirectSuggestionProvider>();
        p.Name.Returns(name);
        p.SuggestAsync(Arg.Any<RedirectSuggestionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RedirectSuggestion>>(results));
        return p;
    }

    private static readonly RedirectSuggestionRequest Request = new("/old", "example.com", null);

    [Fact]
    public async Task No_providers_means_no_suggestions_and_HasProviders_false()
    {
        var service = Suggestions(null, 5);

        Assert.False(service.HasProviders);
        Assert.Empty(await service.GetSuggestionsAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task Suggestions_are_deduplicated_ordered_clamped_limited_and_attributed()
    {
        var service = Suggestions(null, 2,
            Provider("A", new RedirectSuggestion("/low", 0.2, "r"), new RedirectSuggestion("/dup", 0.4, "r"), new RedirectSuggestion("/over", 7, "r")),
            Provider("B", new RedirectSuggestion("/DUP", 0.9, "r"), new RedirectSuggestion("", 1, "blank target ignored")));

        var result = await service.GetSuggestionsAsync(Request, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("/over", result[0].TargetUrl);
        Assert.Equal(1.0, result[0].Confidence);          // clamped from 7
        Assert.Equal("A", result[0].Source);
        Assert.Equal("/DUP", result[1].TargetUrl);         // highest-confidence duplicate wins (0.9 from B)
        Assert.Equal("B", result[1].Source);
    }

    [Fact]
    public async Task A_throwing_provider_is_ignored_and_the_others_still_answer()
    {
        var bad = Substitute.For<IRedirectSuggestionProvider>();
        bad.Name.Returns("bad");
        bad.SuggestAsync(Arg.Any<RedirectSuggestionRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<RedirectSuggestion>>>(_ => throw new InvalidOperationException("boom"));
        var service = Suggestions(null, 5, bad, Provider("ok", new RedirectSuggestion("/new", 0.8, "r")));

        var result = await service.GetSuggestionsAsync(Request, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("/new", result[0].TargetUrl);
    }

    [Fact]
    public async Task A_provider_that_ignores_cancellation_is_cut_off_after_the_timeout()
    {
        var slow = Substitute.For<IRedirectSuggestionProvider>();
        slow.Name.Returns("slow");
        slow.SuggestAsync(Arg.Any<RedirectSuggestionRequest>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await Task.Delay(TimeSpan.FromSeconds(10)); return (IReadOnlyList<RedirectSuggestion>)Array.Empty<RedirectSuggestion>(); });
        var service = Suggestions(TimeSpan.FromMilliseconds(50), 5, slow, Provider("ok", new RedirectSuggestion("/new", 0.8, "r")));

        var started = DateTime.UtcNow;
        var result = await service.GetSuggestionsAsync(Request, CancellationToken.None);

        Assert.Single(result);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Caller_cancellation_still_propagates()
    {
        var service = Suggestions(null, 5, Provider("ok", new RedirectSuggestion("/new", 0.8, "r")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetSuggestionsAsync(Request, cts.Token));
    }

    // ---------- enrichment ----------

    [Fact]
    public async Task Enrichers_add_extensions_and_a_failing_one_is_ignored()
    {
        var bad = Substitute.For<IMissedRequestEnricher>();
        bad.EnrichAsync(Arg.Any<IReadOnlyList<MissedRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("boom"));
        var good = Substitute.For<IMissedRequestEnricher>();
        good.EnrichAsync(Arg.Any<IReadOnlyList<MissedRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                foreach (var row in call.Arg<IReadOnlyList<MissedRequestDto>>()) row.SetExtension("score", 42);
                return Task.CompletedTask;
            });
        var service = new MissedRequestEnrichmentService(new[] { bad, good },
            Options.Create(new RedirectExtensibilityOptions()), NullLogger<MissedRequestEnrichmentService>.Instance);
        var rows = new List<MissedRequestDto> { new() { Id = 1, Path = "/a" } };

        await service.EnrichAsync(rows, CancellationToken.None);

        Assert.Equal(42, rows[0].Extensions!["score"]);
    }

    [Fact]
    public void Extensions_are_omitted_from_JSON_when_unset_so_the_payload_is_unchanged()
    {
        var json = JsonSerializer.Serialize(new MissedRequestDto { Id = 1, Path = "/a" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("extensions", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extensions_are_serialised_when_set()
    {
        var dto = new MissedRequestDto { Id = 1, Path = "/a" };
        dto.SetExtension("score", 3);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"extensions\":{\"score\":3}", json);
    }

    // ---------- dashboard extension registry ----------

    private sealed class FixedProvider : IDashboardExtensionProvider
    {
        private readonly IEnumerable<DashboardExtension> _items;
        public FixedProvider(params DashboardExtension[] items) => _items = items;
        public IEnumerable<DashboardExtension> GetExtensions() => _items;
    }

    private sealed class ThrowingProvider : IDashboardExtensionProvider
    {
        public IEnumerable<DashboardExtension> GetExtensions() => throw new InvalidOperationException("boom");
    }

    private static DashboardExtension Ext(string id = "x.tab", string module = "/App_Plugins/X/x.js", string element = "x-tab", int weight = 100) =>
        new(id, DashboardSlot.Tab, "Label", module, element, weight);

    private static DashboardExtensionRegistry Registry(params IDashboardExtensionProvider[] providers) =>
        new(providers, NullLogger<DashboardExtensionRegistry>.Instance);

    [Fact]
    public void Registry_returns_valid_extensions_ordered_by_weight_then_id()
    {
        var result = Registry(new FixedProvider(Ext("b", weight: 10), Ext("a", weight: 10), Ext("c", weight: 1))).GetExtensions();

        Assert.Equal(new[] { "c", "a", "b" }, result.Select(e => e.Id));
    }

    [Theory]
    [InlineData("https://evil.test/x.js")]
    [InlineData("//evil.test/x.js")]
    [InlineData("/App_Plugins/../secret.js")]
    [InlineData("/other/x.js")]
    [InlineData("")]
    public void Registry_drops_module_urls_outside_App_Plugins(string module)
    {
        Assert.Empty(Registry(new FixedProvider(Ext(module: module))).GetExtensions());
    }

    [Theory]
    [InlineData("nohyphen")]
    [InlineData("Has-Caps")]
    [InlineData("a b-c")]
    [InlineData("")]
    public void Registry_drops_invalid_custom_element_names(string element)
    {
        Assert.Empty(Registry(new FixedProvider(Ext(element: element))).GetExtensions());
    }

    [Fact]
    public void Registry_keeps_the_first_of_duplicate_ids_and_survives_a_throwing_provider()
    {
        var result = Registry(new ThrowingProvider(), new FixedProvider(Ext("dup", element: "first-el"), Ext("dup", element: "second-el"))).GetExtensions();

        Assert.Single(result);
        Assert.Equal("first-el", result[0].ElementName);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter ExtensibilityServicesTests`
Expected: FAIL to compile (types do not exist yet).

- [ ] **Step 3: Implement the contracts**

`Extensibility/RedirectEdition.cs`:

```csharp
namespace Umbraco.RedirectManager.Extensibility;

public enum RedirectEdition
{
    Standard = 0,
    Premium = 1,
    Company = 2
}

/// <summary>
/// Reports which edition is active. The core registers a Standard provider by
/// default (via TryAdd); a commercial package replaces it with one that
/// validates its license. Implementations must be cheap and must never throw.
/// </summary>
public interface IRedirectEditionProvider
{
    RedirectEdition Edition { get; }

    /// <summary>Stable feature keys enabled for this installation (empty in Standard).</summary>
    IReadOnlyCollection<string> EnabledFeatures { get; }
}

public sealed class StandardEditionProvider : IRedirectEditionProvider
{
    public RedirectEdition Edition => RedirectEdition.Standard;

    public IReadOnlyCollection<string> EnabledFeatures { get; } = Array.Empty<string>();
}
```

`Extensibility/MissedRequestObserver.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Extensibility;

/// <summary>What the core knows about a request that ended in a 404.</summary>
public sealed record MissedRequestContext(
    string Path,
    string? Domain,
    string? Referrer,
    string? UserAgent,
    DateTime TimestampUtc);

/// <summary>
/// Called on the request path right after a 404 was recorded. <b>Must be fast
/// and non-blocking</b> — enqueue anything heavy. Exceptions are caught and
/// logged by the core. The core ships no observers, so by default nothing
/// beyond the existing miss counters is captured.
/// </summary>
public interface IMissedRequestObserver
{
    void OnMissedRequest(MissedRequestContext context);
}

public interface IMissedRequestObserverDispatcher
{
    void Dispatch(HttpContext httpContext, string path);
}

public sealed class MissedRequestObserverDispatcher : IMissedRequestObserverDispatcher
{
    private readonly IMissedRequestObserver[] _observers;
    private readonly ILogger<MissedRequestObserverDispatcher> _logger;

    public MissedRequestObserverDispatcher(
        IEnumerable<IMissedRequestObserver> observers,
        ILogger<MissedRequestObserverDispatcher> logger)
    {
        _observers = observers.ToArray();
        _logger = logger;
    }

    public void Dispatch(HttpContext httpContext, string path)
    {
        if (_observers.Length == 0)
        {
            return;
        }

        MissedRequestContext context;
        try
        {
            var headers = httpContext.Request.Headers;
            context = new MissedRequestContext(
                path,
                DomainNormalizer.Normalize(httpContext.Request.Host.Value),
                NullIfEmpty(headers.Referer.ToString()),
                NullIfEmpty(headers.UserAgent.ToString()),
                DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not build the missed-request context for {Path}", path);
            return;
        }

        foreach (var observer in _observers)
        {
            try
            {
                observer.OnMissedRequest(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Missed-request observer {Observer} failed", observer.GetType().FullName);
            }
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
```

`Extensibility/RedirectSuggestions.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Umbraco.RedirectManager.Extensibility;

public sealed record RedirectSuggestionRequest(string Path, string? Domain, string? Culture);

/// <param name="TargetUrl">Where the old URL should redirect to.</param>
/// <param name="Confidence">0..1 (the core clamps out-of-range values).</param>
/// <param name="Reason">Short human-readable explanation shown to the editor.</param>
public sealed record RedirectSuggestion(string TargetUrl, double Confidence, string Reason)
{
    /// <summary>Filled in by the core with the provider's <see cref="IRedirectSuggestionProvider.Name"/>.</summary>
    public string? Source { get; init; }
}

public interface IRedirectSuggestionProvider
{
    string Name { get; }

    Task<IReadOnlyList<RedirectSuggestion>> SuggestAsync(RedirectSuggestionRequest request, CancellationToken cancellationToken);
}

public interface IRedirectSuggestionService
{
    bool HasProviders { get; }

    Task<IReadOnlyList<RedirectSuggestion>> GetSuggestionsAsync(RedirectSuggestionRequest request, CancellationToken cancellationToken);
}

public sealed class RedirectExtensibilityOptions
{
    public TimeSpan SuggestionProviderTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan EnricherTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public int MaxSuggestions { get; set; } = 5;
}

public sealed class RedirectSuggestionService : IRedirectSuggestionService
{
    private readonly IRedirectSuggestionProvider[] _providers;
    private readonly RedirectExtensibilityOptions _options;
    private readonly ILogger<RedirectSuggestionService> _logger;

    public RedirectSuggestionService(
        IEnumerable<IRedirectSuggestionProvider> providers,
        IOptions<RedirectExtensibilityOptions> options,
        ILogger<RedirectSuggestionService> logger)
    {
        _providers = providers.ToArray();
        _options = options.Value;
        _logger = logger;
    }

    public bool HasProviders => _providers.Length > 0;

    public async Task<IReadOnlyList<RedirectSuggestion>> GetSuggestionsAsync(
        RedirectSuggestionRequest request, CancellationToken cancellationToken)
    {
        if (_providers.Length == 0)
        {
            return Array.Empty<RedirectSuggestion>();
        }

        // Task.WaitAsync hands back an already-completed task without looking at
        // the token, so honour caller cancellation explicitly.
        cancellationToken.ThrowIfCancellationRequested();

        var results = await Task.WhenAll(_providers.Select(p => RunProviderAsync(p, request, cancellationToken)))
            .ConfigureAwait(false);

        return results
            .SelectMany(r => r)
            .Where(s => !string.IsNullOrWhiteSpace(s.TargetUrl))
            .GroupBy(s => s.TargetUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.Confidence).First())
            .OrderByDescending(s => s.Confidence)
            .Take(Math.Max(1, _options.MaxSuggestions))
            .ToList();
    }

    private async Task<IReadOnlyList<RedirectSuggestion>> RunProviderAsync(
        IRedirectSuggestionProvider provider, RedirectSuggestionRequest request, CancellationToken cancellationToken)
    {
        // The linked token lets cooperative providers stop early; WaitAsync cuts
        // off providers that ignore it, so one slow provider never stalls the UI.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_options.SuggestionProviderTimeout);

        try
        {
            var found = await provider.SuggestAsync(request, linked.Token)
                .WaitAsync(_options.SuggestionProviderTimeout, cancellationToken)
                .ConfigureAwait(false);

            return (found ?? Array.Empty<RedirectSuggestion>())
                .Select(s => s with { Confidence = Math.Clamp(s.Confidence, 0d, 1d), Source = provider.Name })
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redirect suggestion provider {Provider} failed or timed out", provider.Name);
            return Array.Empty<RedirectSuggestion>();
        }
    }
}
```

`Extensibility/MissedRequestEnrichment.cs`:

```csharp
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
```

`Extensibility/DashboardExtensions.cs`:

```csharp
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
```

`Models/MissedRequestDto.cs` — add (and `using System.Text.Json.Serialization;`):

```csharp
    /// <summary>
    /// Extra columns contributed by extensions (see IMissedRequestEnricher).
    /// Omitted from JSON when no extension adds anything, so the payload is
    /// identical to earlier versions for everyone not using extensions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Extensions { get; set; }

    public void SetExtension(string key, object? value)
    {
        Extensions ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        Extensions[key] = value;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter ExtensibilityServicesTests`
Expected: PASS (all). Then `dotnet build Umbraco.RedirectManager.csproj -f net8.0` and `-f net10.0` succeed.

- [ ] **Step 5: Commit**

```bash
git add Extensibility Models/MissedRequestDto.cs Umbraco.RedirectManager.Tests/Extensibility
git commit -m "feat: add extensibility contracts and fail-isolating core services"
```

---

### Task 2: Wire the observer dispatcher into the middleware (TDD)

**Files:**
- Modify: `Middleware/RedirectMiddleware.cs`
- Test: `Umbraco.RedirectManager.Tests/Middleware/RedirectMiddlewareTests.cs`

- [ ] **Step 1: Write failing tests.** In the test helper `CreateMiddleware(...)` add an optional parameter `IMissedRequestObserverDispatcher? observerDispatcher = null` and pass `observerDispatcher ?? Substitute.For<IMissedRequestObserverDispatcher>()` as the new **last** constructor argument. Add `using Umbraco.RedirectManager.Extensibility;`. Then add:

```csharp
    [Fact]
    public async Task Dispatches_to_observers_when_a_404_is_recorded()
    {
        var dispatcher = Substitute.For<IMissedRequestObserverDispatcher>();
        var middleware = CreateMiddleware(
            next: ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; },
            observerDispatcher: dispatcher);
        var context = CreateContext("/gone-page");

        await middleware.InvokeAsync(context, Substitute.For<IRedirectService>());

        dispatcher.Received(1).Dispatch(context, "/gone-page");
    }

    [Fact]
    public async Task Does_not_dispatch_for_successful_responses()
    {
        var dispatcher = Substitute.For<IMissedRequestObserverDispatcher>();
        var middleware = CreateMiddleware(next: ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; }, observerDispatcher: dispatcher);

        await middleware.InvokeAsync(CreateContext("/fine"), Substitute.For<IRedirectService>());

        dispatcher.DidNotReceiveWithAnyArgs().Dispatch(default!, default!);
    }

    [Fact]
    public async Task A_dispatcher_failure_never_breaks_the_request()
    {
        var dispatcher = Substitute.For<IMissedRequestObserverDispatcher>();
        dispatcher.When(d => d.Dispatch(Arg.Any<HttpContext>(), Arg.Any<string>())).Do(_ => throw new InvalidOperationException("boom"));
        var middleware = CreateMiddleware(next: ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; }, observerDispatcher: dispatcher);

        var ex = await Record.ExceptionAsync(() => middleware.InvokeAsync(CreateContext("/gone-page"), Substitute.For<IRedirectService>()));

        Assert.Null(ex);
    }
```

- [ ] **Step 2: Run to verify failure** — `dotnet test Umbraco.RedirectManager.Tests --filter RedirectMiddlewareTests` → compile error (constructor).

- [ ] **Step 3: Implement.** In `RedirectMiddleware`: add `using Umbraco.RedirectManager.Extensibility;`, a `private readonly IMissedRequestObserverDispatcher _observerDispatcher;` field, a new **last** constructor parameter `IMissedRequestObserverDispatcher observerDispatcher` (assign it), and replace the 404 block in `InvokeAsync`:

```csharp
        if (context.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            _missedRequestTracker.RecordMiss(path);

            // Extensions (e.g. a commercial package) may observe the miss. The
            // dispatcher already isolates observer failures; this guard keeps
            // even an unexpected dispatcher fault from affecting the request.
            try
            {
                _observerDispatcher.Dispatch(context, path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Missed-request observer dispatch failed for {Path}", path);
            }
        }
```

- [ ] **Step 4: Run** — `dotnet test Umbraco.RedirectManager.Tests` → all pass (existing 87 + new).

- [ ] **Step 5: Commit** — `git commit -am "feat: notify missed-request observers after a recorded 404"`

---

### Task 3: Controller — capabilities + suggestions endpoints, enrichment in GetMissed, DI (TDD)

**Files:**
- Create: `Controllers/RedirectExtensibilityController.cs`
- Modify: `Controllers/RedirectApiController.cs`, `Composers/RedirectManagerComposer.cs`
- Test: `Umbraco.RedirectManager.Tests/Controllers/RedirectExtensibilityControllerTests.cs`; update the two existing controller test files' constructor calls.

- [ ] **Step 1: Write failing tests**

```csharp
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Umbraco.RedirectManager.Controllers;
using Umbraco.RedirectManager.Extensibility;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Controllers;

public class RedirectExtensibilityControllerTests
{
    private readonly IRedirectEditionProvider _edition = Substitute.For<IRedirectEditionProvider>();
    private readonly IDashboardExtensionRegistry _registry = Substitute.For<IDashboardExtensionRegistry>();
    private readonly IRedirectSuggestionService _suggestions = Substitute.For<IRedirectSuggestionService>();
    private readonly IRedirectCultureResolver _culture = Substitute.For<IRedirectCultureResolver>();
    private readonly RedirectExtensibilityController _controller;

    public RedirectExtensibilityControllerTests()
    {
        _edition.Edition.Returns(RedirectEdition.Standard);
        _edition.EnabledFeatures.Returns(Array.Empty<string>());
        _registry.GetExtensions().Returns(Array.Empty<DashboardExtension>());
        _culture.ResolveCultureAsync(Arg.Any<string?>()).Returns(Task.FromResult<string?>(null));
        _controller = new RedirectExtensibilityController(_edition, _registry, _suggestions, _culture);
    }

    private static T Prop<T>(object o, string name) => (T)o.GetType().GetProperty(name)!.GetValue(o)!;

    [Fact]
    public void Capabilities_default_to_Standard_with_nothing_extra()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetCapabilities());

        Assert.Equal("Standard", Prop<string>(ok.Value!, "edition"));
        Assert.Empty(Prop<IEnumerable<string>>(ok.Value!, "features"));
        Assert.False(Prop<bool>(ok.Value!, "suggestionsAvailable"));
        Assert.Empty(Prop<IEnumerable<object>>(ok.Value!, "extensions"));
    }

    [Fact]
    public void Capabilities_report_edition_features_suggestions_and_extensions_with_slot_names()
    {
        _edition.Edition.Returns(RedirectEdition.Premium);
        _edition.EnabledFeatures.Returns(new[] { "smart-target-suggestions" });
        _suggestions.HasProviders.Returns(true);
        _registry.GetExtensions().Returns(new[]
        {
            new DashboardExtension("p.tab", DashboardSlot.Tab, "Migration", "/App_Plugins/P/p.js", "p-tab", 5),
            new DashboardExtension("p.row", DashboardSlot.MissedRowPanel, "Smart", "/App_Plugins/P/p.js", "p-row", 7)
        });

        var ok = Assert.IsType<OkObjectResult>(_controller.GetCapabilities());
        var extensions = Prop<IEnumerable<object>>(ok.Value!, "extensions").ToList();

        Assert.Equal("Premium", Prop<string>(ok.Value!, "edition"));
        Assert.True(Prop<bool>(ok.Value!, "suggestionsAvailable"));
        Assert.Equal("tab", Prop<string>(extensions[0], "slot"));
        Assert.Equal("missed-row-panel", Prop<string>(extensions[1], "slot"));
        Assert.Equal("/App_Plugins/P/p.js", Prop<string>(extensions[0], "moduleUrl"));
        Assert.Equal("p-tab", Prop<string>(extensions[0], "elementName"));
    }

    [Fact]
    public async Task Suggestions_require_a_path()
    {
        Assert.IsType<BadRequestObjectResult>(await _controller.GetSuggestions("  ", null, CancellationToken.None));
    }

    [Fact]
    public async Task Suggestions_pass_a_normalised_domain_and_resolved_culture_to_the_service()
    {
        _culture.ResolveCultureAsync("example.com").Returns(Task.FromResult<string?>("tr-tr"));
        var expected = new List<RedirectSuggestion> { new("/new", 0.9, "r") };
        _suggestions.GetSuggestionsAsync(Arg.Any<RedirectSuggestionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RedirectSuggestion>>(expected));

        var ok = Assert.IsType<OkObjectResult>(await _controller.GetSuggestions("/old", "Example.com:8080", CancellationToken.None));

        Assert.Same(expected, ok.Value);
        await _suggestions.Received(1).GetSuggestionsAsync(
            Arg.Is<RedirectSuggestionRequest>(r => r.Path == "/old" && r.Domain == "example.com" && r.Culture == "tr-tr"),
            Arg.Any<CancellationToken>());
    }
}
```

Also add a test to `RedirectApiControllerMissedCategoryTests` or a new file asserting that `GetMissed` invokes the enrichment service once with the mapped rows (substitute `IMissedRequestEnrichmentService`, `_missedRequestService.GetAll()` returns one row, assert `Received(1).EnrichAsync(Arg.Is<IReadOnlyList<MissedRequestDto>>(r => r.Count == 1), Arg.Any<CancellationToken>())`).

- [ ] **Step 2: Run to verify failure** (types/ctor missing).

- [ ] **Step 3: Implement**

`Controllers/RedirectExtensibilityController.cs`:

```csharp
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
                weight = e.Weight
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
```

`RedirectApiController`: add `using Umbraco.RedirectManager.Extensibility;`, a field + **last** ctor parameter `IMissedRequestEnrichmentService missedRequestEnrichment`, and replace `GetMissed`:

```csharp
    [HttpGet("missed")]
    public async Task<IActionResult> GetMissed(CancellationToken cancellationToken)
    {
        var rows = _missedRequestService.GetAll().Select(ToDto).ToList();
        await _missedRequestEnrichment.EnrichAsync(rows, cancellationToken);
        return Ok(rows);
    }
```
Update existing controller tests' constructor calls to pass `Substitute.For<IMissedRequestEnrichmentService>()` last, and any existing `GetMissed()` call sites in tests to `await GetMissed(CancellationToken.None)`.

`Composers/RedirectManagerComposer.cs` — add `using Microsoft.Extensions.DependencyInjection.Extensions;`, `using Umbraco.RedirectManager.Extensibility;` and, before the pipeline block:

```csharp
        // Extension points. The core registers no observers, providers,
        // enrichers or UI extensions itself; a commercial/third-party package
        // adds them from its own composer ([ComposeAfter(typeof(RedirectManagerComposer))]).
        builder.Services.TryAddSingleton<IRedirectEditionProvider, StandardEditionProvider>();
        builder.Services.Configure<RedirectExtensibilityOptions>(builder.Config.GetSection("RedirectManager:Extensibility"));
        builder.Services.AddSingleton<IMissedRequestObserverDispatcher, MissedRequestObserverDispatcher>();
        builder.Services.AddSingleton<IRedirectSuggestionService, RedirectSuggestionService>();
        builder.Services.AddSingleton<IMissedRequestEnrichmentService, MissedRequestEnrichmentService>();
        builder.Services.AddSingleton<IDashboardExtensionRegistry, DashboardExtensionRegistry>();
```

- [ ] **Step 4: Run** — `dotnet test Umbraco.RedirectManager.Tests` all green; build both TFMs.
- [ ] **Step 5: Commit** — `git commit -am "feat: expose capabilities and suggestions endpoints; enrich 404 log rows"`

---

### Task 4: Lit dashboard — render extensions only when present

**Files:** Modify `App_Plugins/RedirectManager/redirect-dashboard.js` (no automated UI harness exists; verified live in Task 6).

- [ ] **Step 1: Add the extension host element** (above the `RedirectManagerDashboard` class, after the imports/constants):

```javascript
const extensionModuleCache = new Map();

function loadExtensionModule(url) {
    if (!extensionModuleCache.has(url)) {
        extensionModuleCache.set(url, import(url));
    }
    return extensionModuleCache.get(url);
}

/**
 * Hosts one extension element (declared by a server-side IDashboardExtensionProvider).
 * Imports the extension's ES module, creates its custom element and hands it the
 * dashboard API plus (for the row panel) the 404 row. A failing extension shows a
 * small inline notice and never breaks the dashboard.
 */
class RedirectManagerExtensionHost extends LitElement {
    static properties = { ext: { type: Object }, api: { type: Object }, item: { type: Object } };

    static styles = css`:host { display: block; } .err { color: #991b1b; font-size: 11px; }`;

    constructor() { super(); this._el = null; this._failed = false; }

    updated() {
        if (this._failed) return;
        if (this._el) {
            this._el.api = this.api;
            if (this.item !== undefined) this._el.item = this.item;
            return;
        }
        if (!this.ext || this._loading) return;
        this._loading = true;
        loadExtensionModule(this.ext.moduleUrl)
            .then(() => {
                const el = document.createElement(this.ext.elementName);
                el.api = this.api;
                if (this.item !== undefined) el.item = this.item;
                this._el = el;
                this.renderRoot.appendChild(el);
            })
            .catch((error) => {
                console.error('Redirect Manager extension failed to load:', this.ext?.id, error);
                this._failed = true;
                this.requestUpdate();
            });
    }

    render() {
        return this._failed ? html`<span class="err">Extension unavailable</span>` : html``;
    }
}
customElements.define('redirect-manager-extension-host', RedirectManagerExtensionHost);
```

- [ ] **Step 2: Add state.** In `static properties` add `capabilities: { type: Object }`; in the constructor `this.capabilities = { edition: 'Standard', features: [], suggestionsAvailable: false, extensions: [] };`.

- [ ] **Step 3: Load capabilities.** In `connectedCallback()` add `this.loadCapabilities();` and add the method next to `loadStats()`:

```javascript
    async loadCapabilities() {
        try {
            const response = await this.authFetch('/umbraco/api/redirectmanager/capabilities');
            if (response.ok) {
                const caps = await response.json();
                this.capabilities = { ...this.capabilities, ...caps, extensions: caps.extensions || [] };
            }
        } catch (error) {
            console.error('Failed to load capabilities:', error);
        }
    }

    get extensionApi() {
        // Stable object handed to extension elements.
        if (!this._extensionApi) {
            this._extensionApi = {
                authFetch: (url, options) => this.authFetch(url, options),
                showMessage: (text, type) => this.showMessage(text, type),
                createRedirectFromMissed: (item) => this.createRedirectFromMissed(item),
                reloadMissedRequests: () => this.loadMissedRequests(),
                reloadRedirects: () => this.loadRedirects()
            };
        }
        return this._extensionApi;
    }

    get tabExtensions() {
        return this.capabilities.extensions.filter(e => e.slot === 'tab');
    }

    get rowPanelExtensions() {
        return this.capabilities.extensions.filter(e => e.slot === 'missed-row-panel');
    }
```

- [ ] **Step 4: Render extra tabs.** After the Overview `tab-btn` (inside `<div class="tabs">`) add:

```javascript
                ${this.tabExtensions.map(ext => html`
                    <button class="tab-btn ${this.activeTab === `ext:${ext.id}` ? 'active' : ''}"
                            @click=${() => { this.activeTab = `ext:${ext.id}`; }}>
                        ${ext.label}
                    </button>
                `)}
```
and, next to the other `activeTab === '…'` blocks, before the closing of the render template:

```javascript
            ${this.tabExtensions.map(ext => this.activeTab === `ext:${ext.id}` ? html`
                <redirect-manager-extension-host .ext=${ext} .api=${this.extensionApi}></redirect-manager-extension-host>
            ` : '')}
```

- [ ] **Step 5: Render the row panel column only when an extension exists.** In the 404 table `<thead>`, after the "Category" `<th>` add `${this.rowPanelExtensions.length ? html`<th>${this.rowPanelExtensions[0].label}</th>` : ''}`; in each row, after the category `<td>`, add:

```javascript
                                        ${this.rowPanelExtensions.length ? html`
                                            <td>
                                                ${this.rowPanelExtensions.map(ext => html`
                                                    <redirect-manager-extension-host .ext=${ext} .api=${this.extensionApi} .item=${item}></redirect-manager-extension-host>
                                                `)}
                                            </td>
                                        ` : ''}
```

- [ ] **Step 6: Syntax check** — `node --check App_Plugins/RedirectManager/redirect-dashboard.js` (it is an ES module: `cp` to a temp `.mjs` and check that file). Expected: no output.
- [ ] **Step 7: Commit** — `git commit -am "feat: dashboard renders server-declared extension tabs and a per-row panel"`

---

### Task 5: Sample extension (conformance harness + docs by example)

**Files:** Create `samples/RedirectManager.SampleExtension/` (class library, **not** packed, excluded from the core project's globs).

- [ ] **Step 1: Exclude `samples/**` from the core csproj.** Add to the existing exclude `ItemGroup`:

```xml
    <Compile Remove="samples/**" />
    <Content Remove="samples/**" />
    <EmbeddedResource Remove="samples/**" />
    <None Remove="samples/**" />
```

- [ ] **Step 2: Project** `samples/RedirectManager.SampleExtension/RedirectManager.SampleExtension.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <AddRazorSupportForMvc>true</AddRazorSupportForMvc>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\..\Umbraco.RedirectManager.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Code** `SampleExtension.cs` — one file registering every extension point deterministically:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.RedirectManager.Composers;
using Umbraco.RedirectManager.Extensibility;
using Umbraco.RedirectManager.Models;

namespace RedirectManager.SampleExtension;

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

public sealed class SampleMissCounter { public int Count; public string? LastReferrer; }

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

public sealed class SampleSuggestionProvider : IRedirectSuggestionProvider
{
    public string Name => "Sample";

    public Task<IReadOnlyList<RedirectSuggestion>> SuggestAsync(RedirectSuggestionRequest request, CancellationToken cancellationToken)
    {
        // Deterministic: the parent folder, and the path without its extension.
        var suggestions = new List<RedirectSuggestion>();
        var trimmed = request.Path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        if (slash > 0) suggestions.Add(new RedirectSuggestion(trimmed[..slash] + "/", 0.6, "Parent section"));
        var dot = trimmed.LastIndexOf('.');
        if (dot > slash) suggestions.Add(new RedirectSuggestion(trimmed[..dot], 0.8, "Same path without extension"));
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
```

- [ ] **Step 4: UI module** `samples/RedirectManager.SampleExtension/App_Plugins/RedirectManagerSample/sample-extension.js`:

```javascript
// Sample extension elements. `api` and (for rows) `item` are assigned by the dashboard's
// extension host. Row elements MUST load lazily — a 404 log can hold thousands of rows.
class RmSampleTab extends HTMLElement {
    set api(value) { this._api = value; this.render(); }
    async render() {
        if (!this._api || this._rendered) return;
        this._rendered = true;
        const response = await this._api.authFetch('/umbraco/api/redirectmanager/capabilities');
        const caps = await response.json();
        this.innerHTML = `<div id="rm-sample-tab-body" style="padding:16px">
            <h3>Sample extension tab</h3>
            <p>Edition: <strong>${caps.edition}</strong> · extensions: <strong>${caps.extensions.length}</strong></p></div>`;
    }
}
customElements.define('rm-sample-tab', RmSampleTab);

class RmSampleRow extends HTMLElement {
    set api(value) { this._api = value; this.draw(); }
    set item(value) { this._item = value; this.draw(); }
    draw() {
        if (!this._api || !this._item || this._drawn) return;
        this._drawn = true;
        const button = document.createElement('button');
        button.textContent = 'Suggest';
        button.className = 'rm-sample-suggest';
        button.addEventListener('click', async () => {
            const url = `/umbraco/api/redirectmanager/suggestions?path=${encodeURIComponent(this._item.path)}`;
            const response = await this._api.authFetch(url);
            const list = await response.json();
            button.insertAdjacentHTML('afterend',
                `<span class="rm-sample-result"> ${list.map(s => `${s.targetUrl} (${s.confidence})`).join(', ') || 'none'} · pathLength=${this._item.extensions?.['sample.pathLength']}</span>`);
        });
        this.appendChild(button);
    }
}
customElements.define('rm-sample-row', RmSampleRow);
```

- [ ] **Step 5: Build** — `dotnet build samples/RedirectManager.SampleExtension` succeeds; `dotnet build Umbraco.RedirectManager.csproj -f net8.0` still succeeds (samples excluded).
- [ ] **Step 6: Commit** — `git add samples Umbraco.RedirectManager.csproj && git commit -m "feat: add sample extension exercising every extension point"`

---

### Task 6: Live verification in a real Umbraco site (both states)

> Lesson recorded in memory: dashboard changes are verified in a running Umbraco site, not just unit tests.

**Files:** Create `scripts/dev/cdp.mjs` (see Step 1); no product code.

- [ ] **Step 1: Reusable CDP helper.** (Approach proven during the 404-categorization session; steps are recorded in the Obsidian wiki page `wiki/features/404-log-categorization.md`, section "Ekran görüntüsü alma yöntemi".) Write `scripts/dev/cdp.mjs` (Node 22+, native `WebSocket`) with subcommands `login`, `click-text <selector> <text>` (shadow-DOM-piercing, uses `getBoundingClientRect` + `Input.dispatchMouseEvent`), `eval <js>` (shadow-piercing helpers pre-injected), `shot <file>` and `set-viewport <w> <h>`, targeting `http://localhost:9222`. Header comment documents the launch line: `Google Chrome --remote-debugging-port=9222 --user-data-dir=<tmp> <url>` and why real mouse events are used.
- [ ] **Step 2: State A — no extension (regression).** Run the existing test site (`RedirectManagerTestSite/RmTestSite`) **without** the sample project reference. In the dashboard: assert the tab bar has exactly Redirects / 404 log / Overview, the 404 table has **no** extra column, `GET /umbraco/api/redirectmanager/missed` JSON has no `extensions` property, and `GET capabilities` returns `Standard`, `[]`, `suggestionsAvailable:false`, `extensions:[]`. Take a screenshot. Compare with the pre-change screenshot (`assets/4.png` layout).
- [ ] **Step 3: State B — sample extension.** Add `<ProjectReference Include="../../RedirectManager/samples/RedirectManager.SampleExtension/RedirectManager.SampleExtension.csproj" />` to `RmTestSite.csproj` and symlink `RmTestSite/App_Plugins/RedirectManagerSample` to the sample's folder (the dev site already symlinks the core's `App_Plugins`; if one symlink to the whole `App_Plugins` is in place, add the sample as a sibling folder link inside a real `App_Plugins` directory instead). Restart. Assert: a "Sample extension" tab appears and shows `Edition: Standard · extensions: 2`; the 404 table has a "Suggestions" column; clicking **Suggest** on `/assets/app.js` lists `/assets/app (0.8)` and `/assets/ (0.6)`; `missed` JSON rows carry `extensions["sample.pathLength"]`; hit a missing URL with a `Referer` header and confirm the observer counter increments (`observedMisses` grows). Take screenshots.
- [ ] **Step 4: Fault isolation.** Temporarily make the sample provider throw and the sample observer throw; confirm the 404 still returns 404, the dashboard still loads, and warnings (not errors) are logged. Revert.
- [ ] **Step 5: Revert the test site to State A**, commit `scripts/dev/cdp.mjs`.

---

### Task 7: Documentation + release

**Files:** Create `docs/EXTENDING.md`; modify `Umbraco.RedirectManager.csproj`, `App_Plugins/RedirectManager/umbraco-package.json`, `README.md`.

- [ ] **Step 1: `docs/EXTENDING.md`** — the public contract: each interface with a minimal example (copy from the sample), registration via a composer with `[ComposeAfter(typeof(RedirectManagerComposer))]`, singleton lifetime note (use `IServiceScopeFactory` for scoped work), the "fast and non-blocking observer" rule, lazy-loading rule for row panels, the `/App_Plugins/` module-URL rule, fail-isolation guarantees, and a **stability policy**: these interfaces follow semver — additive changes only within 1.x; breaking changes only in a major version with one minor of overlap.
- [ ] **Step 2: README** — add one bullet under Features: "**Extensible**: documented extension points (404 observers, suggestion providers, dashboard panels) — see `docs/EXTENDING.md`." (No mention of paid editions here; that comes with the Free Forever Promise in 0.3.)
- [ ] **Step 3: Version bump** to `1.11.0` in the three places (`.csproj` `<Version>`, `umbraco-package.json` `"version"` and the `?v=` query).
- [ ] **Step 4: Verify** — `dotnet build -f net8.0`, `dotnet build -f net10.0`, `dotnet test Umbraco.RedirectManager.Tests` all green.
- [ ] **Step 5: Commit + push main** — `chore: bump version to 1.11.0, document extension points`.
- [ ] **Step 6: Publish (explicit user confirmation required before the tag push).** Tag `v1.11.0`, push, watch the workflow run **by run id** (not "latest"), confirm nuget.org lists 1.11.0.

---

## Acceptance (maps to the spec's 0.2 goal)

- With no extension installed: unit tests green, `missed` JSON unchanged, dashboard DOM unchanged (verified live, Task 6 Step 2).
- With the sample extension: every extension point is exercised end to end in a real Umbraco site (Task 6 Step 3).
- A throwing/hanging extension never breaks a request, the dashboard or the core API (unit tests + Task 6 Step 4).
- Both target frameworks build; the AngularJS dashboard is untouched.
