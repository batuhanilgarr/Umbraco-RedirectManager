# Extending Redirect Manager

BT.RedirectManager exposes a small set of extension points so that other packages (for example a
commercial edition, or your own agency package) can add behavior **without forking the core**. The
core registers none of them itself: with nothing installed, Redirect Manager behaves exactly as it
always has.

A working example that uses every extension point lives in
[`samples/RedirectManager.SampleExtension`](../samples/RedirectManager.SampleExtension).

| Extension point | Interface | What it is for |
|---|---|---|
| Edition | `IRedirectEditionProvider` | Report which edition is active and which feature keys are enabled |
| 404 observer | `IMissedRequestObserver` | React to a request that ended in a 404 (referrer capture, alerts) |
| Suggestion provider | `IRedirectSuggestionProvider` | Propose a redirect target for a 404 path |
| 404-row enricher | `IMissedRequestEnricher` | Add extra values to the rows of the 404 log |
| Dashboard extension | `IDashboardExtensionProvider` | Add a tab or a per-row panel to the dashboard |

Everything lives in the `Umbraco.RedirectManager.Extensibility` namespace.

## Registering an extension

Register your implementations from a composer that runs after the core's:

```csharp
[ComposeAfter(typeof(RedirectManagerComposer))]
public class MyExtensionComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IMissedRequestObserver, MyObserver>();
        builder.Services.AddSingleton<IRedirectSuggestionProvider, MySuggestionProvider>();
        builder.Services.AddSingleton<IMissedRequestEnricher, MyEnricher>();
        builder.Services.AddSingleton<IDashboardExtensionProvider, MyDashboardExtensions>();

        // Replaces the default Standard edition provider (the core uses TryAdd).
        builder.Services.AddSingleton<IRedirectEditionProvider, MyEditionProvider>();
    }
}
```

* Observers, providers, enrichers and dashboard extension providers are **multi-registrations**: add as many
  as you like.
* They are resolved as **singletons**. If you need scoped services (for example a database scope), inject
  `IServiceScopeFactory` and create a scope per call.
