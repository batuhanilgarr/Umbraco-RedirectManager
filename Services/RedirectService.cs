using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectService : IRedirectService
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IMemoryCache _memoryCache;

    private const string ActiveRegexCacheKey = "RedirectManager.ActiveRegexEntries";
    private const string ActiveWildcardCacheKey = "RedirectManager.ActiveWildcardEntries";

    public RedirectService(IScopeProvider scopeProvider, IMemoryCache memoryCache)
    {
        _scopeProvider = scopeProvider;
        _memoryCache = memoryCache;
    }

    public IEnumerable<RedirectEntry> GetAll()
    {
        using var scope = _scopeProvider.CreateScope();
        var results = scope.Database.Fetch<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} ORDER BY CreatedDate DESC");
        scope.Complete();
        return results;
    }

    public IEnumerable<RedirectEntry> GetAllFiltered(string? query, int? statusCode, bool? isActive, bool? isRegex)
    {
        using var scope = _scopeProvider.CreateScope();

        var sql = $"SELECT * FROM {RedirectEntry.TableName} WHERE 1=1";
        var args = new List<object>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            sql += " AND (OldUrl LIKE @0 OR NewUrl LIKE @0 OR Description LIKE @0)";
            args.Add($"%{query.Trim()}%");
        }

        if (statusCode.HasValue)
        {
            sql += $" AND StatusCode = @{args.Count}";
            args.Add(statusCode.Value);
        }

        if (isActive.HasValue)
        {
            sql += $" AND IsActive = @{args.Count}";
            args.Add(isActive.Value ? 1 : 0);
        }

        if (isRegex.HasValue)
        {
            sql += $" AND IsRegex = @{args.Count}";
            args.Add(isRegex.Value ? 1 : 0);
        }

        sql += " ORDER BY CreatedDate DESC";

        var results = scope.Database.Fetch<RedirectEntry>(sql, args.ToArray());
        scope.Complete();
        return results;
    }

    public RedirectEntry? GetById(int id)
    {
        using var scope = _scopeProvider.CreateScope();
        var result = scope.Database.SingleOrDefault<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE Id = @0", id);
        scope.Complete();
        return result;
    }

    public RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null)
    {
        using var scope = _scopeProvider.CreateScope();
        var normalizedUrl = NormalizeUrl(oldUrl);
        var normalizedDomain = DomainNormalizer.Normalize(domain);
        var now = DateTime.UtcNow;

        RedirectEntry? result = null;
        if (normalizedDomain != null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND Domain = @1 AND IsActive = 1 AND IsRegex = 0 AND (ValidFrom IS NULL OR ValidFrom <= @2) AND (ValidUntil IS NULL OR ValidUntil >= @2)",
                normalizedUrl, normalizedDomain, now);
        }

        if (result == null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND (Domain IS NULL OR Domain = '') AND IsActive = 1 AND IsRegex = 0 AND (ValidFrom IS NULL OR ValidFrom <= @1) AND (ValidUntil IS NULL OR ValidUntil >= @1)",
                normalizedUrl, now);
        }

        scope.Complete();
        return result;
    }

    public RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null)
    {
        using var scope = _scopeProvider.CreateScope();
        var value = NormalizeOldUrl(oldUrl, isRegex);
        var normalizedDomain = DomainNormalizer.Normalize(domain);

        RedirectEntry? result;
        if (normalizedDomain != null)
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsRegex = @1 AND Domain = @2",
                value, isRegex ? 1 : 0, normalizedDomain);
        }
        else
        {
            result = scope.Database.SingleOrDefault<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsRegex = @1 AND (Domain IS NULL OR Domain = '')",
                value, isRegex ? 1 : 0);
        }

        scope.Complete();
        return result;
    }

    public IEnumerable<RedirectEntry> GetActiveRegexEntries()
    {
        return _memoryCache.GetOrCreate(ActiveRegexCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            using var scope = _scopeProvider.CreateScope();
            var now = DateTime.UtcNow;
            var results = scope.Database.Fetch<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE IsActive = 1 AND IsRegex = 1 AND (ValidFrom IS NULL OR ValidFrom <= @0) AND (ValidUntil IS NULL OR ValidUntil >= @0) ORDER BY CreatedDate DESC",
                now);
            scope.Complete();
            return results;
        }) ?? Enumerable.Empty<RedirectEntry>();
    }

    public IEnumerable<RedirectEntry> GetActiveWildcardEntries()
    {
        return _memoryCache.GetOrCreate(ActiveWildcardCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

            using var scope = _scopeProvider.CreateScope();
            var now = DateTime.UtcNow;
            var results = scope.Database.Fetch<RedirectEntry>(
                $"SELECT * FROM {RedirectEntry.TableName} WHERE IsActive = 1 AND IsRegex = 0 AND OldUrl LIKE '%*%' AND (ValidFrom IS NULL OR ValidFrom <= @0) AND (ValidUntil IS NULL OR ValidUntil >= @0) ORDER BY CreatedDate DESC",
                now);
            scope.Complete();
            return results;
        }) ?? Enumerable.Empty<RedirectEntry>();
    }

    public IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts()
    {
        using var scope = _scopeProvider.CreateScope();

        // "Last 7 days" / "last 30 days" both count today, so the cutoff is
        // today minus (window - 1) days.
        var cutoff7 = DateTime.UtcNow.Date.AddDays(-6);
        var cutoff30 = DateTime.UtcNow.Date.AddDays(-29);

        var rows = scope.Database.Fetch<HitWindowRow>(
            $@"SELECT RedirectId,
                      SUM(CASE WHEN HitDate >= @0 THEN HitCount ELSE 0 END) AS Last7,
                      SUM(CASE WHEN HitDate >= @1 THEN HitCount ELSE 0 END) AS Last30
               FROM {RedirectHitDaily.TableName}
               WHERE HitDate >= @1
               GROUP BY RedirectId",
            cutoff7, cutoff30);

        scope.Complete();

        return rows.ToDictionary(r => r.RedirectId, r => (r.Last7, r.Last30));
    }

    private sealed class HitWindowRow
    {
        public int RedirectId { get; set; }
        public int Last7 { get; set; }
        public int Last30 { get; set; }
    }

    public RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName)
    {
        var isRegex = dto.IsRegex;
        var entry = new RedirectEntry
        {
            OldUrl = NormalizeOldUrl(dto.OldUrl, isRegex),
            NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, isRegex),
            Domain = DomainNormalizer.Normalize(dto.Domain),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            StatusCode = ValidateStatusCode(dto.StatusCode),
            IsActive = dto.IsActive,
            IsRegex = isRegex,
            VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, isRegex),
            VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight,
            PreserveQueryString = dto.PreserveQueryString,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            CreatedBy = actorName,
            ModifiedBy = actorName,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        using var scope = _scopeProvider.CreateScope();
        scope.Database.Insert(entry);
        scope.Complete();

        InvalidateMatchCaches();

        return entry;
    }

    public RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName)
    {
        using var scope = _scopeProvider.CreateScope();
        var existing = scope.Database.SingleOrDefault<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE Id = @0", id);

        if (existing == null)
        {
            scope.Complete();
            return null;
        }

        existing.IsRegex = dto.IsRegex;
        existing.OldUrl = NormalizeOldUrl(dto.OldUrl, existing.IsRegex);
        existing.NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeNewUrl(dto.NewUrl, existing.IsRegex);
        existing.Domain = DomainNormalizer.Normalize(dto.Domain);
        existing.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        existing.StatusCode = ValidateStatusCode(dto.StatusCode);
        existing.IsActive = dto.IsActive;
        existing.VariantBUrl = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : NormalizeNewUrl(dto.VariantBUrl, existing.IsRegex);
        existing.VariantBWeight = string.IsNullOrWhiteSpace(dto.VariantBUrl) ? null : dto.VariantBWeight;
        existing.PreserveQueryString = dto.PreserveQueryString;
        existing.ValidFrom = dto.ValidFrom;
        existing.ValidUntil = dto.ValidUntil;
        existing.ModifiedBy = actorName;
        existing.UpdatedDate = DateTime.UtcNow;

        scope.Database.Update(existing);
        scope.Complete();

        InvalidateMatchCaches();

        return existing;
    }

    public bool Delete(int id)
    {
        using var scope = _scopeProvider.CreateScope();
        var rowsAffected = scope.Database.Delete<RedirectEntry>(id);
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateMatchCaches();
        }

        return rowsAffected > 0;
    }

    public int BulkDelete(IEnumerable<int> ids)
    {
        var idList = ids?.Distinct().ToArray() ?? Array.Empty<int>();
        if (idList.Length == 0)
            return 0;

        using var scope = _scopeProvider.CreateScope();
        var placeholders = string.Join(",", idList.Select((_, i) => $"@{i}"));
        var sql = $"DELETE FROM {RedirectEntry.TableName} WHERE Id IN ({placeholders})";
        var rowsAffected = scope.Database.Execute(sql, idList.Cast<object>().ToArray());
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateMatchCaches();
        }

        return rowsAffected;
    }

    public int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName)
    {
        var idList = ids?.Distinct().ToArray() ?? Array.Empty<int>();
        if (idList.Length == 0)
            return 0;

        using var scope = _scopeProvider.CreateScope();
        // actorName may be null here; NPoco maps a null CLR value in the params
        // array to a parameterized DBNull, the same as any other nullable column
        // written via Insert/Update elsewhere in this class -- no special-casing
        // needed for the null case.
        var args = new List<object> { isActive ? 1 : 0, DateTime.UtcNow, actorName };
        var placeholders = string.Join(",", idList.Select((_, i) => $"@{i + args.Count}"));
        args.AddRange(idList.Cast<object>());
        var sql = $"UPDATE {RedirectEntry.TableName} SET IsActive = @0, UpdatedDate = @1, ModifiedBy = @2 WHERE Id IN ({placeholders})";
        var rowsAffected = scope.Database.Execute(sql, args.ToArray());
        scope.Complete();

        if (rowsAffected > 0)
        {
            InvalidateMatchCaches();
        }

        return rowsAffected;
    }

    private void InvalidateMatchCaches()
    {
        _memoryCache.Remove(ActiveRegexCacheKey);
        _memoryCache.Remove(ActiveWildcardCacheKey);
    }

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        url = url.Trim().ToLowerInvariant();
        
        if (!url.StartsWith("/") && !url.StartsWith("http"))
            url = "/" + url;

        return url;
    }

    private static string NormalizeOldUrl(string oldUrl, bool isRegex)
    {
        if (isRegex)
        {
            return oldUrl?.Trim() ?? string.Empty;
        }

        return NormalizeUrl(oldUrl);
    }

    private static string NormalizeNewUrl(string newUrl, bool isRegex)
    {
        if (isRegex)
        {
            return newUrl?.Trim() ?? string.Empty;
        }

        return NormalizeUrl(newUrl);
    }

    private static int ValidateStatusCode(int statusCode)
    {
        return statusCode switch
        {
            301 or 302 or 404 or 410 => statusCode,
            _ => 301
        };
    }
}
