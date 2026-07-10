using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectService
{
    IEnumerable<RedirectEntry> GetAll();
    IEnumerable<RedirectEntry> GetAllFiltered(string? query, int? statusCode, bool? isActive, bool? isRegex);
    RedirectEntry? GetById(int id);
    RedirectEntry? GetByOldUrl(string oldUrl, string? domain = null);
    RedirectEntry? GetByOldUrlAndIsRegex(string oldUrl, bool isRegex, string? domain = null);
    IEnumerable<RedirectEntry> GetActiveRegexEntries();
    IEnumerable<RedirectEntry> GetActiveWildcardEntries();
    RedirectEntry Create(CreateRedirectEntryDto dto, string? actorName);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto, string? actorName);
    bool Delete(int id);
    int BulkDelete(IEnumerable<int> ids);
    int BulkSetActive(IEnumerable<int> ids, bool isActive, string? actorName);
    IReadOnlyDictionary<int, (int Last7, int Last30)> GetHitWindowCounts();
}
