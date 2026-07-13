using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
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
