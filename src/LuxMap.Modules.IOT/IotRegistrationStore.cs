using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Modules.IOT.Registry;
using LuxMap.Modules.IOT.Lighting;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LuxMap.Modules.IOT;

internal sealed record IotServerRegistration(string ServerId, string BaseUrl)
{
    public string? CommuneId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
internal sealed record IotSourceRegistration(string NodeId, string ServerId, string DeviceId, string Profile)
{
    public DateTime? DetachedAt { get; set; }
}

internal sealed class IotRegistrationAudit : IImmutableRecord
{
    public long Id { get; set; }
    public required string ServerId { get; init; }
    public string? NodeId { get; init; }
    public required string Details { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string CorrelationId { get; init; }
}

internal sealed class IotRegistrationAuditConfiguration : IEntityTypeConfiguration<IotRegistrationAudit>
{
    public void Configure(EntityTypeBuilder<IotRegistrationAudit> builder)
    {
        builder.ToTable("iot_registration_audit");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Details).HasColumnType("jsonb");
    }
}

internal sealed class IotServerRegistrationConfiguration : IEntityTypeConfiguration<IotServerRegistration>
{
    public void Configure(EntityTypeBuilder<IotServerRegistration> builder)
    {
        builder.ToTable("iot_server_registration");
        builder.HasKey(row => row.ServerId);
        builder.HasOne<AdministrativeUnit>().WithMany().HasForeignKey(row => row.CommuneId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IotSourceRegistrationConfiguration : IEntityTypeConfiguration<IotSourceRegistration>
{
    public void Configure(EntityTypeBuilder<IotSourceRegistration> builder)
    {
        builder.ToTable("iot_source_registration");
        builder.HasKey(row => row.NodeId);
        builder.HasIndex(row => new { row.ServerId, row.DeviceId }).IsUnique();
        builder.Property(row => row.Profile).HasColumnType("jsonb");
        builder.HasOne<IotNode>().WithOne().HasForeignKey<IotSourceRegistration>(row => row.NodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed record FeederTelemetryBinding(
    string NodeId, string ServerId, string DeviceId, DateTimeOffset From, DateTimeOffset? Until);

public sealed class IotRegistrationStore(
    LuxMapDbContext db, IOptions<IotOptions> options, ICurrentActorAccessor actor, ICorrelationIdAccessor correlation)
{
    internal IotRegistrationStore ForContext(LuxMapDbContext context) => new(context, options, actor, correlation);

    internal Task<IotOptions> ReadAsync(IReadOnlyCollection<string> scopedNodeIds, CancellationToken ct, bool includeDetached = false)
        => ReadAsync(db, options.Value, scopedNodeIds, ct, includeDetached);

    internal static async Task<IotOptions> ReadAsync(
        LuxMapDbContext db, IotOptions configured, IReadOnlyCollection<string> scopedNodeIds, CancellationToken ct,
        bool includeDetached = false)
    {
        var servers = new Dictionary<string, IotServerProfile>(configured.Servers, StringComparer.OrdinalIgnoreCase);
        foreach (var row in await db.Set<IotServerRegistration>().AsNoTracking().ToListAsync(ct))
            servers[row.ServerId] = new(row.BaseUrl, row.CommuneId, row.Latitude, row.Longitude);

        var nodes = new Dictionary<string, NodeProfile>(StringComparer.Ordinal);
        var visibleNodes = db.Set<IotNode>().AsNoTracking()
            .Where(node => scopedNodeIds.Contains(node.NodeId) && (includeDetached || node.CabinetId != null));
        if (configured.Nodes.Count > 0)
        {
            var configuredIds = configured.Nodes.Keys.ToArray();
            foreach (var id in await visibleNodes.Where(node => configuredIds.Contains(node.NodeId)).Select(node => node.NodeId).ToListAsync(ct))
                nodes.Add(id, configured.Nodes[id]);
        }

        // Join through the commune-filtered node, even if an unscoped caller passes an ID list.
        var sourceRows = await (
            from row in db.Set<IotSourceRegistration>().AsNoTracking()
            join node in visibleNodes on row.NodeId equals node.NodeId
            where includeDetached || row.DetachedAt == null
            select row).ToListAsync(ct);
        foreach (var row in sourceRows)
            nodes[row.NodeId] = JsonSerializer.Deserialize<NodeProfile>(row.Profile, LuxMapJsonOptions.Default)!;
        return new() { BaseUrl = configured.BaseUrl, Servers = servers, Nodes = nodes, AllowedOrigins = configured.AllowedOrigins };
    }

    internal async Task RegisterServerAsync(string id, Uri uri, string? communeId, IotLocation? location, CancellationToken ct)
    {
        if (options.Value.Servers.Keys.Contains(id, StringComparer.OrdinalIgnoreCase))
            throw Conflict();
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (communeId is not null) await RequireCommuneAsync(communeId, ct);
        db.Add(new IotServerRegistration(id, origin)
        {
            CommuneId = communeId, Latitude = location?.Lat, Longitude = location?.Lng,
        });
        RecordRegistration(id, null, JsonSerializer.Serialize(new { base_url = origin, commune_id = communeId, location }, LuxMapJsonOptions.Default));
        await SaveAsync(ct);
    }

    internal async Task UpdateServerLocationAsync(string id, string communeId, IotLocation location, CancellationToken ct)
    {
        await RequireCommuneAsync(communeId, ct);
        var row = await db.Set<IotServerRegistration>().SingleOrDefaultAsync(server => server.ServerId == id, ct);
        if (row is null)
        {
            if (!options.Value.Servers.TryGetValue(id, out var configured) ||
                !IotOrigin.TryAllowed(options.Value, configured.BaseUrl, out var uri))
                throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy IoT server.");
            row = new(id, uri.GetLeftPart(UriPartial.Authority));
            db.Add(row);
        }
        row.CommuneId = communeId;
        row.Latitude = location.Lat;
        row.Longitude = location.Lng;
        RecordRegistration(id, null, JsonSerializer.Serialize(new { commune_id = communeId, location }, LuxMapJsonOptions.Default));
        await SaveAsync(ct);
    }

    private async Task RequireCommuneAsync(string communeId, CancellationToken ct)
    {
        if (!await db.Set<AdministrativeUnit>().AnyAsync(unit => unit.CommuneId == communeId, ct))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy xã/phường.");
    }

    internal async Task<(string NodeId, string GatewayId, bool Created)> RegisterNodeAsync(CreateIotNodeRequest request,
        NodeProfile profile, IotNodeRegistryService nodes, IotFeederSourceLookup sources,
        Func<string, CancellationToken, Task<string>> ensureGatewayRoute, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The feeder serializes repeat registrations. A new node row is private to this transaction.
        var feeder = await FeederControllerBinding.LockFeederAsync(db, profile.FeederId, ct);
        if (feeder.CabinetId != request.CabinetId)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Feeder phải thuộc tủ điện đã chọn.");

        var (settings, links) = await sources.LinksAsync(db, [profile.FeederId], ct);
        if (links.TryGetValue(profile.FeederId, out var existing))
        {
            var node = await nodes.NodeAsync(existing.NodeId, ct);
            if (existing.Profile.ServerId != profile.ServerId || existing.Profile.DeviceId != profile.DeviceId ||
                existing.Profile.NominalVoltage != profile.NominalVoltage ||
                existing.Profile.NominalCurrentMa != profile.NominalCurrentMa ||
                existing.Profile.NominalPowerMw != profile.NominalPowerMw ||
                (profile.LampCountCalibration is not null &&
                 existing.Profile.EffectiveLampCountCalibration != profile.LampCountCalibration) ||
                node.DataSource != request.DataSource || node.SupportsRemoteControl != request.SupportsRemoteControl)
                throw Conflict();
            return (existing.NodeId, await ensureGatewayRoute(feeder.CommuneId, ct), false);
        }

        // One physical MQTT device per bridge origin, including server aliases on the same VPS.
        if (!settings.Servers.TryGetValue(profile.ServerId!, out var target) || target.CommuneId != feeder.CommuneId)
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound,
                "Không tìm thấy IoT server trong phạm vi.");
        if (!IotOrigin.TryAllowed(settings, target.BaseUrl, out var targetUri))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Địa chỉ IoT server chưa được allowlist.");
        var serverIds = settings.Servers.Where(pair => IotOrigin.TryAllowed(settings, pair.Value.BaseUrl, out var uri) &&
            uri == targetUri).Select(pair => pair.Key.ToLowerInvariant()).ToArray();
        // Serialize registration of the same physical ID even across different feeders/server records.
        var deviceKey = targetUri.AbsoluteUri + profile.DeviceId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({deviceKey}, 0))", ct);
        if (await db.Set<IotSourceRegistration>().AnyAsync(row =>
                serverIds.Contains(row.ServerId) && row.DeviceId == profile.DeviceId, ct) ||
            options.Value.Nodes.Values.Any(source => source.DeviceId == profile.DeviceId &&
                serverIds.Contains(source.ServerId?.ToLowerInvariant())))
            throw Conflict();

        var nodeId = await nodes.CreateAsync(request, ct);
        await SaveSourceAsync(nodeId, profile, createOnly: true, ct);
        var gatewayId = await ensureGatewayRoute(feeder.CommuneId, ct);
        await transaction.CommitAsync(ct);
        return (nodeId, gatewayId, true);
    }

    internal async Task SaveSourceAsync(string nodeId, NodeProfile profile, bool createOnly, CancellationToken ct,
        Func<string, CancellationToken, Task>? authorize = null)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var node = await LightingCommandService.LockDeviceAsync(db, nodeId, ct);
        var feeder = await FeederControllerBinding.LockFeederAsync(db, profile.FeederId, ct);
        if (feeder.CabinetId is null || feeder.CommuneId != node.CommuneId ||
            (node.CabinetId is not null && feeder.CabinetId != node.CabinetId))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Feeder phải thuộc một tủ điện trong cùng xã và cùng tủ với node nếu node đang được gắn.");
        var controller = await db.Set<FeederControl>().Where(control => control.FeederId == profile.FeederId)
            .Select(control => control.NodeId).SingleOrDefaultAsync(ct);
        FeederControllerBinding.RequireCompatible(profile.FeederId, nodeId, controller);

        var id = profile.ServerId!.ToLowerInvariant();
        var settings = await ReadAsync([nodeId], ct);
        if (!settings.Servers.TryGetValue(id, out var server))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "IoT server chưa được đăng ký.");
        if (!IotOrigin.TryAllowed(settings, server.BaseUrl, out _))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "Địa chỉ IoT server chưa được allowlist.");
        var existing = await db.Set<IotSourceRegistration>().SingleOrDefaultAsync(row => row.NodeId == nodeId, ct);
        if (createOnly && (existing is not null || settings.Nodes.ContainsKey(nodeId))) throw Conflict();
        if (existing is null && options.Value.Nodes.ContainsKey(nodeId)) throw StaticSource();
        if (options.Value.Nodes.Any(pair => pair.Key != nodeId &&
                string.Equals(pair.Value.ServerId, id, StringComparison.OrdinalIgnoreCase) && pair.Value.DeviceId == profile.DeviceId))
            throw Conflict();

        var configuredOwners = options.Value.Nodes.Where(pair => pair.Key != nodeId && pair.Value.FeederId == profile.FeederId)
            .Select(pair => pair.Key).ToArray();
        if (configuredOwners.Length > 0 && await db.Set<IotNode>().AnyAsync(candidate =>
                candidate.CabinetId != null && configuredOwners.Contains(candidate.NodeId), ct))
            throw Conflict();
        if (authorize is not null) await authorize(node.CommuneId, ct);
        if (existing is not null) await AuditLegacyFeederBindingAsync(node, existing, ct);

        if (node.CabinetId is null)
        {
            node.CabinetId = feeder.CabinetId;
            node.UpdatedAt = UtcMicrosecondClock.UtcNow();
        }
        var details = JsonSerializer.Serialize(profile with { ServerId = id }, LuxMapJsonOptions.Default);
        if (existing is null) db.Add(new IotSourceRegistration(nodeId, id, profile.DeviceId, details));
        else db.Entry(existing).CurrentValues.SetValues(new IotSourceRegistration(nodeId, id, profile.DeviceId, details));
        RecordRegistration(id, nodeId, details);
        await SaveAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    internal async Task DetachNodeAsync(string nodeId, LightingCommandService lighting, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var node = await lighting.LockDeviceAsync(nodeId, ct);
        if (node.CabinetId is null) return;

        var row = await db.Set<IotSourceRegistration>().SingleOrDefaultAsync(source => source.NodeId == nodeId, ct);
        if (row is null && options.Value.Nodes.TryGetValue(nodeId, out var configured))
        {
            row = new(nodeId, configured.ServerId?.ToLowerInvariant() ?? "default", configured.DeviceId,
                JsonSerializer.Serialize(configured, LuxMapJsonOptions.Default));
            db.Add(row);
        }
        if (row is not null) await AuditLegacyFeederBindingAsync(node, row, ct);
        var relays = await db.Set<FeederControl>().Where(control => control.NodeId == nodeId).ToListAsync(ct);
        var now = UtcMicrosecondClock.UtcNow();
        await lighting.ExpireStaleAsync([nodeId], now, ct);
        await lighting.SupersedeRelayAsync(nodeId, null, AuditActorKind.User, now, ct);
        if (relays.Count > 0)
        {
            db.RemoveRange(relays);
            // The cabinet-aware relay FK is enforced in SQL. Clear the bindings before updating the node.
            await SaveAsync(ct);
        }

        if (row is not null) row.DetachedAt = now;
        RecordRegistration(row?.ServerId ?? "default", nodeId, JsonSerializer.Serialize(new
        {
            action = "node_detached", cabinet_id = node.CabinetId, device_id = row?.DeviceId,
        }, LuxMapJsonOptions.Default));
        node.CabinetId = null;
        node.UpdatedAt = now;
        await SaveAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static LuxMapException StaticSource() =>
        new("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict, "Nguồn đo được khai báo trong cấu hình triển khai.");

    private async Task AuditLegacyFeederBindingAsync(IotNode node, IotSourceRegistration source, CancellationToken ct)
    {
        var profile = JsonNode.Parse(source.Profile)!.AsObject();
        if (profile["feeder_id"]?.GetValue<string>() is not { Length: > 0 } feederId) return;
        var recorded = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM iot_registration_audit
                WHERE node_id = {node.NodeId} AND server_id = {source.ServerId}
                    AND details ->> 'feeder_id' = {feederId} AND details ->> 'device_id' = {source.DeviceId}
                    AND details ? 'nominal_voltage'
            ) AS "Value"
            """).SingleAsync(ct);
        if (recorded) return;

        profile["effective_from"] = JsonValue.Create(node.CreatedAt);
        RecordRegistration(source.ServerId, node.NodeId, profile.ToJsonString(LuxMapJsonOptions.Default));
    }

    internal async Task<IReadOnlyList<FeederTelemetryBinding>> FeederTelemetryBindingsAsync(
        string feederId, CancellationToken ct)
    {
        // Read audit events and legacy bindings from one snapshot so a concurrent detach cannot leave an open window.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        // Scope at the feeder. A node can have been detached or replaced since making these measurements.
        if (!await db.Set<Feeder>().AnyAsync(feeder => feeder.FeederId == feederId, ct))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy mạch điện.");

        var rows = await db.Database.SqlQuery<FeederTelemetryBindingRow>($"""
            WITH owners AS (
                SELECT DISTINCT node_id
                FROM iot_registration_audit
                WHERE details ->> 'feeder_id' = {feederId} AND details ? 'nominal_voltage'
            ),
            changes AS (
                SELECT event.id, event.node_id, event.server_id, event.details ->> 'device_id' AS device_id,
                       event.details ->> 'feeder_id' AS feeder_id,
                       coalesce((event.details ->> 'effective_from')::timestamptz, event.created_at) AS assigned_at
                FROM iot_registration_audit event JOIN owners USING (node_id)
                WHERE (event.details ? 'feeder_id' AND event.details ? 'nominal_voltage')
                    OR event.details ->> 'action' IN ('node_detached', 'source_removed')
            ),
            timeline AS (
                SELECT *, lead(assigned_at) OVER (PARTITION BY node_id ORDER BY assigned_at, id) AS until
                FROM changes
            )
            SELECT node_id, server_id, device_id, assigned_at, until
            FROM timeline
            WHERE feeder_id = {feederId} AND device_id IS NOT NULL
                AND (until > assigned_at OR (until IS NULL AND EXISTS (
                    SELECT 1 FROM iot_node mounted WHERE mounted.node_id = timeline.node_id
                        AND mounted.cabinet_id IS NOT NULL)))
            """).ToListAsync(ct);
        var bindings = rows.Select(row => new FeederTelemetryBinding(
            row.NodeId, row.ServerId, row.DeviceId, row.AssignedAt, row.Until)).ToList();

        // Config-only and imported profiles may predate source audit rows. Use the node's recorded creation and detach dates.
        var matching = db.Set<IotSourceRegistration>().FromSql($"""
            SELECT * FROM iot_source_registration WHERE profile ->> 'feeder_id' = {feederId}
            """).AsNoTracking();
        var legacy = await (
            from source in matching
            join node in db.Set<IotNode>().AsNoTracking() on source.NodeId equals node.NodeId
            select new { source.NodeId, source.ServerId, source.DeviceId, node.CreatedAt, source.DetachedAt }).ToListAsync(ct);
        foreach (var row in legacy)
            AddLegacy(row.NodeId, row.ServerId, row.DeviceId, row.CreatedAt, row.DetachedAt);

        var configuredIds = options.Value.Nodes.Where(pair => pair.Value.FeederId == feederId).Select(pair => pair.Key).ToArray();
        if (configuredIds.Length > 0)
        {
            var configured = await db.Set<IotNode>().AsNoTracking()
                .Where(node => node.CabinetId != null && configuredIds.Contains(node.NodeId) &&
                    !db.Set<IotSourceRegistration>().Any(source => source.NodeId == node.NodeId))
                .Select(node => new { node.NodeId, node.CreatedAt }).ToListAsync(ct);
            foreach (var node in configured)
            {
                var profile = options.Value.Nodes[node.NodeId];
                AddLegacy(node.NodeId, profile.ServerId ?? "default", profile.DeviceId, node.CreatedAt, null);
            }
        }

        await transaction.CommitAsync(ct);
        return bindings;

        void AddLegacy(string nodeId, string serverId, string deviceId, DateTimeOffset assignedAt, DateTimeOffset? until)
        {
            if (!bindings.Any(binding => binding.NodeId == nodeId && binding.ServerId == serverId && binding.DeviceId == deviceId) &&
                (until is null || until > assignedAt))
                bindings.Add(new(nodeId, serverId, deviceId, assignedAt, until));
        }
    }

    private sealed class FeederTelemetryBindingRow
    {
        [Column("node_id")] public required string NodeId { get; init; }
        [Column("server_id")] public required string ServerId { get; init; }
        [Column("device_id")] public required string DeviceId { get; init; }
        [Column("assigned_at")] public DateTimeOffset AssignedAt { get; init; }
        [Column("until")] public DateTimeOffset? Until { get; init; }
    }

    internal Task AuditCommandAsync(string serverId, string nodeId, object details, CancellationToken ct)
    {
        RecordRegistration(serverId, nodeId, JsonSerializer.Serialize(details, LuxMapJsonOptions.Default));
        return SaveAsync(ct);
    }

    internal async Task<PagedResult<IotCommandHistoryItem>> ReadCommandHistoryAsync(
        string nodeId, PageRequest page, CancellationToken ct)
    {
        var requests = db.Database.SqlQuery<CommandHistoryRow>($"""
            SELECT requested.id, requested.node_id, requested.server_id, requested.details::text AS details,
                   coalesce(nullif(sender.full_name, ''), requested.created_by) AS requested_by, requested.created_at,
                   (
                       SELECT min(accepted.created_at)
                       FROM iot_registration_audit accepted
                       WHERE accepted.node_id = requested.node_id AND accepted.server_id = requested.server_id
                         AND accepted.details ->> 'event_type' = 'command_accepted'
                         AND accepted.details ->> 'command_id' = requested.details ->> 'command_id'
                   ) AS bridge_accepted_at
            FROM iot_registration_audit requested
            LEFT JOIN app_user sender ON sender.user_id = requested.created_by
            WHERE requested.node_id = {nodeId} AND requested.details ->> 'event_type' = 'command_requested'
            """)
            .Where(row => db.Set<IotNode>().Any(node => node.NodeId == row.NodeId));

        var total = await requests.CountAsync(ct);
        if (((long)page.Page - 1) * page.PageSize >= total)
            return PagedResult<IotCommandHistoryItem>.From(page, total, []);

        var rows = await requests.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        var items = rows.Select(row =>
        {
            var details = JsonSerializer.Deserialize<RequestedCommandDetails>(row.Details, LuxMapJsonOptions.Default)!;
            return new IotCommandHistoryItem(details.CommandId, row.NodeId, details.FeederId, row.ServerId,
                details.DeviceId, details.Mode, details.Command, details.AutoSchedule,
                row.RequestedBy, row.CreatedAt, row.BridgeAcceptedAt);
        }).ToArray();
        return PagedResult<IotCommandHistoryItem>.From(page, total, items);
    }

    private sealed class CommandHistoryRow
    {
        [Column("id")] public long Id { get; init; }
        [Column("node_id")] public required string NodeId { get; init; }
        [Column("server_id")] public required string ServerId { get; init; }
        [Column("details")] public required string Details { get; init; }
        [Column("requested_by")] public required string RequestedBy { get; init; }
        [Column("created_at")] public DateTime CreatedAt { get; init; }
        [Column("bridge_accepted_at")] public DateTime? BridgeAcceptedAt { get; init; }
    }

    private sealed record RequestedCommandDetails(
        string CommandId, string FeederId, string DeviceId, string Mode, string? Command, IotAutoSchedule? AutoSchedule);

    // No FK to the registered objects: deleting a node must not erase its registration history.
    private void RecordRegistration(string serverId, string? nodeId, string details) =>
        db.Add(new IotRegistrationAudit
        {
            ServerId = serverId, NodeId = nodeId, Details = details,
            CreatedBy = actor.UserId ?? throw new InvalidOperationException("An authorized request carries a user id."),
            CreatedAt = UtcMicrosecondClock.UtcNow(), CorrelationId = correlation.CorrelationId,
        });

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw Conflict();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy thiết bị IoT.");
        }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException ||
                                   ex is InvalidOperationException { InnerException: NpgsqlException })
        {
            throw new LuxMapException("IOT_CONFIGURATION_UNAVAILABLE", HttpStatusCode.ServiceUnavailable,
                "Backend không lưu được cấu hình IoT.");
        }
    }

    private static LuxMapException Conflict() =>
        new("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict, "Server, node hoặc mã thiết bị đã được đăng ký.");
}

internal static class IotOrigin
{
    internal static bool TryAllowed(IotOptions settings, string? address, out Uri uri)
    {
        uri = null!;
        if (!TryParse(address, out var requested)) return false;
        var origin = requested.GetLeftPart(UriPartial.Authority);
        foreach (var entry in settings.AllowedOrigins)
        {
            if (TryParse(entry, out var allowed) &&
                string.Equals(origin, allowed.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            {
                uri = allowed;
                return true;
            }
        }
        return false;
    }

    private static bool TryParse(string? address, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https") ||
            parsed.AbsolutePath != "/" || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 ||
            parsed.Fragment.Length != 0) return false;
        uri = parsed;
        return true;
    }
}
