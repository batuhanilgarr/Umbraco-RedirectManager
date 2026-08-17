using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Umbraco.Cms.Core.Security;
using Umbraco.RedirectManager.Controllers;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Controllers;

public class RedirectApiControllerMissedCategoryTests
{
    private readonly IMissedRequestService _missedRequestService = Substitute.For<IMissedRequestService>();
    private readonly RedirectApiController _controller;

    public RedirectApiControllerMissedCategoryTests()
    {
        _controller = new RedirectApiController(
            Substitute.For<IRedirectService>(),
            _missedRequestService,
            Substitute.For<IRedirectTelemetryPinger>(),
            Substitute.For<IRedirectTelemetrySettingsStore>(),
            Substitute.For<IRedirectVersionChecker>(),
            Substitute.For<IBackOfficeSecurityAccessor>());
    }

    [Fact]
    public void SetMissedCategory_returns_ok_when_row_found()
    {
        _missedRequestService.SetCategory(5, MissedRequestCategory.Gone).Returns(true);

        var result = _controller.SetMissedCategory(5, new RedirectApiController.SetCategoryDto { Category = "Gone" });

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public void SetMissedCategory_returns_not_found_when_row_missing()
    {
        _missedRequestService.SetCategory(5, MissedRequestCategory.Gone).Returns(false);

        var result = _controller.SetMissedCategory(5, new RedirectApiController.SetCategoryDto { Category = "Gone" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void SetMissedCategory_returns_bad_request_for_invalid_category()
    {
        var result = _controller.SetMissedCategory(5, new RedirectApiController.SetCategoryDto { Category = "NotARealCategory" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void BulkSetMissedCategory_returns_updated_count()
    {
        _missedRequestService.BulkSetCategory(Arg.Any<IEnumerable<int>>(), MissedRequestCategory.MaliciousScanner).Returns(3);

        var result = _controller.BulkSetMissedCategory(new RedirectApiController.BulkCategoryDto
        {
            Ids = new List<int> { 1, 2, 3 },
            Category = "MaliciousScanner"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var updated = ok.Value!.GetType().GetProperty("updated")!.GetValue(ok.Value);
        Assert.Equal(3, updated);
    }
}
