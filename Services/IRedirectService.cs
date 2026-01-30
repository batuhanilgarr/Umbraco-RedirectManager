using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectService
{
    IEnumerable<RedirectEntry> GetAll();
    RedirectEntry? GetById(int id);
    RedirectEntry? GetByOldUrl(string oldUrl);
    RedirectEntry Create(CreateRedirectEntryDto dto);
    RedirectEntry? Update(int id, UpdateRedirectEntryDto dto);
    bool Delete(int id);
}
