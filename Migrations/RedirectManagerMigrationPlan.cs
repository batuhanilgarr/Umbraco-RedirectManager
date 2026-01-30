using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Migrations;

public class RedirectManagerMigrationNotificationHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<RedirectManagerMigrationNotificationHandler> _logger;

    public RedirectManagerMigrationNotificationHandler(
        IScopeProvider scopeProvider,
        ILogger<RedirectManagerMigrationNotificationHandler> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeProvider.CreateScope();
            
            var tableExists = scope.Database.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @0", 
                RedirectEntry.TableName) > 0;

            if (!tableExists)
            {
                _logger.LogInformation("Creating RedirectManager table: {TableName}", RedirectEntry.TableName);
                
                scope.Database.Execute($@"
                    CREATE TABLE [{RedirectEntry.TableName}] (
                        [Id] INT IDENTITY(1,1) NOT NULL,
                        [OldUrl] NVARCHAR(2048) NOT NULL,
                        [NewUrl] NVARCHAR(2048) NULL,
                        [StatusCode] INT NOT NULL DEFAULT 301,
                        [CreatedDate] DATETIME NOT NULL DEFAULT GETUTCDATE(),
                        [UpdatedDate] DATETIME NOT NULL DEFAULT GETUTCDATE(),
                        [IsActive] BIT NOT NULL DEFAULT 1,
                        [IsRegex] BIT NOT NULL DEFAULT 0,
                        CONSTRAINT [PK_{RedirectEntry.TableName}] PRIMARY KEY ([Id])
                    )");
                
                _logger.LogInformation("RedirectManager table created successfully");
            }
            else
            {
                var isRegexColumnExists = scope.Database.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @0 AND COLUMN_NAME = @1",
                    RedirectEntry.TableName,
                    "IsRegex") > 0;

                if (!isRegexColumnExists)
                {
                    _logger.LogInformation("Adding missing column IsRegex to table: {TableName}", RedirectEntry.TableName);
                    scope.Database.Execute($"ALTER TABLE [{RedirectEntry.TableName}] ADD [IsRegex] BIT NOT NULL CONSTRAINT [DF_{RedirectEntry.TableName}_IsRegex] DEFAULT 0");
                }
            }
            
            scope.Complete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating RedirectManager table");
        }

        return Task.CompletedTask;
    }
}
