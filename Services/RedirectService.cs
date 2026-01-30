using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public class RedirectService : IRedirectService
{
    private readonly IScopeProvider _scopeProvider;

    public RedirectService(IScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public IEnumerable<RedirectEntry> GetAll()
    {
        using var scope = _scopeProvider.CreateScope();
        var results = scope.Database.Fetch<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} ORDER BY CreatedDate DESC");
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

    public RedirectEntry? GetByOldUrl(string oldUrl)
    {
        using var scope = _scopeProvider.CreateScope();
        var normalizedUrl = NormalizeUrl(oldUrl);
        var result = scope.Database.SingleOrDefault<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE OldUrl = @0 AND IsActive = 1", normalizedUrl);
        scope.Complete();
        return result;
    }

    public RedirectEntry Create(CreateRedirectEntryDto dto)
    {
        var entry = new RedirectEntry
        {
            OldUrl = NormalizeUrl(dto.OldUrl),
            NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeUrl(dto.NewUrl),
            StatusCode = ValidateStatusCode(dto.StatusCode),
            IsActive = dto.IsActive,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        using var scope = _scopeProvider.CreateScope();
        scope.Database.Insert(entry);
        scope.Complete();

        return entry;
    }

    public RedirectEntry? Update(int id, UpdateRedirectEntryDto dto)
    {
        using var scope = _scopeProvider.CreateScope();
        var existing = scope.Database.SingleOrDefault<RedirectEntry>(
            $"SELECT * FROM {RedirectEntry.TableName} WHERE Id = @0", id);

        if (existing == null)
        {
            scope.Complete();
            return null;
        }

        existing.OldUrl = NormalizeUrl(dto.OldUrl);
        existing.NewUrl = string.IsNullOrWhiteSpace(dto.NewUrl) ? null : NormalizeUrl(dto.NewUrl);
        existing.StatusCode = ValidateStatusCode(dto.StatusCode);
        existing.IsActive = dto.IsActive;
        existing.UpdatedDate = DateTime.UtcNow;

        scope.Database.Update(existing);
        scope.Complete();

        return existing;
    }

    public bool Delete(int id)
    {
        using var scope = _scopeProvider.CreateScope();
        var rowsAffected = scope.Database.Delete<RedirectEntry>(id);
        scope.Complete();
        return rowsAffected > 0;
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

    private static int ValidateStatusCode(int statusCode)
    {
        return statusCode switch
        {
            301 or 302 or 404 or 410 => statusCode,
            _ => 301
        };
    }
}
