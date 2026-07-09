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
    private readonly IMissedRequestService _missedRequestService;
    private readonly IRedirectTelemetryPinger _telemetryPinger;
    private readonly IRedirectTelemetrySettingsStore _telemetrySettingsStore;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public RedirectApiController(
        IRedirectService redirectService,
        IMissedRequestService missedRequestService,
        IRedirectTelemetryPinger telemetryPinger,
        IRedirectTelemetrySettingsStore telemetrySettingsStore)
    {
        _redirectService = redirectService;
        _missedRequestService = missedRequestService;
        _telemetryPinger = telemetryPinger;
        _telemetrySettingsStore = telemetrySettingsStore;
    }

    // Fired by the dashboard on load. Shares the same 24h-per-site throttle
    // as the periodic background ping (RedirectTelemetryService) — whichever
    // fires first in a given window sends it, the other is a no-op. Never
    // awaited by the caller, so a slow/unreachable telemetry endpoint can't
    // delay the dashboard. No-ops entirely unless the site owner has
    // enabled telemetry via the toggle below.
    [HttpPost("telemetry/ping")]
    public IActionResult PingTelemetry()
    {
        _ = _telemetryPinger.PingIfDueAsync(Request.Host.Value, CancellationToken.None);
        return Ok();
    }

    [HttpGet("telemetry/status")]
    public IActionResult GetTelemetryStatus()
    {
        return Ok(new
        {
            enabled = _telemetrySettingsStore.IsEnabled(),
            decided = _telemetrySettingsStore.HasDecided()
        });
    }

    [HttpPost("telemetry/enable")]
    public IActionResult EnableTelemetry()
    {
        _telemetrySettingsStore.SetEnabled(true);
        _ = _telemetryPinger.PingIfDueAsync(Request.Host.Value, CancellationToken.None);
        return Ok(new { enabled = true });
    }

    [HttpPost("telemetry/disable")]
    public IActionResult DisableTelemetry()
    {
        _telemetrySettingsStore.SetEnabled(false);
        return Ok(new { enabled = false });
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

        var windowCounts = _redirectService.GetHitWindowCounts();

        return Ok(redirects.Select(r =>
        {
            var window = windowCounts.TryGetValue(r.Id, out var w) ? w : (Last7: 0, Last30: 0);
            return ToDto(r, window.Last7, window.Last30);
        }));
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var stats = BuildStats();

        return Ok(new
        {
            total = stats.Total,
            active = stats.Active,
            inactive = stats.Inactive,
            topRedirects = stats.TopRedirects,
            staleRedirects = stats.StaleRedirects
        });
    }

    [HttpGet("stats/export")]
    public IActionResult ExportStatsCsv()
    {
        var bytes = RedirectStatsCsvWriter.Write(BuildStats());
        return File(bytes, "text/csv", "redirect-overview.csv");
    }

    private RedirectStatsBuilder.Stats BuildStats()
    {
        var redirects = _redirectService.GetAll();
        var windowCounts = _redirectService.GetHitWindowCounts();
        return RedirectStatsBuilder.Build(redirects, windowCounts);
    }

    [HttpGet("get/{id:int}")]
    public IActionResult Get(int id)
    {
        var redirect = _redirectService.GetById(id);
        if (redirect == null)
            return NotFound();

        return Ok(ToDto(redirect));
    }

    [HttpPost("create")]
    public IActionResult Create([FromBody] CreateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var validationError = ValidateRedirect(dto.OldUrl, dto.NewUrl, dto.StatusCode, dto.IsRegex, dto.VariantBUrl, dto.VariantBWeight);
        if (validationError != null)
            return BadRequest(validationError);

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
        if (duplicate != null)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");

        var redirect = _redirectService.Create(dto);
        return Ok(ToDto(redirect));
    }

    [HttpPut("update/{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var validationError = ValidateRedirect(dto.OldUrl, dto.NewUrl, dto.StatusCode, dto.IsRegex, dto.VariantBUrl, dto.VariantBWeight);
        if (validationError != null)
            return BadRequest(validationError);

        var duplicate = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
        if (duplicate != null && duplicate.Id != id)
            return Conflict("A redirect with the same Old URL and Match type already exists for that domain");

        var redirect = _redirectService.Update(id, dto);
        if (redirect == null)
            return NotFound();

        return Ok(ToDto(redirect));
    }

    [HttpDelete("delete/{id:int}")]
    public IActionResult Delete(int id)
    {
        var result = _redirectService.Delete(id);
        if (!result)
            return NotFound();

        return Ok();
    }

    [HttpGet("missed")]
    public IActionResult GetMissed()
    {
        var missed = _missedRequestService.GetAll();
        return Ok(missed.Select(ToDto));
    }

    [HttpDelete("missed/{id:int}")]
    public IActionResult DismissMissed(int id)
    {
        var result = _missedRequestService.Delete(id);
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
                redirect = ToDto(exact),
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
                redirect = ToDto(r),
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

        var bytes = RedirectCsvWriter.Write(redirects);
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

            var existing = _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain);
            if (existing == null)
            {
                _redirectService.Create(new CreateRedirectEntryDto
                {
                    OldUrl = dto.OldUrl,
                    NewUrl = dto.NewUrl,
                    Domain = dto.Domain,
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

    private static RedirectEntryDto ToDto(RedirectEntry r, int hits7d = 0, int hits30d = 0)
    {
        return new RedirectEntryDto
        {
            Id = r.Id,
            OldUrl = r.OldUrl,
            NewUrl = r.NewUrl,
            Domain = r.Domain,
            Description = r.Description,
            StatusCode = r.StatusCode,
            IsActive = r.IsActive,
            IsRegex = r.IsRegex,
            HitCount = r.HitCount,
            LastHitDate = r.LastHitDate,
            Hits7d = hits7d,
            Hits30d = hits30d,
            VariantBUrl = r.VariantBUrl,
            VariantBWeight = r.VariantBWeight,
            VariantBHitCount = r.VariantBHitCount,
            VariantBLastHitDate = r.VariantBLastHitDate
        };
    }

    private static MissedRequestDto ToDto(MissedRequest m)
    {
        return new MissedRequestDto
        {
            Id = m.Id,
            Path = m.Path,
            Domain = m.Domain,
            HitCount = m.HitCount,
            FirstSeenDate = m.FirstSeenDate,
            LastSeenDate = m.LastSeenDate
        };
    }

    private static string? ValidateRedirect(string oldUrl, string? newUrl, int statusCode, bool isRegex, string? variantBUrl = null, int? variantBWeight = null)
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

        if (!string.IsNullOrWhiteSpace(variantBUrl))
        {
            if (isRegex)
                return "A/B testing (Variant B URL) isn't supported for regex rules";

            if (statusCode != 301 && statusCode != 302)
                return "A/B testing (Variant B URL) only applies to 301/302 redirect rules";

            var target = variantBUrl.Trim();
            if (!(target.StartsWith("/") || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return "Variant B URL must start with '/' or 'http(s)://'";

            if (variantBWeight is not int weight || weight < 0 || weight > 100)
                return "Variant B weight must be between 0 and 100";
        }

        return null;
    }
}
