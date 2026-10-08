using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Telemetry.Lighting;

/// <summary>
/// ON / OFF / AUTO on testbed devices (LIGHT-CTRL 2b — drift LC-1…LC-11, SELF-SIGNED). Only a Manager presses
/// (<c>ControlLighting</c>); all four roles read the history (<c>ReadNetwork</c>, D-6).
/// </summary>
/// <remarks>
/// The response is 202: a command is ACCEPTED, not executed — the device fetches it, runs it and reports. The relay's real mode
/// is what the device reports (<c>feeder_control.control_mode</c>, I-6), never what was asked.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/lighting")]
public sealed class LightingController(LightingCommandService service) : ControllerBase
{
    /// <summary>What pressing would switch, what it would leave out and why, which segments it would also light. Writes nothing.</summary>
    [HttpGet("preview")]
    [Authorize(Policy = LuxMapPolicies.ControlLighting)]
    [ProducesResponseType<LightingPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LightingPreview>> PreviewAsync(
        [FromQuery(Name = "feeder_id")] string? feederId, [FromQuery(Name = "segment_id")] string? segmentId, CancellationToken ct)
        => Ok(await service.PreviewAsync(feederId, segmentId, ct));

    /// <summary>
    /// Presses ON / OFF / AUTO on a feeder or a segment: one command per switchable relay, the rest in <c>excluded</c>. 202 for a
    /// new press, 200 for a replay of the same <c>client_op_id</c>; 409 <c>NO_CONTROLLABLE_RELAY</c> when nothing can be switched.
    /// </summary>
    [HttpPost("commands")]
    [Authorize(Policy = LuxMapPolicies.ControlLighting)]
    [ProducesResponseType<LightingRequestResult>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<LightingRequestResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LightingRequestResult>> CreateAsync([FromBody] CreateLightingRequest request, CancellationToken ct)
    {
        var (result, replayed) = await service.CreateAsync(request, ct);
        return replayed ? Ok(result) : Accepted(result);
    }

    /// <summary>Commands in the caller's communes, newest first. <c>status</c> is as read: an open command past expiry is <c>expired</c>.</summary>
    [HttpGet("commands")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<PagedResult<LightingCommandItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<LightingCommandItem>>> ListAsync(
        [FromQuery(Name = "request_id")] Guid? requestId,
        [FromQuery(Name = "feeder_id")] string? feederId,
        [FromQuery(Name = "status")] string? status,
        PageQuery page,
        CancellationToken ct)
    {
        var parsed = status is null ? (LightingCommandStatus?)null : WireEnum.Parse<LightingCommandStatus>(status, "status");
        return Ok(await service.ListAsync(requestId, feederId, parsed, page.ToPageRequest(), ct));
    }
}
