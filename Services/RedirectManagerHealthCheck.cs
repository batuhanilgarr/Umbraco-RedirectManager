using Umbraco.Cms.Core.HealthChecks;

namespace Umbraco.RedirectManager.Services;

[HealthCheck(
    "E7C4A912-6B3D-4F81-9E05-8A2C7D619B34",
    "Redirect table accessible",
    Description = "Confirms the Redirect Manager database table exists and can be queried.",
    Group = "Data Integrity")]
public class RedirectManagerHealthCheck : HealthCheck
{
    private readonly IRedirectService _redirectService;

    public RedirectManagerHealthCheck(IRedirectService redirectService)
    {
        _redirectService = redirectService;
    }

    // Shared by both the net10.0+ GetStatusAsync override and the net8.0
    // GetStatus override below, so the actual check logic exists in exactly
    // one place despite the two TFMs requiring different override names.
    private HealthCheckStatus BuildStatus()
    {
        var accessible = _redirectService.CanAccessTable();

        return new HealthCheckStatus(
            accessible
                ? "The Redirect Manager database table is accessible."
                : "The Redirect Manager database table could not be accessed. This may mean the package's migration hasn't run yet, or the database connection is misconfigured.")
        {
            ResultType = accessible ? StatusResultType.Success : StatusResultType.Error
        };
    }

#if NET10_0_OR_GREATER
    // Umbraco.Cms.Core 17.1.0+'s HealthCheck base class replaced the
    // abstract GetStatus() from 13.9.2 with a virtual GetStatusAsync() --
    // GetStatus() no longer exists to override on this TFM.
    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync() =>
        Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { BuildStatus() });
#else
    public override Task<IEnumerable<HealthCheckStatus>> GetStatus() =>
        Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { BuildStatus() });
#endif

    // This check has no "Fix" action -- an inaccessible table means a
    // migration hasn't run or the DB connection itself is broken, neither of
    // which this in-process check can safely or usefully "fix" on the
    // admin's behalf (see design spec's "Decisions" section). BuildStatus()
    // above never adds any Actions to the returned HealthCheckStatus, so
    // Umbraco's dashboard never renders a button that could call this in the
    // first place; the throw is defensive, matching how a check with no
    // actions would naturally behave if ever miscalled. No #if needed here:
    // ExecuteAction's signature is identical on both TFMs (just virtual
    // rather than abstract on net10.0+), so one override satisfies both.
    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) =>
        throw new InvalidOperationException("This health check does not support any actions.");
}
