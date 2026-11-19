using System.ComponentModel.DataAnnotations;
using System.Net;
using Asp.Versioning;
using LuxMap.Modules.IOT.Registry;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.IOT;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/iot")]
public sealed class IotDiagnosticsController(IotDiagnosticsService diagnostics) : ControllerBase
{
    public sealed record RegisterServerRequest(string ServerId, string? CommuneId = null, IotLocation? Location = null);
    public sealed record RegisteredServer(string ServerId);

    [HttpPost("servers")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<RegisteredServer>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterServerAsync(RegisterServerRequest request,
        [FromServices] IotRegistrationStore registry, [FromServices] IOptions<IotOptions> options, CancellationToken ct)
    {
        var settings = options.Value;
        if (!ValidServerId(request.ServerId))
            throw Invalid("Server ID không hợp lệ.");
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl;
        // Backwards compatible with deployments configured with a single allowlisted Bridge.
        if (baseUrl is null && settings.AllowedOrigins.Length == 1) baseUrl = settings.AllowedOrigins[0];
        if (!IotOrigin.TryAllowed(settings, baseUrl, out var uri))
            throw new LuxMapException("IOT_SOURCE_UNAVAILABLE", HttpStatusCode.ServiceUnavailable,
                "Địa chỉ Bridge mặc định chưa được cấu hình hợp lệ hoặc chưa được allowlist.");

        var id = request.ServerId.ToLowerInvariant();
        if (request.CommuneId is not null || request.Location is not null)
            ValidateLocation(request.CommuneId, request.Location);
        await registry.RegisterServerAsync(id, uri, request.CommuneId, request.Location, ct);
        return StatusCode(StatusCodes.Status201Created, new RegisteredServer(id));
    }


    /// <summary>Cấp MQTT key cho đúng server_id; Client ID và username bằng server_id.</summary>
    [HttpPost("servers/{serverId}/credential")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<IotGatewayCredential>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotGatewayCredential> IssueServerCredentialAsync(string serverId,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return diagnostics.IssueGatewayCredentialAsync(serverId, serverId.ToLowerInvariant(), configuration["Mqtt:ProvisioningKey"], ct);
    }

    [HttpGet("servers/{serverId}/credential")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<IotGatewayCredential>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotGatewayCredential> GetServerCredentialAsync(string serverId,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return diagnostics.GetGatewayCredentialAsync(serverId, serverId.ToLowerInvariant(), configuration["Mqtt:ProvisioningKey"], ct);
    }

    [HttpDelete("servers/{serverId}/credential")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RevokeServerCredentialAsync(string serverId,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        await diagnostics.RevokeGatewayCredentialAsync(serverId, serverId.ToLowerInvariant(), configuration["Mqtt:ProvisioningKey"], ct);
        return NoContent();
    }

    [HttpGet("servers/{serverId}/nodes")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<PagedResult<IotServerNode>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<IotServerNode>> ServerNodesAsync(string serverId, [FromQuery] PageQuery page, CancellationToken ct)
        => diagnostics.ServerNodesAsync(serverId, page.ToPageRequest(), ct);

    public sealed record ServerLocationRequest(string CommuneId, IotLocation Location);

    [Obsolete("Use server_id-only APIs. Only legacy aliases saved by earlier releases are supported.")]
    [HttpGet("servers/{serverId}/gateways/routes")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<IReadOnlyList<IotGatewayRoute>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IReadOnlyList<IotGatewayRoute>> GatewayRoutesAsync(string serverId,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
        => diagnostics.GatewayRoutesAsync(serverId, configuration["Mqtt:ProvisioningKey"], ct);

    [Obsolete("Use server_id-only APIs. Only legacy aliases saved by earlier releases are supported.")]
    [HttpPut("servers/{serverId}/gateways/{gatewayId}/route")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterGatewayRouteAsync(string serverId, string gatewayId,
        IotGatewayRouteRequest request, [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration,
        CancellationToken ct)
    {
        await diagnostics.RegisterGatewayRouteAsync(serverId, gatewayId, request, configuration["Mqtt:ProvisioningKey"], ct);
        return NoContent();
    }

    [Obsolete("Use server_id-only APIs. Only legacy aliases saved by earlier releases are supported.")]
    [HttpPost("servers/{serverId}/gateways/{gatewayId}/credential")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<IotGatewayCredential>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotGatewayCredential> IssueGatewayCredentialAsync(string serverId, string gatewayId,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return diagnostics.IssueGatewayCredentialAsync(serverId, gatewayId, configuration["Mqtt:ProvisioningKey"], ct);
    }


    /// <summary>Xem lại MQTT secret của gateway, dành cho Admin.</summary>
    [Obsolete("Use server_id-only APIs. Only legacy aliases saved by earlier releases are supported.")]
    [HttpGet("servers/{serverId}/gateways/{gatewayId}/credential")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<IotGatewayCredential>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotGatewayCredential> GetGatewayCredentialAsync(string serverId, string gatewayId,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return diagnostics.GetGatewayCredentialAsync(serverId, gatewayId, configuration["Mqtt:ProvisioningKey"], ct);
    }

    /// <summary>Thu hồi MQTT secret và ngắt gateway đang kết nối.</summary>
    [Obsolete("Use server_id-only APIs. Only legacy aliases saved by earlier releases are supported.")]
    [HttpDelete("servers/{serverId}/gateways/{gatewayId}/credential")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RevokeGatewayCredentialAsync(string serverId, string gatewayId,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        await diagnostics.RevokeGatewayCredentialAsync(serverId, gatewayId, configuration["Mqtt:ProvisioningKey"], ct);
        return NoContent();
    }

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
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotPoleControl>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<IotPoleControl> PoleControlAsync(string poleId, CancellationToken ct)
        => diagnostics.PoleControlAsync(poleId, ct);

    public sealed record RegisterNodeRequest
    {
        [Required] public string ServerId { get; init; } = "";
        [Required, MaxLength(32)] public string CabinetId { get; init; } = "";
        [Required, MaxLength(32)] public string FeederId { get; init; } = "";
        [Required] public string DeviceId { get; init; } = "";
        [Required] public double NominalVoltage { get; init; }
        [Required] public double NominalCurrentMa { get; init; }
        [Required] public double NominalPowerMw { get; init; }
    }

    public sealed record RegisteredNode(string NodeId, string ServerId, string DeviceId, string FeederId);

    /// <summary>Admin đăng ký node và đồng bộ quyền MQTT; gửi lại cùng cấu hình sửa tuyến bị thiếu.</summary>
    [HttpPost("nodes")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<RegisteredNode>(StatusCodes.Status200OK)]
    [ProducesResponseType<RegisteredNode>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterNodeAsync(RegisterNodeRequest request,
        [FromServices] IotRegistrationStore registry, [FromServices] IotNodeRegistryService nodes,
        [FromServices] IotFeederSourceLookup sources,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
    {
        if (!ValidServerId(request.ServerId) || !IotDiagnosticsService.ValidMqttId(request.DeviceId))
            throw Invalid("Server ID hoặc Device ID không hợp lệ.");
        var profile = new NodeProfile
        {
            ServerId = request.ServerId.ToLowerInvariant(),
            DeviceId = request.DeviceId,
            FeederId = request.FeederId,
            NominalVoltage = request.NominalVoltage,
            NominalCurrentMa = request.NominalCurrentMa,
            NominalPowerMw = request.NominalPowerMw,
        };
        if (!profile.IsValid())
            throw Invalid("Thông tin feeder hoặc ngưỡng đo không hợp lệ.");

        var (nodeId, _, created) = await registry.RegisterNodeAsync(new CreateIotNodeRequest
        {
            CabinetId = request.CabinetId, DataSource = DataSource.CalibrationRig,
            SupportsRemoteControl = true,
        }, profile, nodes, sources, (communeId, existingSource, token) =>
            diagnostics.EnsureGatewayRouteAsync(profile.ServerId, request.DeviceId, communeId,
                configuration["Mqtt:ProvisioningKey"], token, existingSource), ct);
        var result = new RegisteredNode(nodeId, profile.ServerId, profile.DeviceId, profile.FeederId);
        return created ? Created($"/api/v1/iot/nodes/{Uri.EscapeDataString(nodeId)}/electrical", result) : Ok(result);
    }

    /// <summary>Đọc cấu hình nguồn node đã lưu, gồm server, thiết bị MQTT, feeder và ba chỉ số danh định.</summary>
    /// <remarks>ReadNetwork trong phạm vi xã. Không gọi Bridge; nguồn đã gỡ hoặc chưa cấu hình trả 404.</remarks>
    [HttpGet("nodes/{nodeId}")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotNodeSourceDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<IotNodeSourceDetails> NodeSourceDetailsAsync(string nodeId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return diagnostics.NodeSourceDetailsAsync(nodeId, ct);
    }

    public sealed record UpdateNodeRequest
    {
        [Required] public string ServerId { get; init; } = "";
        [Required] public string DeviceId { get; init; } = "";
        [Required, MaxLength(32)] public string FeederId { get; init; } = "";
        [Required] public double NominalVoltage { get; init; }
        [Required] public double NominalCurrentMa { get; init; }
        [Required] public double NominalPowerMw { get; init; }
    }

    /// <summary>Manager cập nhật nguồn và xác nhận quyền MQTT của node trên server đã chọn.</summary>
    [HttpPut("nodes/{nodeId}")]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateNodeSourceAsync(string nodeId, UpdateNodeRequest request,
        [FromServices] IotRegistrationStore registry,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
    {
        var profile = new NodeProfile {
            ServerId = request.ServerId.ToLowerInvariant(), DeviceId = request.DeviceId, FeederId = request.FeederId,
            NominalVoltage = request.NominalVoltage, NominalCurrentMa = request.NominalCurrentMa,
            NominalPowerMw = request.NominalPowerMw,
        };
        if (!profile.IsValid() || !ValidServerId(profile.ServerId) ||
            !IotDiagnosticsService.ValidMqttId(profile.DeviceId))
            throw Invalid("Thiếu server ID, thông tin thiết bị, tài sản hoặc ngưỡng đo hợp lệ.");

        NodeProfile? previous = null;
        string? commune = null;
        bool routeChanged = false;
        try {
            await registry.SaveSourceAsync(nodeId, profile, createOnly: false, ct, async (communeId, old, token) => {
                previous = old; commune = communeId;
                await diagnostics.AuthorizeSourceChangeAsync(profile, old, communeId,
                    configuration["Mqtt:ProvisioningKey"], token);
                routeChanged = old is not null;
            });
        }
        catch {
            if (routeChanged && previous is not null && commune is not null) {
                try { await diagnostics.RestoreBindingAsync(previous, profile, commune, configuration["Mqtt:ProvisioningKey"], CancellationToken.None); }
                catch { throw new LuxMapException("IOT_SOURCE_UNAVAILABLE", HttpStatusCode.ServiceUnavailable,
                    "Lưu nguồn thất bại và chưa khôi phục được tuyến MQTT; cần thử lại cập nhật nguồn."); }
            }
            throw;
        }
        return NoContent();
    }

    [HttpDelete("nodes/{nodeId}")]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> DetachNodeAsync(string nodeId,
        [FromServices] IotRegistrationStore registry,
        [FromServices] LuxMap.Modules.IOT.Lighting.LightingCommandService lighting,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
        => DetachAsync(nodeId, registry, lighting, configuration, ct, detachCabinet: true);

    /// <summary>Gỡ nguồn và quyền MQTT của node khỏi feeder, giữ node trong tủ và giữ lịch sử.</summary>
    [HttpDelete("nodes/{nodeId}/feeder")]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> DetachNodeFeederAsync(string nodeId,
        [FromServices] IotRegistrationStore registry,
        [FromServices] LuxMap.Modules.IOT.Lighting.LightingCommandService lighting,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct)
        => DetachAsync(nodeId, registry, lighting, configuration, ct, detachCabinet: false);

    private async Task<IActionResult> DetachAsync(string nodeId, IotRegistrationStore registry,
        LuxMap.Modules.IOT.Lighting.LightingCommandService lighting,
        Microsoft.Extensions.Configuration.IConfiguration configuration, CancellationToken ct, bool detachCabinet)
    {
        NodeProfile? previous = null;
        string? commune = null;
        bool routeChanged = false;
        try {
            await registry.DetachNodeAsync(nodeId, lighting, ct, async (communeId, old, token) => {
                previous = old; commune = communeId;
                await diagnostics.DetachBindingAsync(old, communeId, configuration["Mqtt:ProvisioningKey"], token);
                routeChanged = true;
            }, detachCabinet: detachCabinet);
        }
        catch {
            if (routeChanged && previous is not null && commune is not null) {
                try { await diagnostics.RestoreBindingAsync(previous, null, commune, configuration["Mqtt:ProvisioningKey"], CancellationToken.None); }
                catch { throw new LuxMapException("IOT_SOURCE_UNAVAILABLE", HttpStatusCode.ServiceUnavailable,
                    "Gỡ nguồn thất bại và chưa khôi phục được tuyến MQTT; cần thử lại."); }
            }
            throw;
        }
        return NoContent();
    }

    private static bool ValidServerId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 && char.IsAsciiLetterOrDigit(id[0]) &&
        !id.Equals("default", StringComparison.OrdinalIgnoreCase) &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static LuxMapException Invalid(string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message);

    /// <summary>Chẩn đoán tổng hợp từ job nền, có phân trang và phạm vi xã.</summary>
    [HttpGet("diagnostics/summary")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<PagedResult<IotDiagnosticSummaryItem>>(StatusCodes.Status200OK)]
    public Task<PagedResult<IotDiagnosticSummaryItem>> SummaryAsync(
        [FromQuery] PageQuery page, [FromServices] IotDiagnosticSummary summary, CancellationToken ct)
        => summary.ListAsync(page.ToPageRequest(), ct);

    /// <summary>Lỗi hiện tại của feeder, đọc trực tiếp nguồn IoT hiện có.</summary>
    [HttpGet("feeders/{feederId}/diagnostics")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotDiagnosticResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotDiagnosticResult> FeederDiagnosticsAsync(string feederId, CancellationToken ct)
        => diagnostics.FeederDiagnosticsAsync(feederId, ct);

    /// <summary>Lịch sử đợt lỗi feeder, gộp theo phút UTC, lọc khoảng thời gian và phân trang.</summary>
    [HttpGet("feeders/{feederId}/diagnostics/history")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<PagedResult<IotDiagnosticHistoryItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<IotDiagnosticHistoryItem>> DiagnosticHistoryAsync(
        string feederId, [FromQuery] PageQuery page, CancellationToken ct,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        => diagnostics.DiagnosticHistoryAsync(feederId, page.ToPageRequest(), from, to, ct);

    [HttpGet("nodes/{nodeId}/diagnostics")]
    [ProducesResponseType<IotDiagnosticResult>(StatusCodes.Status200OK)]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotDiagnosticResult> GetAsync(string nodeId, CancellationToken ct)
        => diagnostics.GetAsync(nodeId, ct);

    /// <summary>Gửi ON, OFF hoặc AUTO đến node. Đọc GET command để xem ACK.</summary>
    [HttpPost("nodes/{nodeId}/command")]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
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

    /// <summary>Đọc lịch sử lệnh và trạng thái ACK trong thời hạn 12 giây, mới nhất trước.</summary>
    [HttpGet("nodes/{nodeId}/command/history")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<PagedResult<IotCommandHistoryItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<PagedResult<IotCommandHistoryItem>> CommandHistoryAsync(string nodeId, CancellationToken ct,
        [FromQuery(Name = "page")] int? page = null, [FromQuery(Name = "page_size")] int? pageSize = null)
        => diagnostics.CommandHistoryAsync(nodeId, PageRequest.Create(page, pageSize), ct);

    [HttpGet("nodes/{nodeId}/electrical")]
    [ProducesResponseType<IotElectricalResult>(StatusCodes.Status200OK)]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotElectricalResult> ElectricalAsync(string nodeId, CancellationToken ct)
        => diagnostics.ElectricalAsync(nodeId, ct);

    /// <summary>Danh sách node theo xã, mặc định 50 và tối đa 200 mục mỗi trang.</summary>
    /// <summary>Mẫu điện mới nhất của feeder, kể cả sau khi node đã được gỡ.</summary>
    [HttpGet("feeders/{feederId}/electrical")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotFeederElectricalResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotFeederElectricalResult> FeederElectricalAsync(string feederId, CancellationToken ct)
        => diagnostics.FeederElectricalAsync(feederId, ct);

    /// <summary>Lịch sử điện trung bình 30 giây theo giờ đo DS3231, mới nhất trước.</summary>
    [HttpGet("feeders/{feederId}/electrical/history")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotFeederHistoryResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotFeederHistoryResult> FeederMeasuredHistoryAsync(string feederId, CancellationToken ct,
        [FromQuery] int? page = null, [FromQuery(Name = "page_size")] int? pageSize = null,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        => diagnostics.FeederAveragedHistoryAsync(feederId, PageRequest.Create(page, pageSize ?? 20),
            "measured_at", from, to, ct);

    /// <summary>Lịch sử điện trung bình 30 giây theo giờ server nhận, mới nhất trước.</summary>
    [HttpGet("feeders/{feederId}/electrical/history/received")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<IotFeederHistoryResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IotFeederHistoryResult> FeederReceivedHistoryAsync(string feederId, CancellationToken ct,
        [FromQuery] int? page = null, [FromQuery(Name = "page_size")] int? pageSize = null,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        => diagnostics.FeederAveragedHistoryAsync(feederId, PageRequest.Create(page, pageSize ?? 20),
            "received_at", from, to, ct);

    [HttpGet("nodes")]
    [ProducesResponseType<PagedResult<IotNodeStatus>>(StatusCodes.Status200OK)]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    public Task<PagedResult<IotNodeStatus>> NodesAsync(CancellationToken ct,
        [FromQuery(Name = "page")] int? page = null, [FromQuery(Name = "page_size")] int? pageSize = null)
        => diagnostics.NodesAsync(PageRequest.Create(page, pageSize), ct);

    [HttpGet("servers")]
    [ProducesResponseType<IReadOnlyList<IotServerStatus>>(StatusCodes.Status200OK)]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    public Task<IReadOnlyList<IotServerStatus>> ServersAsync(CancellationToken ct)
        => diagnostics.ServersAsync(ct);
}
