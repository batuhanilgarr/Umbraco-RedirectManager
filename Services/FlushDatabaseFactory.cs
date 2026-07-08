using System.Data.Common;
using Microsoft.Extensions.Configuration;
using NPoco;

namespace Umbraco.RedirectManager.Services;

/// <summary>
/// Creates standalone NPoco <see cref="Database"/> instances for the background
/// flush services, deliberately bypassing Umbraco's <c>IScopeProvider</c> and
/// <c>IUmbracoDatabaseFactory</c> — both maintain "ambient" state (an
/// ambient-scope stack, and per Umbraco's own docs, a database connection
/// that's "static to the current thread") that is not safely isolated between
/// independently-scheduled background continuations once .NET's ThreadPool
/// reuses an OS thread across unrelated Tasks. That was confirmed via live
/// testing to corrupt Umbraco's own internal background jobs and crash the
/// whole application host. A database instance constructed directly from the
/// connection string, used, and disposed within a single method call shares no
/// ambient state with anything else in the process, so it cannot participate in
/// or be corrupted by that ambient-tracking bug.
/// </summary>
internal static class FlushDatabaseFactory
{
    // Mirrors Umbraco.Cms.Core.Configuration.Models.ConnectionStrings' own
    // conventions: the connection string lives at ConnectionStrings:umbracoDbDSN,
    // its provider name at ConnectionStrings:umbracoDbDSN_ProviderName (the
    // "_ProviderName" postfix is Umbraco's own ConnectionStrings.ProviderNamePostfix
    // constant), and "Microsoft.Data.SqlClient" is Umbraco's own
    // ConnectionStrings.DefaultProviderName fallback when the key is absent.
    private const string ConnectionStringName = "umbracoDbDSN";
    private const string DefaultProviderName = "Microsoft.Data.SqlClient";

    public static Database Create(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        var providerName = configuration[$"ConnectionStrings:{ConnectionStringName}_ProviderName"]
            ?? DefaultProviderName;

        // Umbraco's own SQL Server/SQLite composers register their DbProviderFactory
        // into the process-wide DbProviderFactories registry during startup
        // (see Umbraco.Cms.Persistence.SqlServer.UmbracoBuilderExtensions.AddUmbracoSqlServerSupport,
        // which calls DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", ...)),
        // so by the time these background services run this lookup succeeds — it
        // relies on that registration, not on anything ambient/thread-static.
        var providerFactory = DbProviderFactories.GetFactory(providerName);

        // Same resolution Umbraco's own UmbracoDatabaseFactory uses internally:
        // DatabaseType.Resolve(DbProviderFactory.GetType().Name, ProviderName).
        var databaseType = DatabaseType.Resolve(providerFactory.GetType().Name, providerName);

        // A brand-new Database instance every call: no shared/cached/singleton
        // state, no ambient scope, no ambient connection — genuinely independent.
        return new Database(connectionString, databaseType, providerFactory);
    }
}
