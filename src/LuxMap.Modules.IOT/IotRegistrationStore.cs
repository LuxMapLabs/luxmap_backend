using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using System.Text.Json;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Modules.IOT.Registry;
using LuxMap.Modules.IOT.Lighting;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
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
internal sealed record IotSourceRegistration(string NodeId, string ServerId, string DeviceId, string Profile);

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

public sealed class IotRegistrationStore(
    LuxMapDbContext db, IOptions<IotOptions> options, ICurrentActorAccessor actor, ICorrelationIdAccessor correlation)
{
    internal Task<IotOptions> ReadAsync(IReadOnlyCollection<string> scopedNodeIds, CancellationToken ct)
        => ReadAsync(db, options.Value, scopedNodeIds, ct);

    internal static async Task<IotOptions> ReadAsync(
        LuxMapDbContext db, IotOptions configured, IReadOnlyCollection<string> scopedNodeIds, CancellationToken ct)
    {
        var servers = new Dictionary<string, IotServerProfile>(configured.Servers, StringComparer.OrdinalIgnoreCase);
        foreach (var row in await db.Set<IotServerRegistration>().AsNoTracking().ToListAsync(ct))
            servers[row.ServerId] = new(row.BaseUrl, row.CommuneId, row.Latitude, row.Longitude);

        var nodes = new Dictionary<string, NodeProfile>(StringComparer.Ordinal);
        foreach (var id in scopedNodeIds)
            if (configured.Nodes.TryGetValue(id, out var profile)) nodes.Add(id, profile);

        // Join through the commune-filtered node, even if an unscoped caller passes an ID list.
        var sourceRows = await (
            from row in db.Set<IotSourceRegistration>().AsNoTracking()
            join node in db.Set<IotNode>() on row.NodeId equals node.NodeId
            where scopedNodeIds.Contains(node.NodeId)
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
        await SaveSourceAsync(nodeId, profile, replace: false, ct);
        var gatewayId = await ensureGatewayRoute(feeder.CommuneId, ct);
        await transaction.CommitAsync(ct);
        return (nodeId, gatewayId, true);
    }

    internal async Task SaveSourceAsync(string nodeId, NodeProfile profile, bool replace, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var node = await LightingCommandService.LockDeviceAsync(db, nodeId, ct);
        var feeder = await FeederControllerBinding.LockFeederAsync(db, profile.FeederId, ct);
        if (feeder.CabinetId != node.CabinetId || feeder.CommuneId != node.CommuneId)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Feeder phải thuộc cùng tủ điện và xã với node IoT.");
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
        if (replace && existing is null) throw MissingSource(nodeId);
        if ((!replace && (existing is not null || settings.Nodes.ContainsKey(nodeId))) ||
            options.Value.Nodes.Values.Any(node => string.Equals(node.ServerId, id, StringComparison.OrdinalIgnoreCase) &&
                                                    node.DeviceId == profile.DeviceId) ||
            options.Value.Nodes.Any(pair => pair.Key != nodeId && pair.Value.FeederId == profile.FeederId))
            throw Conflict();

        var details = JsonSerializer.Serialize(profile with { ServerId = id }, LuxMapJsonOptions.Default);
        if (existing is null) db.Add(new IotSourceRegistration(nodeId, id, profile.DeviceId, details));
        else db.Entry(existing).CurrentValues.SetValues(new IotSourceRegistration(nodeId, id, profile.DeviceId, details));
        RecordRegistration(id, nodeId, details);
        await SaveAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    internal async Task DeleteSourceAsync(string nodeId, CancellationToken ct)
    {
        if (!await db.Set<IotNode>().AnyAsync(node => node.NodeId == nodeId, ct))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy thiết bị IoT.");
        var row = await db.Set<IotSourceRegistration>().SingleOrDefaultAsync(source => source.NodeId == nodeId, ct)
            ?? throw MissingSource(nodeId);
        if (options.Value.Nodes.ContainsKey(nodeId)) throw MissingSource(nodeId);
        db.Remove(row);
        RecordRegistration(row.ServerId, nodeId, JsonSerializer.Serialize(new
        {
            action = "source_removed", device_id = row.DeviceId,
        }, LuxMapJsonOptions.Default));
        await SaveAsync(ct);
    }

    private LuxMapException MissingSource(string nodeId) => options.Value.Nodes.ContainsKey(nodeId)
        ? new("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict, "Nguồn đo được khai báo trong cấu hình triển khai.")
        : new(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Node chưa có nguồn đo đã đăng ký.");

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
