using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbraco.RedirectManager.Middleware;
using Umbraco.RedirectManager.Migrations;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Composers;

public class RedirectManagerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<IRedirectService, RedirectService>();
        
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, RedirectManagerMigrationNotificationHandler>();

        builder.Services.Configure<UmbracoPipelineOptions>(options =>
        {
            options.AddFilter(new UmbracoPipelineFilter("RedirectManager")
            {
                PrePipeline = app =>
                {
                    app.UseMiddleware<RedirectMiddleware>();
                }
            });
        });
    }
}
