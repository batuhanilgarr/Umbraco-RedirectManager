using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Route("umbraco/api/redirectmanager")]
public class RedirectApiController : Controller
{
    private readonly IRedirectService _redirectService;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectApiController(IRedirectService redirectService)
    {
        _redirectService = redirectService;
    }

    [HttpGet("getall")]
    public IActionResult GetAll(
        [FromQuery] string? q,
        [FromQuery] int? statusCode,
        [FromQuery] bool? isActive,
        [FromQuery] bool? isRegex)
    {
        var redirects = string.IsNullOrWhiteSpace(q) && statusCode == null && isActive == null && isRegex == null
            ? _redirectService.GetAll()
            : _redirectService.GetAllFiltered(q, statusCode, isActive, isRegex);

        return Ok(redirects.Select(r => new RedirectEntryDto
        {
            Id = r.Id,
            OldUrl = r.OldUrl,
            NewUrl = r.NewUrl,
            Description = r.Description,
            StatusCode = r.StatusCode,
            IsActive = r.IsActive,
            IsRegex = r.IsRegex,
            HitCount = r.HitCount,
            LastHitDate = r.LastHitDate
        }));
    }

    [HttpGet("get/{id:int}")]
    public IActionResult Get(int id)
    {
        var redirect = _redirectService.GetById(id);
        if (redirect == null)
            return NotFound();

        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex,
            HitCount = redirect.HitCount,
            LastHitDate = redirect.LastHitDate
        });
    }

    [HttpPost("create")]
    public IActionResult Create([FromBody] CreateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var validationError = ValidateRedirect(dto.OldUrl, dto.NewUrl, dto.StatusCode, dto.IsRegex);
        if (validationError != null)
            return BadRequest(validationError);

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex);
        if (duplicate != null)
            return Conflict("A redirect with the same Old URL and Match type already exists");

        var redirect = _redirectService.Create(dto);
        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex,
            HitCount = redirect.HitCount,
            LastHitDate = redirect.LastHitDate
        });
    }

    [HttpPut("update/{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var validationError = ValidateRedirect(dto.OldUrl, dto.NewUrl, dto.StatusCode, dto.IsRegex);
        if (validationError != null)
            return BadRequest(validationError);

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex);
        if (duplicate != null && duplicate.Id != id)
            return Conflict("A redirect with the same Old URL and Match type already exists");

        var redirect = _redirectService.Update(id, dto);
        if (redirect == null)
            return NotFound();

        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            Description = redirect.Description,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive,
            IsRegex = redirect.IsRegex,
            HitCount = redirect.HitCount,
            LastHitDate = redirect.LastHitDate
        });
    }

    [HttpDelete("delete/{id:int}")]
    public IActionResult Delete(int id)
    {
        var result = _redirectService.Delete(id);
        if (!result)
            return NotFound();

        return Ok();
    }

    [HttpGet("test")]
    public IActionResult Test([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest("Path is required");

        var normalizedPath = path.Trim().ToLowerInvariant();
        if (!normalizedPath.StartsWith("/"))
            normalizedPath = "/" + normalizedPath;

        var exact = _redirectService.GetByOldUrl(normalizedPath);
        if (exact != null)
        {
            return Ok(new
            {
                matched = true,
                matchType = "Exact",
                redirect = new RedirectEntryDto
                {
                    Id = exact.Id,
                    OldUrl = exact.OldUrl,
                    NewUrl = exact.NewUrl,
                    Description = exact.Description,
                    StatusCode = exact.StatusCode,
                    IsActive = exact.IsActive,
                    IsRegex = exact.IsRegex
                },
                computedNewUrl = exact.NewUrl
            });
        }

        foreach (var r in _redirectService.GetActiveRegexEntries())
        {
            if (string.IsNullOrWhiteSpace(r.OldUrl))
                continue;

            Regex regex;
            try
            {
                regex = new Regex(r.OldUrl, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch
            {
                continue;
            }

            bool matched;
            try
            {
                matched = regex.IsMatch(normalizedPath);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!matched)
                continue;

            var computedNewUrl = r.NewUrl;
            if ((r.StatusCode == 301 || r.StatusCode == 302) && !string.IsNullOrWhiteSpace(computedNewUrl))
            {
                try
                {
                    computedNewUrl = regex.Replace(normalizedPath, computedNewUrl);
                }
                catch
                {
                    // ignore
                }
            }

            return Ok(new
            {
                matched = true,
                matchType = "Regex",
                redirect = new RedirectEntryDto
                {
                    Id = r.Id,
                    OldUrl = r.OldUrl,
                    NewUrl = r.NewUrl,
                    Description = r.Description,
                    StatusCode = r.StatusCode,
                    IsActive = r.IsActive,
                    IsRegex = r.IsRegex
                },
                computedNewUrl
            });
        }

        return Ok(new { matched = false });
    }

    [HttpPost("bulk/delete")]
    public IActionResult BulkDelete([FromBody] BulkIdsDto dto)
    {
        var deleted = _redirectService.BulkDelete(dto.Ids);
        return Ok(new { deleted });
    }

    [HttpPost("bulk/activate")]
    public IActionResult BulkActivate([FromBody] BulkIdsDto dto)
    {
        var updated = _redirectService.BulkSetActive(dto.Ids, true);
        return Ok(new { updated });
    }

    [HttpPost("bulk/deactivate")]
    public IActionResult BulkDeactivate([FromBody] BulkIdsDto dto)
    {
        var updated = _redirectService.BulkSetActive(dto.Ids, false);
        return Ok(new { updated });
    }

    [HttpGet("export")]
    public IActionResult ExportCsv(
        [FromQuery] string? q,
        [FromQuery] int? statusCode,
        [FromQuery] bool? isActive,
        [FromQuery] bool? isRegex)
    {
        var redirects = string.IsNullOrWhiteSpace(q) && statusCode == null && isActive == null && isRegex == null
            ? _redirectService.GetAll()
            : _redirectService.GetAllFiltered(q, statusCode, isActive, isRegex);

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

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", "redirects.csv");
    }

    [HttpPost("import")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> ImportCsv([FromForm] IFormFile? file)
    {
        string content;

        if (Request.HasFormContentType)
        {
            if (file == null)
            {
                if (Request.Form.Files != null && Request.Form.Files.Count > 0)
                {
                    file = Request.Form.Files[0];
                }
                else
                {
                    return BadRequest("File is missing");
                }
            }

            if (file.Length == 0)
                return BadRequest("File is empty");

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            content = await reader.ReadToEndAsync();
        }
        else
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            content = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(content))
                return BadRequest("File is empty");
        }

        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return BadRequest("No rows found");

        var header = ParseCsvLine(lines[0]);
        var map = header
            .Select((h, i) => new { h = h.Trim(), i })
            .ToDictionary(x => x.h, x => x.i, StringComparer.OrdinalIgnoreCase);

        int created = 0;
        int updated = 0;
        int skipped = 0;

        for (var rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
            var cols = ParseCsvLine(lines[rowIndex]);
            if (cols.Count == 0)
                continue;

            var oldUrl = GetCol(map, cols, "OldUrl");
            if (string.IsNullOrWhiteSpace(oldUrl))
            {
                skipped++;
                continue;
            }

            var newUrl = GetCol(map, cols, "NewUrl");
            var description = GetCol(map, cols, "Description");
            var statusStr = GetCol(map, cols, "StatusCode");
            var isActiveStr = GetCol(map, cols, "IsActive");
            var isRegexStr = GetCol(map, cols, "IsRegex");

            var statusCode = int.TryParse(statusStr, out var sc) ? sc : 301;
            var isActiveVal = !bool.TryParse(isActiveStr, out var ia) || ia;
            var isRegexVal = bool.TryParse(isRegexStr, out var ir) && ir;

            var dto = new UpdateRedirectEntryDto
            {
                OldUrl = oldUrl,
                NewUrl = string.IsNullOrWhiteSpace(newUrl) ? null : newUrl,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                StatusCode = statusCode,
                IsActive = isActiveVal,
                IsRegex = isRegexVal
            };

            var existing = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex);
            if (existing == null)
            {
                _redirectService.Create(new CreateRedirectEntryDto
                {
                    OldUrl = dto.OldUrl,
                    NewUrl = dto.NewUrl,
                    Description = dto.Description,
                    StatusCode = dto.StatusCode,
                    IsActive = dto.IsActive,
                    IsRegex = dto.IsRegex
                });
                created++;
            }
            else
            {
                _redirectService.Update(existing.Id, dto);
                updated++;
            }
        }

        return Ok(new { created, updated, skipped });
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        return value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null)
            return result;

        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        result.Add(sb.ToString());
        return result;
    }

    private static string GetCol(Dictionary<string, int> map, List<string> cols, string name)
    {
        return map.TryGetValue(name, out var idx) && idx >= 0 && idx < cols.Count ? cols[idx] : string.Empty;
    }

    public class BulkIdsDto
    {
        public List<int> Ids { get; set; } = new();
    }

    private static string? ValidateRedirect(string oldUrl, string? newUrl, int statusCode, bool isRegex)
    {
        if (isRegex)
        {
            try
            {
                _ = new Regex(oldUrl.Trim(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch
            {
                return "Invalid regex pattern";
            }
        }

        if (statusCode == 301 || statusCode == 302)
        {
            if (string.IsNullOrWhiteSpace(newUrl))
                return "New URL is required for redirect status codes";

            var target = newUrl.Trim();
            if (!(target.StartsWith("/") || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return "New URL must start with '/' or 'http(s)://'";
        }

        return null;
    }
}
