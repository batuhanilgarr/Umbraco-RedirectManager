namespace Umbraco.RedirectManager.Services;

/// <summary>
/// Shared mutual-exclusion lock for the two background flush services
/// (<see cref="RedirectHitFlushService"/> and <see cref="MissedRequestFlushService"/>).
/// Each runs its own independent 30-second <c>PeriodicTimer</c>, started at nearly
/// the same moment during app startup, and each opens Umbraco <c>IScope</c>s to
/// write to the database. When their scope-creating windows overlap, Umbraco's
/// ambient-scope tracking can get corrupted — one service's scope disposal throws
/// "Scope X being disposed is not the Ambient Scope Y", and that corruption can
/// cascade into unrelated Umbraco-internal background jobs, crashing the whole
/// host. Both services take this lock around their entire scope-creating region
/// so their database work is never concurrent with each other.
/// </summary>
internal static class FlushCoordinator
{
    public static readonly object Lock = new();
}
