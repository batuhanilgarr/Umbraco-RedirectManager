using System.Text.RegularExpressions;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

public interface IMissedRequestService
{
    IEnumerable<MissedRequest> GetAll();
    bool Delete(int id);
    bool SetCategory(int id, MissedRequestCategory category);
    int BulkSetCategory(IEnumerable<int> ids, MissedRequestCategory category);
}

// Deliberately narrow: only the exact patterns named in the customer's feature
// request (scanner probes and static-asset extensions). Anything else stays
// Unclassified for a human to triage -- this is not meant to be a general WAF
// ruleset, just enough to clear the obvious noise automatically.
public static class MissedRequestClassifier
{
    private static readonly Regex MaliciousScannerPattern = new(
        @"\.php$|^/wp-|/\.env(/|$)|/\.git/|^/(admin|phpmyadmin|xmlrpc\.php)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MissingAssetPattern = new(
        @"\.(js|css|map|jpg|jpeg|png|gif|svg|webp|ico|woff|woff2|ttf)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static MissedRequestCategory Classify(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return MissedRequestCategory.Unclassified;
        }

        if (MaliciousScannerPattern.IsMatch(path))
        {
            return MissedRequestCategory.MaliciousScanner;
        }

        if (MissingAssetPattern.IsMatch(path))
        {
            return MissedRequestCategory.MissingAsset;
        }

        return MissedRequestCategory.Unclassified;
    }
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

    public bool SetCategory(int id, MissedRequestCategory category)
    {
        using var scope = _scopeProvider.CreateScope();
        var rowsAffected = scope.Database.Execute(
            $"UPDATE {MissedRequest.TableName} SET Category = @0 WHERE Id = @1",
            category.ToString(), id);
        scope.Complete();
        return rowsAffected > 0;
    }

    public int BulkSetCategory(IEnumerable<int> ids, MissedRequestCategory category)
    {
        var idList = ids?.Distinct().ToArray() ?? Array.Empty<int>();
        if (idList.Length == 0)
            return 0;

        using var scope = _scopeProvider.CreateScope();
        var args = new List<object> { category.ToString() };
        var placeholders = string.Join(",", idList.Select((_, i) => $"@{i + args.Count}"));
        args.AddRange(idList.Cast<object>());
        var sql = $"UPDATE {MissedRequest.TableName} SET Category = @0 WHERE Id IN ({placeholders})";
        var rowsAffected = scope.Database.Execute(sql, args.ToArray());
        scope.Complete();
        return rowsAffected;
    }
}
