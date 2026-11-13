using System.Net;
using Asp.Versioning;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.IOT;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/iot")]
public sealed class IotDiagnosticsController(IotDiagnosticsService diagnostics) : ControllerBase
{
    public sealed record RegisterServerRequest(string ServerId, string BaseUrl, string? CommuneId = null, IotLocation? Location = null);
    public sealed record RegisteredServer(string ServerId);
    public sealed record RegisteredSource(string NodeId, string ServerId, string DeviceId);

    [HttpPost("servers")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<RegisteredServer>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterServerAsync(RegisterServerRequest request,
        [FromServices] IotRegistrationStore registry, [FromServices] IOptions<IotOptions> options, CancellationToken ct)
    {
        if (!ValidServerId(request.ServerId) || !IotOrigin.TryAllowed(options.Value, request.BaseUrl, out var uri))
            throw Invalid("Server ID không hợp lệ hoặc địa chỉ IoT server chưa được allowlist.");

        var id = request.ServerId.ToLowerInvariant();
        if (request.CommuneId is not null || request.Location is not null)
            ValidateLocation(request.CommuneId, request.Location);
        await registry.RegisterServerAsync(id, uri, request.CommuneId, request.Location, ct);
        return StatusCode(StatusCodes.Status201Created, new RegisteredServer(id));
    }

    public sealed record ServerLocationRequest(string CommuneId, IotLocation Location);

    [HttpPut("servers/{serverId}/location")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateServerLocationAsync(string serverId, ServerLocationRequest request,
        [FromServices] IotRegistrationStore registry, CancellationToken ct)
    {
        if (!ValidServerId(serverId)) throw Invalid("Server ID không hợp lệ.");
        ValidateLocation(request.CommuneId, request.Location);
        await registry.UpdateServerLocationAsync(serverId.ToLowerInvariant(), request.CommuneId, request.Location, ct);
        return NoContent();
    }

    private static void ValidateLocation(string? communeId, IotLocation? location)
    {
        if (string.IsNullOrWhiteSpace(communeId) ||
            location is not { Lat: >= -90 and <= 90, Lng: >= -180 and <= 180 })
            throw Invalid("Cần xã/phường và tọa độ GPS hợp lệ.");
    }

    [HttpGet("poles/{poleId}/control")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotPoleControl>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<IotPoleControl> PoleControlAsync(string poleId, CancellationToken ct)
        => diagnostics.PoleControlAsync(poleId, ct);

    [HttpPost("nodes/{nodeId}/source")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<RegisteredSource>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterNodeSourceAsync(string nodeId, NodeProfile profile,
        [FromServices] LuxMapDbContext db, [FromServices] IotRegistrationStore registry, CancellationToken ct)
    {
        if (!profile.IsValid() || !ValidServerId(profile.ServerId) ||
            profile.DeviceId.Length > 128 || profile.DeviceId is "." or ".." ||
            profile.DeviceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_' && c != '.'))
            throw Invalid("Thiếu server ID, thông tin thiết bị, tài sản hoặc ngưỡng đo hợp lệ.");

        if (!await db.Set<IotNode>().AnyAsync(node => node.NodeId == nodeId, ct))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy thiết bị IoT.");

        var validTopology = await (
            from control in db.Set<FeederControl>()
            join pole in db.Set<Pole>() on control.FeederId equals pole.FeederId
            where control.NodeId == nodeId && control.RelayNo == profile.RelayNo &&
                control.FeederId == profile.FeederId && pole.PoleId == profile.PoleId
            select pole.PoleId).AnyAsync(ct);
        if (!validTopology)
            throw Invalid("Trụ đèn phải thuộc feeder được gán cho relay của node IoT.");

        await registry.RegisterSourceAsync(nodeId, profile, ct);
        return Created($"/api/v1/iot/nodes/{Uri.EscapeDataString(nodeId)}/electrical",
            new RegisteredSource(nodeId, profile.ServerId!.ToLowerInvariant(), profile.DeviceId));
    }

    private static bool ValidServerId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 &&
        !id.Equals("default", StringComparison.OrdinalIgnoreCase) &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static LuxMapException Invalid(string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message);

    [HttpGet("nodes/{nodeId}/diagnostics")]
    [ProducesResponseType<IotDiagnosticResult>(StatusCodes.Status200OK)]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotDiagnosticResult> GetAsync(string nodeId, CancellationToken ct)
        => diagnostics.GetAsync(nodeId, ct);

    /// <summary>Gửi ON, OFF hoặc AUTO đến node. Đọc GET command để xem ACK.</summary>
    [HttpPost("nodes/{nodeId}/command")]
    [Authorize(Policy = LuxMapPolicies.ControlLighting)]
    [ProducesResponseType<IotCommandStatus>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IotCommandStatus>> SendCommandAsync(
        string nodeId, IotCommandRequest request, CancellationToken ct)
        => Accepted(await diagnostics.SendCommandAsync(nodeId, request, ct));

    /// <summary>Đọc lệnh hiện tại và ACK từ node.</summary>
    [HttpGet("nodes/{nodeId}/command")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotCommandStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotCommandStatus> CommandAsync(string nodeId, CancellationToken ct)
        => diagnostics.CommandAsync(nodeId, ct);

    /// <summary>Đọc lịch sử điện đã lưu. Dùng next_cursor làm after để lấy mẫu tiếp theo.</summary>
    [HttpGet("nodes/{nodeId}/electrical/history")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotTelemetryPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotTelemetryPage> TelemetryHistoryAsync(string nodeId, CancellationToken ct,
        [FromQuery] long after = 0, [FromQuery] int limit = 100)
        => diagnostics.TelemetryHistoryAsync(nodeId, after, limit, ct);

    [HttpGet("nodes/{nodeId}/electrical")]
    [ProducesResponseType<IotElectricalResult>(StatusCodes.Status200OK)]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotElectricalResult> ElectricalAsync(string nodeId, CancellationToken ct)
        => diagnostics.ElectricalAsync(nodeId, ct);

    [HttpGet("nodes")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    public Task<PagedResult<IotNodeStatus>> NodesAsync(PageQuery page, CancellationToken ct)
        => diagnostics.NodesAsync(page.ToPageRequest(), ct);

    [HttpGet("servers")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    public Task<IReadOnlyList<IotServerStatus>> ServersAsync(CancellationToken ct)
        => diagnostics.ServersAsync(ct);
}
