using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IMissedRequestService
{
    IEnumerable<MissedRequest> GetAll();
    bool Delete(int id);
}

public class MissedRequestService : IMissedRequestService
{
    private readonly IScopeProvider _scopeProvider;

    public MissedRequestService(IScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public IEnumerable<MissedRequest> GetAll()
    {
        using var scope = _scopeProvider.CreateScope();
        var results = scope.Database.Fetch<MissedRequest>(
            $"SELECT * FROM {MissedRequest.TableName} ORDER BY HitCount DESC");
        scope.Complete();
        return results;
    }

    public bool Delete(int id)
    {
        using var scope = _scopeProvider.CreateScope();
        var rowsAffected = scope.Database.Delete<MissedRequest>(id);
        scope.Complete();
        return rowsAffected > 0;
    }
}
