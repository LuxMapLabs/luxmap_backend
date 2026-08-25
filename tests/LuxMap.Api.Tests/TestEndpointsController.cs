using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Api.Tests;

/// <summary>
/// Controller CHỈ tồn tại trong assembly test. Nạp vào host qua ApplicationPart nên
/// LuxMap.Api không phải mang endpoint giả — BE-04 là middleware, chưa phải endpoint.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/_test")]
[ApiVersion("1.0")]
public sealed class TestEndpointsController : ControllerBase
{
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("nổ có chủ đích");

    [HttpGet("known-error")]
    public IActionResult KnownError() => throw new LuxMapException(
        LuxMap.Shared.Contracts.Errors.ErrorCodes.BboxTooLarge,
        System.Net.HttpStatusCode.RequestEntityTooLarge,
        "Phóng to để xem chi tiết.",
        new Dictionary<string, object?> { ["pole_count"] = 4211 });

    [HttpGet("paged")]
    public ActionResult<PagedResult<string>> Paged([FromQuery] PageQuery page)
    {
        var request = page.ToPageRequest();
        return Ok(PagedResult<string>.From(request, total: 1337, items: ["FAULT-0001"]));
    }

    [HttpPost("validated")]
    public IActionResult Validated([FromBody] ValidatedBody body) => Ok(body);

    [HttpGet("ok")]
    public IActionResult Fine() => Ok(new { pole_id = "POLE-0001" });

    public sealed class ValidatedBody
    {
        [Required]
        [MinLength(10)]
        public string? Note { get; init; }

        [Range(0, 1)]
        public double StatusConfidence { get; init; }
    }
}
