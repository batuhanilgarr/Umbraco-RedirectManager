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
    public void Capabilities_expose_sort_settings_only_for_row_panels_with_a_valid_key()
    {
        _registry.GetExtensions().Returns(new[]
        {
            new DashboardExtension("p.row", DashboardSlot.MissedRowPanel, "Traffic", "/App_Plugins/P/p.js", "p-row", 7,
                SortKey: "searchConsole.impact", SortDefault: true),
            new DashboardExtension("p.bad", DashboardSlot.MissedRowPanel, "Bad", "/App_Plugins/P/p.js", "p-bad", 8,
                SortKey: "no spaces allowed"),
            new DashboardExtension("p.tab", DashboardSlot.Tab, "Tab", "/App_Plugins/P/p.js", "p-tab", 9,
                SortKey: "ignored.on.tabs"),
            new DashboardExtension("p.plain", DashboardSlot.MissedRowPanel, "Plain", "/App_Plugins/P/p.js", "p-plain", 10)
        });

        var ok = Assert.IsType<OkObjectResult>(_controller.GetCapabilities());
        var ext = Prop<IEnumerable<object>>(ok.Value!, "extensions").ToList();

        Assert.Equal("searchConsole.impact", Prop<string>(ext[0], "sortKey"));
        Assert.Equal("number", Prop<string>(ext[0], "sortType"));
        Assert.True(Prop<bool>(ext[0], "sortDefault"));
        foreach (var rest in ext.Skip(1))
        {
            Assert.Null(Prop<string?>(rest, "sortKey"));
            Assert.Null(Prop<string?>(rest, "sortType"));
            Assert.False(Prop<bool>(rest, "sortDefault"));
        }
    }

    [Theory]
    [InlineData("string", "string")]
    [InlineData("date", "date")]
    [InlineData("number", "number")]
    [InlineData("whatever", "number")]
    public void Sort_type_falls_back_to_number(string given, string expected)
        => Assert.Equal(expected, new DashboardExtension("p", DashboardSlot.MissedRowPanel, "L", "/App_Plugins/P/p.js", "p-row",
            SortKey: "k", SortType: given).EffectiveSortType);

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
