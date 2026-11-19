using System.Net;
using System.Net.Http.Json;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.IOT;

public sealed record IotServerNode(string NodeId, string ServerId, string DeviceId,
    string FeederId, string Status, DateTime? LastSeenAt, string ElectricalStatus);

public sealed partial class IotDiagnosticsService
{
    private async Task<IotGatewayRoute?> ReadServerRouteAsync(Uri uri, string serverId, string key, CancellationToken ct)
    {
        using var request = ProvisioningRequest(HttpMethod.Get, uri,
            $"gateways/{Uri.EscapeDataString(serverId)}/route", key);
        try {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode != HttpStatusCode.OK || !IsJson(response)) throw Unavailable("Không đọc được tuyến server.");
            var route = await response.Content.ReadFromJsonAsync<IotGatewayRoute>(cancellationToken: ct);
            if (route is null || route.GatewayId != serverId || route.DeviceIds is null ||
                route.DeviceIds.Length > 32 || route.DeviceIds.Any(id => !ValidMqttId(id)) ||
                route.DeviceIds.Distinct(StringComparer.Ordinal).Count() != route.DeviceIds.Length)
                throw Unavailable("Bridge trả tuyến không khớp server_id.");
            return route;
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct)) { throw Unavailable("Không đọc được tuyến MQTT của server."); }
    }

    private async Task<string> CanonicalGatewayAsync(string serverId, string supplied, CancellationToken ct)
    {
        serverId = serverId.ToLowerInvariant();
        var settings = await registry.ReadAsync([], ct);
        if (!settings.Servers.TryGetValue(serverId, out var server))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy IoT server.");
        if (supplied != serverId && supplied != server.LegacyGatewayId)
            throw new LuxMapException("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict,
                "Gateway ID phải trùng server_id hoặc alias đã được xác nhận bằng migration.");
        return serverId;
    }

    public async Task<PagedResult<IotServerNode>> ServerNodesAsync(string serverId, PageRequest page, CancellationToken ct)
    {
        serverId = serverId.ToLowerInvariant();
        var visible = db.Set<IotNode>().AsNoTracking().Where(n => n.CabinetId != null);
        var ids = await visible.Select(n => n.NodeId).ToArrayAsync(ct);
        var settings = await registry.ReadAsync(ids, ct);
        if (!settings.Servers.TryGetValue(serverId, out var server) ||
            !(db.CurrentCommuneScope.IsSystemWide || server.CommuneId is { } commune && db.CurrentCommuneScope.Allows(commune)))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy IoT server trong phạm vi.");
        var matches = settings.Nodes.Where(p => string.Equals(p.Value.ServerId, serverId, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var offset = ((long)page.Page - 1) * page.PageSize;
        if (offset >= matches.Count) return PagedResult<IotServerNode>.From(page, matches.Count, []);
        var selected = await visible.Where(n => matches.Keys.Contains(n.NodeId))
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.NodeId.Length).ThenBy(n => n.NodeId)
            .Skip((int)offset).Take(page.PageSize).Select(n => n.NodeId).ToArrayAsync(ct);
        var result = selected.Select(id => new IotServerNode(id, serverId, matches[id].DeviceId,
            matches[id].FeederId, "source_unavailable", null, "source_unavailable")).ToArray();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try {
            await Parallel.ForEachAsync(Enumerable.Range(0, selected.Length), new ParallelOptions {
                MaxDegreeOfParallelism = 12, CancellationToken = budget.Token,
            }, async (index, token) => {
                var state = await NodeStatusAsync(settings, selected[index], token);
                result[index] = result[index] with { Status = state.Status,
                    LastSeenAt = state.LastSeenAt, ElectricalStatus = state.ElectricalStatus };
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.IsCancellationRequested) { }
        return PagedResult<IotServerNode>.From(page, matches.Count, result);
    }

    public async Task MigrateServerIdentityAsync(string serverId, string legacyId, string? key, CancellationToken ct)
    {
        serverId = serverId.ToLowerInvariant();
        if (!ValidMqttId(legacyId) || legacyId == serverId)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "Legacy Gateway ID không hợp lệ.");
        var ids = await db.Set<IotNode>().AsNoTracking().Where(n => n.CabinetId != null).Select(n => n.NodeId).ToArrayAsync(ct);
        var settings = await registry.ReadAsync(ids, ct);
        if (settings.Servers.Any(p => !string.Equals(p.Key, serverId, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(p.Key, legacyId, StringComparison.OrdinalIgnoreCase) || p.Value.LegacyGatewayId == legacyId)))
            throw new LuxMapException("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict,
                "Legacy ID đã thuộc server khác.");
        var uri = await ProvisioningOriginAsync(serverId, null, key, ct);
        var devices = settings.Nodes.Values.Where(p => string.Equals(p.ServerId, serverId, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.DeviceId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // No guessing from the Bridge's other gateways. The source registry defines ownership.
        await registry.MigrateIdentityAsync(serverId, legacyId, async token => {
            using var request = ProvisioningRequest(HttpMethod.Post, uri,
                $"gateways/{Uri.EscapeDataString(serverId)}/migration", key!);
            request.Content = JsonContent.Create(new { legacyGatewayId = legacyId, deviceIds = devices });
            try {
                using var response = await client.SendAsync(request, token);
                if (response.StatusCode == HttpStatusCode.Conflict)
                    throw new LuxMapException("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict,
                        "Cần key/heartbeat online cho mã server mới và danh sách node cũ khớp nguồn đã đăng ký.");
                if (response.StatusCode != HttpStatusCode.NoContent) throw Unavailable("Bridge chưa xác nhận chuyển danh tính.");
            }
            catch (Exception ex) when (IsSourceFailure(ex, token)) { throw Unavailable("Không chuyển được danh tính trên Bridge; có thể thử lại."); }
        }, ct);
    }

    private async Task ChangeBindingAsync(NodeProfile target, string? expected, bool detach, string commune,
        string? key, CancellationToken ct)
    {
        var serverId = target.ServerId!.ToLowerInvariant();
        var uri = await ProvisioningOriginAsync(serverId, commune, key, ct);
        using var request = ProvisioningRequest(HttpMethod.Post, uri,
            $"gateways/{Uri.EscapeDataString(serverId)}/devices/{Uri.EscapeDataString(target.DeviceId)}/binding", key!);
        request.Content = JsonContent.Create(new { expectedGatewayId = expected, detach });
        try {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new LuxMapException("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict,
                    "Quyền MQTT đã thay đổi hoặc server chưa có key; vui lòng tải lại.");
            if (response.StatusCode != HttpStatusCode.NoContent) throw Unavailable("Bridge chưa xác nhận cập nhật quyền MQTT.");
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct)) { throw Unavailable("Không cập nhật được quyền MQTT."); }
    }

    public async Task AuthorizeSourceChangeAsync(NodeProfile target, NodeProfile? previous, string commune,
        string? key, CancellationToken ct)
    {
        if (previous is null) {
            await RequireMqttAuthorizationAsync(target.ServerId!, target.DeviceId, commune, key, ct);
            return;
        }
        if (previous.DeviceId == target.DeviceId) {
            if (!string.Equals(previous.ServerId, target.ServerId, StringComparison.OrdinalIgnoreCase)) {
                var oldUri = await ProvisioningOriginAsync(previous.ServerId!, commune, key, ct);
                var newUri = await ProvisioningOriginAsync(target.ServerId!, commune, key, ct);
                if (oldUri != newUri)
                    throw new LuxMapException("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict, "Chỉ chuyển node giữa server cùng Bridge.");
                await ChangeBindingAsync(target, previous.ServerId!.ToLowerInvariant(), false, commune, key, ct);
            }
            else await EnsureGatewayRouteAsync(target.ServerId!, target.DeviceId, commune, key, ct);
        }
        else {
            await RequireMqttAuthorizationAsync(target.ServerId!, target.DeviceId, commune, key, ct);
            await ChangeBindingAsync(previous, previous.ServerId!.ToLowerInvariant(), true, commune, key, ct);
        }
    }

    public Task DetachBindingAsync(NodeProfile previous, string commune, string? key, CancellationToken ct)
        => ChangeBindingAsync(previous, previous.ServerId!.ToLowerInvariant(), true, commune, key, ct);

    public Task RestoreBindingAsync(NodeProfile previous, NodeProfile? target, string commune, string? key, CancellationToken ct)
        => ChangeBindingAsync(previous, previous.DeviceId == target?.DeviceId ? target.ServerId?.ToLowerInvariant() : null,
            false, commune, key, ct);
}
