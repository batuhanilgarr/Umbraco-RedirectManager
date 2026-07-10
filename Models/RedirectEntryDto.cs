namespace Umbraco.RedirectManager.Models;

public class RedirectEntryDto
{
    public int Id { get; set; }
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
    public int HitCount { get; set; } = 0;
    public DateTime? LastHitDate { get; set; }
    public int Hits7d { get; set; } = 0;
    public int Hits30d { get; set; } = 0;
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public int VariantBHitCount { get; set; } = 0;
    public DateTime? VariantBLastHitDate { get; set; }
    public bool PreserveQueryString { get; set; } = false;
}

public class CreateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public bool PreserveQueryString { get; set; } = false;
}

public class UpdateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public bool IsRegex { get; set; } = false;
    public string? VariantBUrl { get; set; }
    public int? VariantBWeight { get; set; }
    public bool PreserveQueryString { get; set; } = false;
}
