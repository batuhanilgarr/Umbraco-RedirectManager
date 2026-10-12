using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbraco.RedirectManager.Extensibility;
using Umbraco.RedirectManager.Middleware;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Composers;

public class RedirectManagerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IRedirectService, RedirectService>();
        builder.Services.AddScoped<IMissedRequestService, MissedRequestService>();
        builder.Services.AddSingleton<IRedirectHitTracker, RedirectHitTracker>();
        builder.Services.AddHostedService<RedirectHitFlushService>();
        builder.Services.AddSingleton<IMissedRequestTracker, MissedRequestTracker>();
        builder.Services.AddHostedService<MissedRequestFlushService>();

        builder.Services.Configure<RedirectBackupOptions>(builder.Config.GetSection("RedirectManager:Backup"));
        builder.Services.AddHostedService<RedirectCsvBackupService>();

        builder.Services.AddSingleton<IVariantBHitTracker, VariantBHitTracker>();
        builder.Services.AddHostedService<VariantBHitFlushService>();

        builder.Services.Configure<RedirectRateLimitOptions>(builder.Config.GetSection("RedirectManager:RateLimit"));
        builder.Services.AddSingleton<IRedirectRateLimiter, RedirectRateLimiter>();

        builder.Services.AddSingleton<IRedirectCultureResolver, RedirectCultureResolver>();

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<IRedirectTelemetrySettingsStore, RedirectTelemetrySettingsStore>();
        builder.Services.AddSingleton<IRedirectTelemetryPinger, RedirectTelemetryPinger>();
        builder.Services.AddHostedService<RedirectTelemetryService>();

        builder.Services.AddSingleton<IRedirectVersionChecker, RedirectVersionChecker>();
        builder.Services.AddHostedService<RedirectVersionCheckService>();

        // Extension points. The core registers no observers, providers,
        // enrichers or UI extensions itself; a commercial/third-party package
        // adds them from its own composer ([ComposeAfter(typeof(RedirectManagerComposer))]).
        builder.Services.TryAddSingleton<IRedirectEditionProvider, StandardEditionProvider>();
        builder.Services.Configure<RedirectExtensibilityOptions>(builder.Config.GetSection("RedirectManager:Extensibility"));
        builder.Services.AddSingleton<IMissedRequestObserverDispatcher, MissedRequestObserverDispatcher>();
        builder.Services.AddSingleton<IRedirectSuggestionService, RedirectSuggestionService>();
        builder.Services.AddSingleton<IMissedRequestEnrichmentService, MissedRequestEnrichmentService>();
        builder.Services.AddSingleton<IDashboardExtensionRegistry, DashboardExtensionRegistry>();

        builder.Services.Configure<UmbracoPipelineOptions>(options =>
        {
            options.PipelineFilters.Insert(0, new UmbracoPipelineFilter("RedirectManager")
            {
                PrePipeline = app =>
                {
                    app.UseMiddleware<RedirectMiddleware>();
                }
            });
        });
    }
}
