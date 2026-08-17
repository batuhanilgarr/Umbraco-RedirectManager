using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class MissedRequestServiceTests
{
    [Fact]
    public void BulkSetCategory_with_empty_ids_returns_zero_without_touching_the_database()
    {
        // IMissedRequestService requires a real IScopeProvider/database to exercise the
        // SQL path meaningfully (see MissedRequestClassifierTests for the pure-logic
        // coverage, and RedirectApiControllerMissedCategoryTests -- a later task -- for
        // controller-level pass-through coverage using a substituted IMissedRequestService).
        // This test only pins down the short-circuit contract: an empty id list must
        // never reach the database.
        var service = new MissedRequestService(scopeProvider: null!);

        var updated = service.BulkSetCategory(Array.Empty<int>(), MissedRequestCategory.Gone);

        Assert.Equal(0, updated);
    }
}
