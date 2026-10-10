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

    [Fact]
    public void Registry_drops_element_names_with_a_trailing_newline()
    {
        Assert.Empty(Registry(new FixedProvider(Ext(element: "x-tab\n"))).GetExtensions());
    }

    [Theory]
    [InlineData("/App_Plugins/%2e%2e/x.js")]
    [InlineData("/App_Plugins/x\t.js")]
    [InlineData("/App_Plugins/x .js")]
    [InlineData("/App_Plugins\\x.js")]
    [InlineData("/App_Plugins/x\u0001.js")]
    public void Registry_drops_obfuscated_module_urls(string module)
    {
        Assert.Empty(Registry(new FixedProvider(Ext(module: module))).GetExtensions());
    }

    [Fact]
    public void Registry_accepts_a_module_url_with_a_query_string()
    {
        Assert.Single(Registry(new FixedProvider(Ext(module: "/App_Plugins/X/x.js?v=1.2"))).GetExtensions());
    }

    // ---------- review hardening: suggestions ----------

    [Fact]
    public async Task A_provider_whose_Name_getter_throws_is_ignored_and_the_others_still_answer()
    {
        var bad = Substitute.For<IRedirectSuggestionProvider>();
        bad.Name.Returns(_ => throw new InvalidOperationException("name boom"));
        bad.SuggestAsync(Arg.Any<RedirectSuggestionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RedirectSuggestion>>(new[] { new RedirectSuggestion("/bad", 0.5, "r") }));
        var service = Suggestions(null, 5, bad, Provider("ok", new RedirectSuggestion("/new", 0.8, "r")));

        var result = await service.GetSuggestionsAsync(Request, CancellationToken.None);

        Assert.Contains(result, s => s.TargetUrl == "/new" && s.Source == "ok");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_non_positive_provider_timeout_falls_back_to_the_default(int milliseconds)
    {
        var service = Suggestions(TimeSpan.FromMilliseconds(milliseconds), 5, Provider("ok", new RedirectSuggestion("/new", 0.8, "r")));

        var result = await service.GetSuggestionsAsync(Request, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task Non_finite_confidence_is_mapped_to_a_serialisable_value()
    {
        var service = Suggestions(null, 5, Provider("A",
            new RedirectSuggestion("/nan", double.NaN, "r"),
            new RedirectSuggestion("/pos", double.PositiveInfinity, "r"),
            new RedirectSuggestion("/neg", double.NegativeInfinity, "r")));

        var result = await service.GetSuggestionsAsync(Request, CancellationToken.None);

        Assert.Equal(0d, result.Single(s => s.TargetUrl == "/nan").Confidence);
        Assert.Equal(1d, result.Single(s => s.TargetUrl == "/pos").Confidence);
        Assert.Equal(0d, result.Single(s => s.TargetUrl == "/neg").Confidence);
        Assert.Null(Record.Exception(() => JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    [Fact]
    public async Task A_provider_returning_null_is_handled()
    {
        var nullProvider = Substitute.For<IRedirectSuggestionProvider>();
        nullProvider.Name.Returns("null");
        nullProvider.SuggestAsync(Arg.Any<RedirectSuggestionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RedirectSuggestion>>(null!));
        var service = Suggestions(null, 5, nullProvider, Provider("ok", new RedirectSuggestion("/new", 0.8, "r")));

        var result = await service.GetSuggestionsAsync(Request, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task A_cooperative_provider_sees_its_token_cancelled_after_the_timeout()
    {
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = Substitute.For<IRedirectSuggestionProvider>();
        slow.Name.Returns("slow");
        slow.SuggestAsync(Arg.Any<RedirectSuggestionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var token = call.Arg<CancellationToken>();
                token.Register(() => observed.TrySetResult(true));
                return Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => (IReadOnlyList<RedirectSuggestion>)Array.Empty<RedirectSuggestion>());
            });
        var service = Suggestions(TimeSpan.FromMilliseconds(50), 5, slow);

        await service.GetSuggestionsAsync(Request, CancellationToken.None);

        Assert.True(await Task.WhenAny(observed.Task, Task.Delay(TimeSpan.FromSeconds(3))) == observed.Task);
    }

    // ---------- review hardening: enrichment ----------

    private static MissedRequestEnrichmentService Enrichment(TimeSpan timeout, params IMissedRequestEnricher[] enrichers) =>
        new(enrichers, Options.Create(new RedirectExtensibilityOptions { EnricherTimeout = timeout }),
            NullLogger<MissedRequestEnrichmentService>.Instance);

    [Fact]
    public async Task A_timed_out_enricher_cannot_write_into_the_real_rows_later()
    {
        var slow = Substitute.For<IMissedRequestEnricher>();
        slow.EnrichAsync(Arg.Any<IReadOnlyList<MissedRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var staged = call.Arg<IReadOnlyList<MissedRequestDto>>();
                await Task.Delay(300);
                foreach (var row in staged) row.SetExtension("late", 1);
            });
        var service = Enrichment(TimeSpan.FromMilliseconds(50), slow);
        var rows = new List<MissedRequestDto> { new() { Id = 1, Path = "/a" } };

        var ex = await Record.ExceptionAsync(() => service.EnrichAsync(rows, CancellationToken.None));
        await Task.Delay(600);

        Assert.Null(ex);
        Assert.True(rows[0].Extensions is null || !rows[0].Extensions!.ContainsKey("late"));
    }

    [Fact]
    public async Task A_good_enricher_still_merges_after_a_previous_one_timed_out()
    {
        var slow = Substitute.For<IMissedRequestEnricher>();
        slow.EnrichAsync(Arg.Any<IReadOnlyList<MissedRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var staged = call.Arg<IReadOnlyList<MissedRequestDto>>();
                await Task.Delay(300);
                foreach (var row in staged) row.SetExtension("late", 1);
            });
        var good = Substitute.For<IMissedRequestEnricher>();
        good.EnrichAsync(Arg.Any<IReadOnlyList<MissedRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                foreach (var row in call.Arg<IReadOnlyList<MissedRequestDto>>()) row.SetExtension("score", 7);
                return Task.CompletedTask;
            });
        var service = Enrichment(TimeSpan.FromMilliseconds(50), slow, good);
        var rows = new List<MissedRequestDto> { new() { Id = 1, Path = "/a" }, new() { Id = 2, Path = "/b" } };

        await service.EnrichAsync(rows, CancellationToken.None);

        Assert.All(rows, r => Assert.Equal(7, r.Extensions!["score"]));
    }

    [Fact]
    public async Task Enrichers_see_the_row_data_in_their_staging_copies()
    {
        var seen = new List<(int Id, string Path, string? Domain, int Hits, string Category)>();
        var enricher = Substitute.For<IMissedRequestEnricher>();
        enricher.EnrichAsync(Arg.Any<IReadOnlyList<MissedRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                foreach (var r in call.Arg<IReadOnlyList<MissedRequestDto>>())
                    seen.Add((r.Id, r.Path, r.Domain, r.HitCount, r.Category));
                return Task.CompletedTask;
            });
        var service = Enrichment(TimeSpan.FromSeconds(2), enricher);
        var rows = new List<MissedRequestDto>
        {
            new() { Id = 5, Path = "/p", Domain = "d.test", HitCount = 9, Category = "Bot" }
        };

        await service.EnrichAsync(rows, CancellationToken.None);

        Assert.Equal((5, "/p", (string?)"d.test", 9, "Bot"), seen.Single());
        Assert.Null(rows[0].Extensions);
    }

    [Fact]
    public async Task A_cooperative_enricher_sees_its_token_cancelled_after_the_timeout()
    {
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = Substitute.For<IMissedRequestEnricher>();
        slow.EnrichAsync(Arg.Any<IReadOnlyList<MissedRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().Register(() => observed.TrySetResult(true));
                return Task.Delay(TimeSpan.FromSeconds(10));
            });
        var service = Enrichment(TimeSpan.FromMilliseconds(50), slow);

        await service.EnrichAsync(new List<MissedRequestDto> { new() { Id = 1 } }, CancellationToken.None);

        Assert.True(await Task.WhenAny(observed.Task, Task.Delay(TimeSpan.FromSeconds(3))) == observed.Task);
    }
}
