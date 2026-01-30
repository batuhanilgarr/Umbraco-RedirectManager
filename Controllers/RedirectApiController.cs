using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Controllers;

[Route("umbraco/api/redirectmanager")]
public class RedirectApiController : UmbracoApiController
{
    private readonly IRedirectService _redirectService;

    public RedirectApiController(IRedirectService redirectService)
    {
        _redirectService = redirectService;
    }

    [HttpGet("getall")]
    public IActionResult GetAll()
    {
        var redirects = _redirectService.GetAll();
        return Ok(redirects.Select(r => new RedirectEntryDto
        {
            Id = r.Id,
            OldUrl = r.OldUrl,
            NewUrl = r.NewUrl,
            StatusCode = r.StatusCode,
            IsActive = r.IsActive
        }));
    }

    [HttpGet("get/{id:int}")]
    public IActionResult Get(int id)
    {
        var redirect = _redirectService.GetById(id);
        if (redirect == null)
            return NotFound();

        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive
        });
    }

    [HttpPost("create")]
    public IActionResult Create([FromBody] CreateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var redirect = _redirectService.Create(dto);
        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive
        });
    }

    [HttpPut("update/{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdateRedirectEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OldUrl))
            return BadRequest("Old URL is required");

        if ((dto.StatusCode == 301 || dto.StatusCode == 302) && string.IsNullOrWhiteSpace(dto.NewUrl))
            return BadRequest("New URL is required for redirect status codes");

        var redirect = _redirectService.Update(id, dto);
        if (redirect == null)
            return NotFound();

        return Ok(new RedirectEntryDto
        {
            Id = redirect.Id,
            OldUrl = redirect.OldUrl,
            NewUrl = redirect.NewUrl,
            StatusCode = redirect.StatusCode,
            IsActive = redirect.IsActive
        });
    }

    [HttpDelete("delete/{id:int}")]
    public IActionResult Delete(int id)
    {
        var result = _redirectService.Delete(id);
        if (!result)
            return NotFound();

        return Ok();
    }
}
