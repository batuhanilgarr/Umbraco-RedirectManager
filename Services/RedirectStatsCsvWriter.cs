using System.Text;

namespace Umbraco.RedirectManager.Services;

// Renders a RedirectStatsBuilder.Stats snapshot as a small CSV report —
// summary totals, then the top-10 and stale lists. Used by both the
// "Export overview" dashboard button and the periodic summary email.
internal static class RedirectStatsCsvWriter
{
    public static byte[] Write(RedirectStatsBuilder.Stats stats)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Redirect Manager - Overview report");
        sb.AppendLine($"Generated,{DateTime.UtcNow:u}");
        sb.AppendLine();
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"Total redirects,{stats.Total}");
        sb.AppendLine($"Active,{stats.Active}");
        sb.AppendLine($"Inactive,{stats.Inactive}");
        sb.AppendLine($"Active with 0 hits in last 30 days,{stats.StaleRedirects.Count}");
        sb.AppendLine();

        sb.AppendLine("Top 10 most-used redirects");
        sb.AppendLine("OldUrl,NewUrl,HitCount,LastHitDate");
        foreach (var r in stats.TopRedirects)
        {
            AppendRow(sb, r);
        }

        sb.AppendLine();
        sb.AppendLine("Active, but 0 hits in the last 30 days");
        sb.AppendLine("OldUrl,NewUrl,HitCount,LastHitDate");
        foreach (var r in stats.StaleRedirects)
        {
            AppendRow(sb, r);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendRow(StringBuilder sb, RedirectStatsBuilder.StatRow r)
    {
        sb.Append(EscapeCsv(r.OldUrl));
        sb.Append(',');
        sb.Append(EscapeCsv(r.NewUrl ?? string.Empty));
        sb.Append(',');
        sb.Append(r.HitCount);
        sb.Append(',');
        sb.Append(r.LastHitDate?.ToString("u") ?? "Never");
        sb.AppendLine();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        return value;
    }
}
