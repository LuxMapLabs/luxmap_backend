using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Assets.Crud;

/// <summary>
/// Asset inventory management (BE-12a). NOT in Contract v1.1 — registered as drift.
/// </summary>
/// <remarks>
/// ⚠️ Routed under <c>/assets/</c> ON PURPOSE, and it must stay there. Contract section 2.1 already
/// specifies <c>GET /poles</c> as the map endpoint: a mandatory <c>bbox</c>, a GeoJSON
/// <c>FeatureCollection</c>, 413 past 2000 poles. That is BE-14's, and an inventory list answering
/// the same path would take it. These are two different surfaces for two different jobs.
/// <para>
/// <b>Permissions.</b> Writing is <see cref="LuxMapPolicies.Administrator"/> — the first production
/// use of the four BE-08 policies. Reads carry NO policy: <c>SetFallbackPolicy</c> already demands
/// authentication, and naming a role here would EXCLUDE the other three rather than set a floor.
/// Contract section 7 covers territory only and says nothing about who may write, so the split is
/// registered as drift.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/assets")]
public sealed class AssetsController(
    AssetCrudService service,
    ICommuneScopeAccessor scopeAccessor) : ControllerBase
{
    /// <summary>
    /// Ids only, paged. The full asset shape is <b>BE-12b</b>.
    /// </summary>
    /// <remarks>
    /// A deliberate placeholder, not a design. BE-12a owns requests and permissions; what a read
    /// returns is still under review, and shipping a guess would publish a shape the front end starts
    /// depending on. Ids are enough to confirm what an import wrote, and <c>PagedResult</c> is already
    /// the published envelope from Contract section 0.
    /// </remarks>
    [HttpGet("segments")]
    [ProducesResponseType<PagedResult<string>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<string>>> ListSegmentsAsync(
        [FromQuery(Name = "commune_id")] string[]? communeId, PageQuery page, CancellationToken ct)
        => Ok(await service.ListSegmentsAsync(Narrow(communeId), page.ToPageRequest(), ct));

    [HttpGet("feeders")]
    [ProducesResponseType<PagedResult<string>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<string>>> ListFeedersAsync(
        [FromQuery(Name = "commune_id")] string[]? communeId, PageQuery page, CancellationToken ct)
        => Ok(await service.ListFeedersAsync(Narrow(communeId), page.ToPageRequest(), ct));

    [HttpGet("poles")]
    [ProducesResponseType<PagedResult<string>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<string>>> ListPolesAsync(
        [FromQuery(Name = "commune_id")] string[]? communeId, PageQuery page, CancellationToken ct)
        => Ok(await service.ListPolesAsync(Narrow(communeId), page.ToPageRequest(), ct));

    [HttpPost("segments")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateSegmentAsync(
        [FromBody] CreateSegmentRequest request, CancellationToken ct)
        => CreatedAsset("segments", await service.CreateSegmentAsync(request, ct));

    [HttpPost("feeders")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateFeederAsync(
        [FromBody] CreateFeederRequest request, CancellationToken ct)
        => CreatedAsset("feeders", await service.CreateFeederAsync(request, ct));

    [HttpPost("poles")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePoleAsync(
        [FromBody] CreatePoleRequest request, CancellationToken ct)
        => CreatedAsset("poles", await service.CreatePoleAsync(request, ct));

    [HttpPost("fixtures")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateFixtureAsync(
        [FromBody] CreateFixtureRequest request, CancellationToken ct)
        => CreatedAsset("fixtures", await service.CreateFixtureAsync(request, ct));

    /// <summary>
    /// Retires a lamp by setting <c>removed_date</c>. There is no DELETE — the equipment history is
    /// the reason the table exists.
    /// </summary>
    [HttpPut("fixtures/{fixtureId}/removal")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetireFixtureAsync(
        string fixtureId, [FromBody] RetireFixtureRequest request, CancellationToken ct)
    {
        await service.RetireFixtureAsync(fixtureId, request.RemovedDate!.Value, ct);
        return NoContent();
    }

    /// <summary>
    /// 201 with a <c>Location</c> header and NO body.
    /// </summary>
    /// <remarks>
    /// The id is the one thing the caller cannot work out for itself, and the header carries it
    /// without committing to a representation BE-12b has not settled yet.
    /// </remarks>
    private IActionResult CreatedAsset(string collection, string id)
        => Created($"/api/v1/assets/{collection}/{id}", null);

    /// <summary>
    /// The <c>commune_id</c> query parameter NARROWS inside the permitted scope; it never widens it.
    /// Out of scope is 403 <c>COMMUNE_FORBIDDEN</c> (Contract section 7) — the query filter alone
    /// would answer 200 with an empty list, which tells the caller nothing.
    /// </summary>
    private IReadOnlyList<string>? Narrow(string[]? communeId)
        => CommuneFilter.Narrow(scopeAccessor.Scope, communeId);
}

/// <summary>Body of the fixture retirement endpoint.</summary>
public sealed record RetireFixtureRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public DateOnly? RemovedDate { get; init; }
}
