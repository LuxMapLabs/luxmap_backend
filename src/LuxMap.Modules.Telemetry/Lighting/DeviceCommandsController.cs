using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Telemetry.Lighting;

/// <summary>
/// The device side of lighting commands — the HTTPS poll adapter (LIGHT-CTRL D-1). <c>Authorization: Device
/// &lt;node_id&gt;.&lt;secret&gt;</c> only (policy <see cref="DeviceAuth.Policy"/>); a user's Bearer token is a 401 here.
/// </summary>
/// <remarks>
/// 🔴 NEVER <c>[AllowAnonymous]</c> on this controller or its methods: it skips authorization altogether. The node id comes from
/// the authenticated principal, never from the request.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/device/commands")]
[Authorize(Policy = DeviceAuth.Policy)]
public sealed class DeviceCommandsController(LightingCommandService service, LightingOptions options) : ControllerBase
{
    /// <summary>
    /// This device's open commands, newest per relay — delivered again until acknowledged or expired; drop duplicates by
    /// <c>command_id</c> and anything with a <c>seq</c> not above the last executed on that relay.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<DeviceCommandBatch>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DeviceCommandBatch>> PollAsync(CancellationToken ct)
        => HttpChannel ? Ok(await service.PollAsync(NodeId, ct)) : NotFound();

    /// <summary>Reports the outcome AFTER executing. A repeat of the same report is 200; a closed command is 409 <c>COMMAND_CLOSED</c>.</summary>
    [HttpPost("{commandId}/ack")]
    [ProducesResponseType<DeviceAckResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceAckResult>> AckAsync(string commandId, [FromBody] DeviceAckRequest ack, CancellationToken ct)
        => HttpChannel ? Ok(await service.AckAsync(NodeId, commandId, ack, ct)) : NotFound();

    /// <summary>LC-12 M-10: one channel at a time — with <c>Lighting:Channel = mqtt</c> these endpoints do not exist for a device.</summary>
    private bool HttpChannel => options.Channel == LightingOptions.Http;

    private string NodeId => User.FindFirst(DeviceAuth.NodeIdClaim)?.Value
        ?? throw new InvalidOperationException("The DeviceOnly policy admitted a principal without a node id.");
}
