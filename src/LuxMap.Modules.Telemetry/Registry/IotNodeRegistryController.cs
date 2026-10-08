using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Telemetry.Registry;

/// <summary>
/// The device registry (LIGHT-CTRL 2a — drift LC-7, LC-10; SELF-SIGNED): IoT devices, their relays, their secrets.
/// </summary>
/// <remarks>
/// Under <c>/assets</c> because a device is equipment mounted in a cabinet (CABINET), managed like every other asset:
/// <c>ReadNetwork</c> reads, <c>ManageAssets</c> (Manager) writes. Lighting commands are a separate surface (2b).
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/assets/iot-nodes")]
public sealed class IotNodeRegistryController(IotNodeRegistryService service, ICommuneScopeAccessor scopeAccessor) : ControllerBase
{
    /// <summary>Devices in the caller's communes, paged, each with its wired relays. Never the secret.</summary>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<PagedResult<IotNodeItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<IotNodeItem>>> ListAsync(
        [FromQuery(Name = "commune_id")] string[]? communeId, PageQuery page, CancellationToken ct)
        => Ok(await service.ListAsync(CommuneFilter.Narrow(scopeAccessor.Scope, communeId), page.ToPageRequest(), ct));

    /// <summary>One device. Absent and out of scope answer the same 404.</summary>
    [HttpGet("{nodeId}")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotNodeItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IotNodeItem>> NodeAsync(string nodeId, CancellationToken ct)
        => Ok(await service.NodeAsync(nodeId, ct));

    /// <summary>Registers a device in a cabinet. 409 when the cabinet already carries one; the commune comes from the cabinet.</summary>
    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateIotNodeRequest request, CancellationToken ct)
    {
        var nodeId = await service.CreateAsync(request, ct);
        return Created($"/api/v1/assets/iot-nodes/{nodeId}", null);
    }

    /// <summary>Full replacement of <c>data_source</c> and <c>supports_remote_control</c>. Cabinet and commune are not writable.</summary>
    [HttpPut("{nodeId}")]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(string nodeId, [FromBody] UpdateIotNodeRequest request, CancellationToken ct)
    {
        await service.UpdateAsync(nodeId, request, ct);
        return NoContent();
    }

    /// <summary>Deletes a device. 409 while a relay is still wired.</summary>
    [HttpDelete("{nodeId}")]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(string nodeId, CancellationToken ct)
    {
        await service.DeleteAsync(nodeId, ct);
        return NoContent();
    }

    /// <summary>
    /// Wires a relay to a feeder of the device's own cabinet (CAB-4), or unwires it with <c>{ "feeder_id": null }</c>. The key is
    /// required.
    /// </summary>
    [HttpPut("{nodeId}/relays/{relayNo:int}")]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRelayAsync(string nodeId, int relayNo, [FromBody] SetRelayRequest request, CancellationToken ct)
    {
        await service.SetRelayAsync(nodeId, relayNo, request.ReadFeederId(), ct);
        return NoContent();
    }

    /// <summary>Issues a new device secret — shown ONCE in this response; the previous secret stops working at once.</summary>
    [HttpPost("{nodeId}/credential")]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType<IotNodeCredential>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IotNodeCredential>> IssueCredentialAsync(string nodeId, CancellationToken ct)
        => Ok(await service.IssueCredentialAsync(nodeId, ct));
}
