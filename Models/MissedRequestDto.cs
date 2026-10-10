using System.Text.Json.Serialization;

namespace Umbraco.RedirectManager.Models;

public class MissedRequestDto
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public int HitCount { get; set; } = 1;
    public DateTime FirstSeenDate { get; set; }
    public DateTime LastSeenDate { get; set; }
    public string Category { get; set; } = nameof(MissedRequestCategory.Unclassified);

    /// <summary>
    /// Extra columns contributed by extensions (see IMissedRequestEnricher).
    /// Omitted from JSON when no extension adds anything, so the payload is
    /// identical to earlier versions for everyone not using extensions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Extensions { get; set; }

    public void SetExtension(string key, object? value)
    {
        Extensions ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        Extensions[key] = value;
    }
}
