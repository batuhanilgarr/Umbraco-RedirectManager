namespace Umbraco.RedirectManager.Models;

public class RedirectEntryDto
{
    public int Id { get; set; }
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
}

public class CreateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
}

public class UpdateRedirectEntryDto
{
    public string OldUrl { get; set; } = string.Empty;
    public string? NewUrl { get; set; }
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
}
