using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Api.Tests;

/// <summary>
/// Controller CHỈ tồn tại trong assembly test. Nạp vào host qua ApplicationPart nên
/// LuxMap.Api không phải mang endpoint giả — BE-04 là middleware, chưa phải endpoint.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/_test")]
[ApiVersion("1.0")]
// Nhóm endpoint này test pipeline LỖI và PHÂN TRANG của BE-04, không phải xác thực. BE-08 đặt
// mặc định toàn ứng dụng là phải đăng nhập, nên phải mở tường minh.
[AllowAnonymous]
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

    /// <summary>
    /// DTO mẫu CHỈ có trong assembly test, để kiểm chứng cấu hình sinh spec: enum ra chuỗi,
    /// DateTime và DateOnly ra hai format khác nhau, tên property snake_case.
    /// </summary>
    [HttpGet("schema-probe")]
    public ActionResult<SchemaProbe> Schema() => Ok(new SchemaProbe());

    public sealed class SchemaProbe
    {
        public string PoleId { get; init; } = "POLE-0001";

        public FixtureStatus FixtureStatus { get; init; }

        public SourceChannel SourceChannel { get; init; }

        public DateTime LastSeenAt { get; init; }

        public DateOnly InstallDate { get; init; }

        public DateOnly? WarrantyExpiry { get; init; }

        public bool NearSensitivePoi { get; init; }
    }

    public sealed class ValidatedBody
    {
        [Required]
        [MinLength(10)]
        public string? Note { get; init; }

        [Range(0, 1)]
        public double StatusConfidence { get; init; }
    }
}
