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
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }

    // Populated only by Create/Update, only when this entry is an active
    // broad matcher (regex or wildcard) that also matches one or more
    // existing active exact rules. Null/absent everywhere else (GetAll,
    // the /test match endpoint, exact rules, inactive rules).
    public List<string>? OverlapWarnings { get; set; }
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
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
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
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
}
