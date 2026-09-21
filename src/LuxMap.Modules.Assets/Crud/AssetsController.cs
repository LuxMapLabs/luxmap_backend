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
    /// Replaces a road segment. <b>204</b>, never the updated object.
    /// </summary>
    /// <remarks>
    /// A FULL replacement: every writable field is sent every time, and <c>commune_id</c> is not one
    /// of them. See <see cref="UpdateSegmentRequest"/> for why, and for what a missing field means.
    /// <para>
    /// 204 with no body, like every other write in this controller. Echoing the updated segment would
    /// publish a read shape, and that decision belongs to <b>BE-12b</b>.
    /// </para>
    /// </remarks>
    [HttpPut("segments/{segmentId}")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSegmentAsync(
        string segmentId, [FromBody] UpdateSegmentRequest request, CancellationToken ct)
    {
        await service.UpdateSegmentAsync(segmentId, request, ct);
        return NoContent();
    }

    /// <summary>Replaces a feeder. <b>204</b>, never the updated object.</summary>
    /// <remarks>See <see cref="UpdateSegmentRequest"/> for the shared full-replacement rules.</remarks>
    [HttpPut("feeders/{feederId}")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateFeederAsync(
        string feederId, [FromBody] UpdateFeederRequest request, CancellationToken ct)
    {
        await service.UpdateFeederAsync(feederId, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Replaces a pole. <b>204</b>, never the updated object.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Omitting <c>feeder_id</c> CLEARS the pole's circuit</b>, because this is a full
    /// replacement rather than a patch. The narrow <c>PUT /assets/poles/{id}/feeder</c> is the
    /// endpoint for changing only the circuit, and it refuses a body that leaves the key out.
    /// </remarks>
    [HttpPut("poles/{poleId}")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePoleAsync(
        string poleId, [FromBody] UpdatePoleRequest request, CancellationToken ct)
    {
        await service.UpdatePoleAsync(poleId, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes a road segment. <b>204</b>, or <b>409</b> when a foreign key refuses.
    /// </summary>
    /// <remarks>
    /// Nothing checks in code whether the segment may go. <c>pole</c>, <c>fault</c> and
    /// <c>fault_cluster</c> hold it with RESTRICT — two of those in another module — so the database
    /// answers and the 409 carries the constraint that said no.
    /// </remarks>
    [HttpDelete("segments/{segmentId}")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteSegmentAsync(string segmentId, CancellationToken ct)
    {
        await service.DeleteSegmentAsync(segmentId, ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes a feeder. <b>204</b>, or <b>409</b> when poles are still wired to it.
    /// </summary>
    /// <remarks>
    /// <c>pole.feeder_id</c> is RESTRICT and nullable, so a feeder with poles on it is refused rather
    /// than the poles being quietly unwired. Clearing a circuit is a per-pole decision made through
    /// <c>PUT /assets/poles/{id}/feeder</c>.
    /// </remarks>
    [HttpDelete("feeders/{feederId}")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteFeederAsync(string feederId, CancellationToken ct)
    {
        await service.DeleteFeederAsync(feederId, ct);
        return NoContent();
    }

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

    // ── BE-13 topology ────────────────────────────────────────────────────────────────────────
    //
    // ⚠️ PROVISIONAL — the Contract specifies no topology endpoint at all. Proposed in
    // docs/review/BE-13-topology-shape.md, registered as drift 46, NOT stable until the next FW
    // confirms it. Anything built on top of these routes must say its foundation is temporary.
    //
    // NO role policy, exactly like the other GETs: SetFallbackPolicy already requires a login, and a
    // policy is one EXACT role, so putting MaintenanceEngineer here would lock out the administrator
    // and the managing authority (BE-12a, rule 4).

    /// <summary>
    /// Every pole hanging off one circuit — the query BE-13 exists for, and CV-15's input.
    /// </summary>
    /// <remarks>
    /// A feeder outside the caller's commune answers <b>404</b>, not 403: the query filter makes the
    /// row not exist for them, and Contract section 7 asks for absence rather than a refusal that
    /// would confirm the id is real somewhere else.
    /// </remarks>
    [HttpGet("feeders/{feederId}/poles")]
    [ProducesResponseType<PagedResult<TopologyPole>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TopologyPole>>> ListPolesOnFeederAsync(
        string feederId, PageQuery page, CancellationToken ct)
        => Ok(await service.ListPolesOnFeederAsync(feederId, page.ToPageRequest(), ct));

    /// <summary>Every pole on one road segment — CV-05's input.</summary>
    /// <remarks>
    /// ⚠️ The result may include poles belonging to a DIFFERENT commune than the segment's owner, and
    /// that is correct: <c>road_class = inter_commune</c> means the road runs between communes
    /// (BE-REVIEW-02, constraint 1). Only the electrical circuit has to match its pole's commune.
    /// </remarks>
    [HttpGet("segments/{segmentId}/poles")]
    [ProducesResponseType<PagedResult<TopologyPole>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TopologyPole>>> ListPolesOnSegmentAsync(
        string segmentId, PageQuery page, CancellationToken ct)
        => Ok(await service.ListPolesOnSegmentAsync(segmentId, page.ToPageRequest(), ct));

    /// <summary>
    /// Poles on no circuit: what CV-05 still has to place, plus every solar pole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The weakest part of the proposal, and it is flagged rather than hidden.</b> This path
    /// reads badly — these are not the poles OF any feeder. The alternatives were worse:
    /// <c>/assets/poles?feeder_id=none</c> bolts BE-13 onto the list endpoint whose shape is BE-12b's
    /// open question, and <c>/assets/poles/unassigned</c> takes a name out of the space BE-12b holds.
    /// Section 4 of the proposal asks the reviewer to choose; this route is the placeholder until
    /// they do.
    /// </para>
    /// <para>
    /// The only listing that carries <c>power_source</c>, because it is the only one where "solar, so
    /// no circuit" has to be told apart from "not assigned yet" — identical in the database otherwise.
    /// </para>
    /// </remarks>
    [HttpGet("feeders/poles")]
    [ProducesResponseType<PagedResult<TopologyPole>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<TopologyPole>>> ListPolesWithoutFeederAsync(
        [FromQuery] bool unassigned,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        PageQuery page,
        CancellationToken ct)
    {
        // Required and must be true. The route has no other meaning today, and refusing the bare path
        // keeps the door open for the reviewer to replace it without silently changing what an
        // existing caller gets back.
        if (!unassigned)
        {
            throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                System.Net.HttpStatusCode.BadRequest,
                "This listing only answers unassigned=true.",
                new Dictionary<string, object?> { ["unassigned"] = "must be true" });
        }

        return Ok(await service.ListPolesWithoutFeederAsync(Narrow(communeId), page.ToPageRequest(), ct));
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
