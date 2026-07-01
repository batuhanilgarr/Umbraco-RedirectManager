using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbraco.RedirectManager.Middleware;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Composers;

public class RedirectManagerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IRedirectService, RedirectService>();
        builder.Services.AddSingleton<IRedirectHitTracker, RedirectHitTracker>();
        builder.Services.AddHostedService<RedirectHitFlushService>();

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
