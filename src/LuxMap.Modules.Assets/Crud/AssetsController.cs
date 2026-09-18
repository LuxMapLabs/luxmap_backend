using System.Net;
using System.Text.Json;
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
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePoleAsync(
        [FromBody] CreatePoleRequest request, CancellationToken ct)
        => CreatedAsset("poles", await service.CreatePoleAsync(request, ct));

    [HttpPost("fixtures")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateFixtureAsync(
        [FromBody] CreateFixtureRequest request, CancellationToken ct)
        => CreatedAsset("fixtures", await service.CreateFixtureAsync(request, ct));

    /// <summary>
    /// Deletes a pole outright. <b>204</b>, or <b>409</b> when a foreign key refuses.
    /// </summary>
    /// <remarks>
    /// Poles are the one asset with a DELETE, and fixtures deliberately still have none: retiring a
    /// lamp is a real event that <c>removed_date</c> records, while a pole row typed in by mistake is
    /// not an event at all. Marking such a row retired would write down something that never happened.
    /// <para>
    /// Nothing checks in code whether the pole may go — <c>fault</c> and <c>lux_reading</c> hold it
    /// with RESTRICT, so the database answers, and the 409 carries the constraint that said no.
    /// </para>
    /// </remarks>
    [HttpDelete("poles/{poleId}")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePoleAsync(string poleId, CancellationToken ct)
    {
        await service.DeletePoleAsync(poleId, ct);
        return NoContent();
    }

    /// <summary>
    /// Sets or clears the pole's feeder. <b>204</b>, never the updated object.
    /// </summary>
    /// <remarks>
    /// The topology repair BE-13 and RQ2 both stand on: <c>pole.feeder_id</c> is nullable and the FO-26
    /// mock set carries none, so most poles arrive with no circuit recorded and something has to be
    /// able to fill it in afterwards.
    /// <para>
    /// Answers 204 with no body on purpose. Echoing the updated pole would publish a read shape, and
    /// that decision belongs to <b>BE-12b</b>.
    /// </para>
    /// </remarks>
    [HttpPut("poles/{poleId}/feeder")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetPoleFeederAsync(
        string poleId, [FromBody] SetPoleFeederRequest request, CancellationToken ct)
    {
        await service.SetPoleFeederAsync(poleId, request.ReadFeederId(), ct);
        return NoContent();
    }

    /// <summary>
    /// Retires a lamp by setting <c>removed_date</c>. There is no DELETE — the equipment history is
    /// the reason the table exists.
    /// </summary>
    [HttpPut("fixtures/{fixtureId}/removal")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
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

/// <summary>
/// Body of the feeder assignment endpoint: <c>{ "feeder_id": "FDR-001" }</c> or
/// <c>{ "feeder_id": null }</c>.
/// </summary>
/// <remarks>
/// ⚠️ <b>Held as a <see cref="JsonElement"/> so an ABSENT key and an explicit <c>null</c> stay
/// distinguishable.</b> Everywhere else in this module a required field is <c>nullable +
/// [Required]</c>, but that pattern cannot express this endpoint: <c>[Required]</c> rejects
/// <c>null</c>, and <c>null</c> is a legitimate value here — a <c>solar_all_in_one</c> pole is on no
/// circuit at all.
/// <para>
/// Dropping the distinction and reading a missing key as <c>null</c> was the alternative, and it
/// would mean an empty or malformed body silently CLEARS a pole's circuit. This module already
/// decided that question the other way for <c>SERVER_OWNED_FIELD</c>: reject loudly rather than act
/// on a guess about what the caller meant.
/// </para>
/// </remarks>
public sealed record SetPoleFeederRequest
{
    public JsonElement FeederId { get; init; }

    /// <summary>The feeder id, or <c>null</c> to clear. Throws when the key was not sent at all.</summary>
    public string? ReadFeederId() => FeederId.ValueKind switch
    {
        JsonValueKind.String => FeederId.GetString(),
        JsonValueKind.Null => null,
        JsonValueKind.Undefined => throw new LuxMapException(
            ErrorCodes.ValidationFailed,
            HttpStatusCode.BadRequest,
            "feeder_id is required. Send null to record that the pole is on no circuit.",
            new Dictionary<string, object?> { ["feeder_id"] = "missing" }),
        _ => throw new LuxMapException(
            ErrorCodes.ValidationFailed,
            HttpStatusCode.BadRequest,
            "feeder_id must be a string or null.",
            new Dictionary<string, object?> { ["feeder_id"] = FeederId.ValueKind.ToString().ToLowerInvariant() }),
    };
}

/// <summary>Body of the fixture retirement endpoint.</summary>
public sealed record RetireFixtureRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public DateOnly? RemovedDate { get; init; }
}
