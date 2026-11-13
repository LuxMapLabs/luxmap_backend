using System.Net;
using System.Text.Json;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
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
    internal async Task<IotOptions> ReadAsync(IReadOnlyCollection<string> scopedNodeIds, CancellationToken ct)
    {
        var configured = options.Value;
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

    internal async Task RegisterSourceAsync(string nodeId, NodeProfile profile, CancellationToken ct)
    {
        var id = profile.ServerId!.ToLowerInvariant();
        var settings = await ReadAsync([nodeId], ct);
        if (!settings.Servers.TryGetValue(id, out var server))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "IoT server chưa được đăng ký.");
        if (!IotOrigin.TryAllowed(settings, server.BaseUrl, out _))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "Địa chỉ IoT server chưa được allowlist.");
        if (settings.Nodes.ContainsKey(nodeId) ||
            options.Value.Nodes.Values.Any(node => string.Equals(node.ServerId, id, StringComparison.OrdinalIgnoreCase) &&
                                                    node.DeviceId == profile.DeviceId))
            throw Conflict();

        var details = JsonSerializer.Serialize(profile with { ServerId = id }, LuxMapJsonOptions.Default);
        db.Add(new IotSourceRegistration(nodeId, id, profile.DeviceId, details));
        RecordRegistration(id, nodeId, details);
        await SaveAsync(ct);
    }

    internal Task AuditCommandAsync(string serverId, string nodeId, object details, CancellationToken ct)
    {
        RecordRegistration(serverId, nodeId, JsonSerializer.Serialize(details, LuxMapJsonOptions.Default));
        return SaveAsync(ct);
    }

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
