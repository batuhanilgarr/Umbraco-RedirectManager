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
}
