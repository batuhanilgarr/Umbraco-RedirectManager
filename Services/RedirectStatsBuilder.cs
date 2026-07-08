using NPoco;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

// Shared by the /stats API endpoint (via IRedirectService/IScopeProvider,
// request-scoped so ambient scope is safe there) and the periodic summary
// email (via a standalone FlushDatabaseFactory Database, since that runs
// from a BackgroundService) so the "what counts as stale/top" rules only
// exist in one place.
internal static class RedirectStatsBuilder
{
    public sealed record StatRow(int Id, string OldUrl, string? NewUrl, int HitCount, DateTime? LastHitDate);

    public sealed record Stats(int Total, int Active, int Inactive, IReadOnlyList<StatRow> TopRedirects, IReadOnlyList<StatRow> StaleRedirects);

    public static Stats Build(IEnumerable<RedirectEntry> redirects, IReadOnlyDictionary<int, (int Last7, int Last30)> windowCounts)
    {
        var list = redirects as IReadOnlyCollection<RedirectEntry> ?? redirects.ToList();

        var top = list
            .Where(r => r.HitCount > 0)
            .OrderByDescending(r => r.HitCount)
            .Take(10)
            .Select(ToRow)
            .ToList();

        // Active rules with zero hits in the trailing 30-day window — either a
        // stale rule (safe to retire) or a misconfigured one that isn't firing
        // when it should.
        var stale = list
            .Where(r => r.IsActive && (!windowCounts.TryGetValue(r.Id, out var w) || w.Last30 == 0))
            .OrderBy(r => r.LastHitDate ?? DateTime.MinValue)
            .Select(ToRow)
            .ToList();

        return new Stats(list.Count, list.Count(r => r.IsActive), list.Count(r => !r.IsActive), top, stale);
    }

    // Mirrors RedirectService.GetHitWindowCounts()'s SQL exactly, but against
    // a standalone Database instance instead of an Umbraco IScope.
    public static IReadOnlyDictionary<int, (int Last7, int Last30)> FetchHitWindowCounts(Database db)
    {
        var cutoff7 = DateTime.UtcNow.Date.AddDays(-6);
        var cutoff30 = DateTime.UtcNow.Date.AddDays(-29);

        var rows = db.Fetch<HitWindowRow>(
            $@"SELECT RedirectId,
                      SUM(CASE WHEN HitDate >= @0 THEN HitCount ELSE 0 END) AS Last7,
                      SUM(CASE WHEN HitDate >= @1 THEN HitCount ELSE 0 END) AS Last30
               FROM {RedirectHitDaily.TableName}
               WHERE HitDate >= @1
               GROUP BY RedirectId",
            cutoff7, cutoff30);

        return rows.ToDictionary(r => r.RedirectId, r => (r.Last7, r.Last30));
    }

    private static StatRow ToRow(RedirectEntry r) => new(r.Id, r.OldUrl, r.NewUrl, r.HitCount, r.LastHitDate);

    private sealed class HitWindowRow
    {
        public int RedirectId { get; set; }
        public int Last7 { get; set; }
        public int Last30 { get; set; }
    }
}
