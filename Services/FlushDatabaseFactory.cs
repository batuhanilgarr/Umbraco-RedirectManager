using System.Data.Common;
using NPoco;
using Umbraco.Cms.Core.Configuration.Models;

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
    public static Database Create(ConnectionStrings connectionStrings)
    {
        // Takes the already-resolved ConnectionStrings snapshot (from
        // IOptionsMonitor<ConnectionStrings>.CurrentValue) rather than reading
        // raw IConfiguration ourselves. Umbraco normalizes this value during
        // startup — replacing the |DataDirectory| placeholder for SQLite,
        // forcing Mode=ReadWriteCreate when unset, and rewriting the legacy
        // System.Data.SqlClient provider name to Microsoft.Data.SqlClient — and
        // reusing that normalized value is the only way to stay correct across
        // every deployment shape (SQL Server, SQLite, legacy provider names)
        // instead of re-deriving Umbraco's config conventions by hand.
        if (string.IsNullOrWhiteSpace(connectionStrings.ConnectionString))
        {
            throw new InvalidOperationException("The Umbraco connection string is not configured.");
        }

        var providerName = string.IsNullOrWhiteSpace(connectionStrings.ProviderName)
            ? ConnectionStrings.DefaultProviderName
            : connectionStrings.ProviderName;

        // Umbraco's own SQL Server/SQLite composers register their DbProviderFactory
        // into the process-wide DbProviderFactories registry during startup, so by
        // the time these background services run (30+ seconds later) this lookup
        // succeeds — it relies on that registration, not on anything ambient.
        var providerFactory = DbProviderFactories.GetFactory(providerName);

        // Same resolution Umbraco's own UmbracoDatabaseFactory uses internally:
        // DatabaseType.Resolve(DbProviderFactory.GetType().Name, ProviderName).
        var databaseType = DatabaseType.Resolve(providerFactory.GetType().Name, providerName);

        // A brand-new Database instance every call: no shared/cached/singleton
        // state, no ambient scope, no ambient connection — genuinely independent.
        return new Database(connectionStrings.ConnectionString, databaseType, providerFactory);
    }
}
