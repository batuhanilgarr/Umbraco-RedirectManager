using System.Text;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

// Shared by the export API endpoint and the scheduled backup service so the
// CSV format only exists in one place.
internal static class RedirectCsvWriter
{
    public static byte[] Write(IEnumerable<RedirectEntry> redirects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OldUrl,NewUrl,Description,StatusCode,IsActive,IsRegex");

        foreach (var r in redirects)
        {
            sb.Append(EscapeCsv(r.OldUrl));
            sb.Append(',');
            sb.Append(EscapeCsv(r.NewUrl ?? string.Empty));
            sb.Append(',');
            sb.Append(EscapeCsv(r.Description ?? string.Empty));
            sb.Append(',');
            sb.Append(r.StatusCode);
            sb.Append(',');
            sb.Append(r.IsActive ? "true" : "false");
            sb.Append(',');
            sb.Append(r.IsRegex ? "true" : "false");
            sb.AppendLine();
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
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
