using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Umbraco.Cms.Core.Security;
using Umbraco.RedirectManager.Controllers;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Controllers;

public class RedirectApiControllerTests
{
    private readonly IRedirectService _redirectService = Substitute.For<IRedirectService>();
    private readonly RedirectApiController _controller;

    public RedirectApiControllerTests()
    {
        _controller = new RedirectApiController(
            _redirectService,
            Substitute.For<IMissedRequestService>(),
            Substitute.For<IRedirectTelemetryPinger>(),
            Substitute.For<IRedirectTelemetrySettingsStore>(),
            Substitute.For<IRedirectVersionChecker>(),
            Substitute.For<IBackOfficeSecurityAccessor>());
    }

    private static CreateRedirectEntryDto ValidCreateDto() => new()
    {
        OldUrl = "/old-page",
        NewUrl = "/new-page",
        StatusCode = 301,
        IsActive = true,
        IsRegex = false
    };

    private static UpdateRedirectEntryDto ValidUpdateDto() => new()
    {
        OldUrl = "/old-page",
        NewUrl = "/new-page",
        StatusCode = 301,
        IsActive = true,
        IsRegex = false
    };

    [Fact]
    public void Create_EmptyOldUrl_ReturnsBadRequest()
    {
        var dto = ValidCreateDto();
        dto.OldUrl = "   ";

        var result = _controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Old URL is required", badRequest.Value);
    }

    [Fact]
    public void Create_301WithoutNewUrl_ReturnsBadRequest()
    {
        var dto = ValidCreateDto();
        dto.NewUrl = null;

        var result = _controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("New URL is required for redirect status codes", badRequest.Value);
    }

    [Fact]
    public void Create_InvalidRegexPattern_ReturnsBadRequest()
    {
        var dto = ValidCreateDto();
        dto.IsRegex = true;
        dto.OldUrl = "(unbalanced";

        var result = _controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid regex pattern", badRequest.Value);
    }

    [Fact]
    public void Create_NewUrlWithInvalidFormat_ReturnsBadRequest()
    {
        var dto = ValidCreateDto();
        dto.NewUrl = "not-a-valid-target";

        var result = _controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("New URL must start with '/' or 'http(s)://'", badRequest.Value);
    }

    [Fact]
    public void Create_DuplicateExists_ReturnsConflict()
    {
        var dto = ValidCreateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns(new RedirectEntry { Id = 99, OldUrl = dto.OldUrl });

        var result = _controller.Create(dto);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Create_ValidNoDuplicate_ReturnsOkAndCallsCreate()
    {
        var dto = ValidCreateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var created = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode };
        _redirectService.Create(dto, Arg.Any<string?>()).Returns(created);

        var result = _controller.Create(dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.Received(1).Create(dto, Arg.Any<string?>());
    }

    [Fact]
    public void Update_EmptyOldUrl_ReturnsBadRequest()
    {
        var dto = ValidUpdateDto();
        dto.OldUrl = "";

        var result = _controller.Update(1, dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Old URL is required", badRequest.Value);
    }

    [Fact]
    public void Update_301WithoutNewUrl_ReturnsBadRequest()
    {
        var dto = ValidUpdateDto();
        dto.NewUrl = null;

        var result = _controller.Update(1, dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("New URL is required for redirect status codes", badRequest.Value);
    }

    [Fact]
    public void Update_DuplicateExistsForDifferentId_ReturnsConflict()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns(new RedirectEntry { Id = 99, OldUrl = dto.OldUrl });

        var result = _controller.Update(1, dto);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Update_DuplicateIsTheSameRowBeingEdited_DoesNotReturnConflict()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns(new RedirectEntry { Id = 1, OldUrl = dto.OldUrl });
        var updated = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode };
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns(updated);

        var result = _controller.Update(1, dto);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Update_RowDoesNotExist_ReturnsNotFound()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns((RedirectEntry?)null);

        var result = _controller.Update(1, dto);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Update_ValidNoDuplicate_ReturnsOkAndCallsUpdate()
    {
        var dto = ValidUpdateDto();
        _redirectService.GetByOldUrlAndIsRegex(dto.OldUrl, dto.IsRegex, dto.Domain)
            .Returns((RedirectEntry?)null);
        var updated = new RedirectEntry { Id = 1, OldUrl = dto.OldUrl, NewUrl = dto.NewUrl, StatusCode = dto.StatusCode };
        _redirectService.Update(1, dto, Arg.Any<string?>()).Returns(updated);

        var result = _controller.Update(1, dto);

        Assert.IsType<OkObjectResult>(result);
        _redirectService.Received(1).Update(1, dto, Arg.Any<string?>());
    }
}