* The core is fail-open: an extension that throws or hangs is logged as a warning and skipped. It can never
  break a request, the dashboard, or the core API (see [Guarantees](#guarantees)).

## Edition

```csharp
public interface IRedirectEditionProvider
{
    RedirectEdition Edition { get; }                       // Standard, Premium or Company
    IReadOnlyCollection<string> EnabledFeatures { get; }   // stable feature keys, empty in Standard
}
```

Keep it cheap and never throw. The value is reported by `GET /umbraco/api/redirectmanager/capabilities`.
Licensing must be **fail-open**: an invalid or expired license may disable your own features, but it must never
stop redirects from resolving.

## 404 observers

```csharp
public sealed record MissedRequestContext(string Path, string? Domain, string? Referrer, string? UserAgent, DateTime TimestampUtc);

public interface IMissedRequestObserver
{
    void OnMissedRequest(MissedRequestContext context);
}
```

Called on the request path right after a 404 was recorded. **It must be fast and non-blocking**: enqueue
anything heavy (for example into a `Channel<T>` drained by a hosted service). The core calls observers
synchronously and does not wait for background work you start.

The core ships no observers. Referrer and user agent are passed to your observer, but only *you* decide what is
stored. If you persist them, document it for your users (and prefer host + path over full URLs, no IP addresses).

## Suggestion providers

```csharp
public interface IRedirectSuggestionProvider
{
    string Name { get; }
    Task<IReadOnlyList<RedirectSuggestion>> SuggestAsync(RedirectSuggestionRequest request, CancellationToken cancellationToken);
}
```

`GET /umbraco/api/redirectmanager/suggestions?path=/old&domain=example.com` runs every provider concurrently,
each limited to a timeout, then merges the results: invalid targets dropped, duplicates collapsed
**case-insensitively** keeping the highest confidence (an intentional choice for editor-facing suggestions, even
though URL paths are technically case-sensitive), confidence clamped to 0–1 (NaN becomes 0), best first, capped
at `MaxSuggestions`. Each suggestion's `Source` is set to your provider's `Name`.

## 404-row enrichers

```csharp
public interface IMissedRequestEnricher
{
    Task EnrichAsync(IReadOnlyList<MissedRequestDto> rows, CancellationToken cancellationToken);
}
```

Call `row.SetExtension("yourpackage.key", value)` for each row; the values arrive in the JSON as
`extensions`. Use namespaced keys and JSON-serializable values. When no enricher adds anything, the property is
omitted and the payload is unchanged.

Your enricher receives **staging copies** of the rows (Id, Path, Domain, hit count, dates, category). Values you
set are merged into the real rows only if you finish within the timeout; anything written after that is
discarded. Enrichers run one after another.

## Dashboard extensions

```csharp
public sealed record DashboardExtension(string Id, DashboardSlot Slot, string Label,
    string ModuleUrl, string ElementName, int Weight = 100,
    string? SortKey = null, string SortType = "number", bool SortDefault = false);
```

| Slot | Result |
|---|---|
| `Tab` | An extra tab after *Overview*; your element fills the tab body |
| `MissedRowPanel` | An extra last column in the 404 log; one element per row |

**Sortable row-panel columns (1.12+).** Set `SortKey` to a key your enricher writes with `SetExtension` and the
column header becomes sortable. `SortType` is `number` (default), `string` or `date`. Rows without a value sort
as "less than zero", so they end up last when descending. `SortDefault: true` sorts the 404 log by your column
(descending) until the user picks another one. A `SortKey` may only contain letters, digits, `.`, `_` and `-`;
an invalid key is ignored and the column stays unsortable.

Rules, enforced by the core (an extension that breaks them is dropped with a warning):

* `ModuleUrl` must be a path under `/App_Plugins/` (no scheme, `//`, `..`, backslash, `%`, whitespace or control
  characters). Query strings such as `?v=1.2` are allowed.
* `ElementName` must be a lower-case custom element name containing a hyphen (`my-package-tab`).
* `Id` must be unique; the first registration wins.

The dashboard imports your ES module once and creates your element. It then assigns two properties:

```js
element.api   // { authFetch(url, options), showMessage(text, type), createRedirectFromMissed(item, { newUrl }?),
              //   reloadMissedRequests(), reloadRedirects(), reloadCapabilities() }
element.item  // MissedRowPanel only: the 404 row ({ id, path, hitCount, category, extensions, ... })
```

Use `api.authFetch` for backoffice API calls (it adds the user's token). Style your element inside its own
shadow root.

**Row panels must load lazily.** A 404 log can hold thousands of rows and the dashboard creates one element
per row, so never fetch on connect; wait for a user action (a click, or an `IntersectionObserver`).

The dashboard extensions are rendered by the **Lit dashboard (Umbraco 17+)**. The legacy AngularJS dashboard
(Umbraco 13) ignores them; the server-side seams still compile for every supported target.

## Guarantees

* No behavior change when nothing is registered (same API payloads, same dashboard).
* Each extension point isolates failures: exceptions are logged as warnings, other extensions keep running.
* Timeouts (default 2 s) apply to suggestion providers and enrichers even if they ignore cancellation. Configure
  with `RedirectManager:Extensibility:SuggestionProviderTimeout`, `EnricherTimeout` and `MaxSuggestions`
  (a non-positive timeout falls back to the default). Observers cannot be timed out; keep them non-blocking.
* Verified in a real Umbraco site with a throwing and with a hanging extension: requests still return their
  normal status, the dashboard renders, and the APIs answer within the timeout.

## Stability policy

These interfaces and records are public API and follow semantic versioning:

* Within 1.x, changes are **additive only** (new members with default behavior, new optional record
  parameters).
* Breaking changes happen only in a major version, announced one minor version ahead.
* Adding an optional parameter to a record is source-compatible but not binary-compatible, so compile your
  extension against the same minor version of `BT.RedirectManager` you run it with, and construct the records
  with named arguments.

## Testing your extension

Build against the sample for a working reference. For dashboard changes, verify in a running Umbraco site and
not only in unit tests: `scripts/dev/cdp.mjs` drives a real Chrome (login, clicks that pierce shadow DOM,
screenshots), and the sample's `RM_SAMPLE_FAULT=throw|hang` switch shows how to prove a misbehaving extension
cannot take the host down.
