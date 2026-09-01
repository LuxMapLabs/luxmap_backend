using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Api.Tests;

/// <summary>
/// A controller that exists ONLY in the test assembly. Loaded into the host through an
/// ApplicationPart so LuxMap.Api never has to ship a fake endpoint — BE-04 is middleware, not endpoints.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/_test")]
[ApiVersion("1.0")]
// These endpoints exercise BE-04's ERROR and PAGINATION pipelines, not authentication. BE-08 makes
// sign-in mandatory application-wide, so they must be opened explicitly.
[AllowAnonymous]
public sealed class TestEndpointsController : ControllerBase
{
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("deliberate blow-up");

    [HttpGet("known-error")]
    public IActionResult KnownError() => throw new LuxMapException(
        LuxMap.Shared.Contracts.Errors.ErrorCodes.BboxTooLarge,
        System.Net.HttpStatusCode.RequestEntityTooLarge,
        "Zoom in to see detail.",
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
    /// A sample DTO living ONLY in the test assembly, used to verify the spec generation settings:
    /// string enums, distinct formats for DateTime and DateOnly, and snake_case property names.
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
