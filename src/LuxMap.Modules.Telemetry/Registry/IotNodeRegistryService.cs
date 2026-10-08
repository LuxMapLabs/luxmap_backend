using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Modules.Telemetry.Lighting;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.Telemetry.Registry;

/// <summary>
/// Device registry (LIGHT-CTRL 2a, part of BE-34): register a device in a cabinet, wire its relays to feeders, issue its secret.
/// </summary>
/// <remarks>
/// <para>
/// Everything reads through the commune filter: a cabinet, feeder or device outside the caller's scope is a 404. The device's
/// commune is copied from its cabinet, never sent (the fixture-from-pole rule).
/// </para>
/// <para>
/// 🔴 <b>Wiring locks the device row</b> (<c>SELECT 1 … FOR UPDATE</c>, the <c>FaultLocks</c> shape) before reading the relay
/// state again, so two wirings of one device cannot interleave. Lighting commands (2b) take the same lock.
/// </para>
/// <para>
/// The database backs every rule here and answers for paths that skip this service: one device per cabinet
/// (<c>ux_iot_node_cabinet_id</c>), one relay per feeder (PK of <c>feeder_control</c>), a relay only in its device's cabinet
/// (<c>fk_feeder_control_feeder_same_cabinet</c>), never field data (<c>ck_iot_node_data_source_not_field</c>).
/// </para>
/// </remarks>
public sealed class IotNodeRegistryService(LuxMapDbContext db, TimeProvider clock, LightingCommandService lighting)
{
    /// <summary>Relays a pilot device may number. The CHECK only asks <c>relay_no &gt; 0</c>; this keeps a typo off the table.</summary>
    public const int MaxRelayNo = 32;

    public async Task<PagedResult<IotNodeItem>> ListAsync(IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
    {
        var query = db.Set<IotNode>().AsNoTracking();
        if (communes is not null)
        {
            query = query.Where(node => communes.Contains(node.CommuneId));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(node => node.CreatedAt).ThenBy(node => node.NodeId.Length).ThenBy(node => node.NodeId)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(Row)
            .ToListAsync(ct);

        return PagedResult<IotNodeItem>.From(page, total, items);
    }

    public async Task<IotNodeItem> NodeAsync(string nodeId, CancellationToken ct)
        => await db.Set<IotNode>().AsNoTracking().Where(node => node.NodeId == nodeId).Select(Row).FirstOrDefaultAsync(ct)
            ?? throw NotFound("device");

    public async Task<string> CreateAsync(CreateIotNodeRequest request, CancellationToken ct)
    {
        var source = RequireDeviceSource(request.DataSource!.Value);
        var cabinet = await db.Set<ElectricalCabinet>()
            .Where(candidate => candidate.CabinetId == request.CabinetId)
            .Select(candidate => new { candidate.CabinetId, candidate.CommuneId })
            .FirstOrDefaultAsync(ct) ?? throw NotFound("cabinet");

        if (await db.Set<IotNode>().AnyAsync(node => node.CabinetId == cabinet.CabinetId, ct))
        {
            throw CabinetTaken(cabinet.CabinetId);
        }

        var node = new IotNode
        {
            CabinetId = cabinet.CabinetId,
            CommuneId = cabinet.CommuneId,
            DataSource = source,
            SupportsRemoteControl = request.SupportsRemoteControl,
        };
        db.Add(node);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure) when (Constraint(failure) == "ux_iot_node_cabinet_id")
        {
            // Two registrations raced past the check above; the unique index settled it.
            db.ChangeTracker.Clear();
            throw CabinetTaken(cabinet.CabinetId);
        }

        return node.NodeId;
    }

    public async Task UpdateAsync(string nodeId, UpdateIotNodeRequest request, CancellationToken ct)
    {
        var source = RequireDeviceSource(request.DataSource!.Value);
        var node = await RequireAsync(nodeId, ct);

        node.DataSource = source;
        node.SupportsRemoteControl = request.SupportsRemoteControl;
        Stamp(node);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes a device. The foreign keys decide: a wired relay (and, from 2b, a command) refuses it with 409.</summary>
    public async Task DeleteAsync(string nodeId, CancellationToken ct)
    {
        var node = await RequireAsync(nodeId, ct);
        db.Remove(node);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } postgres)
        {
            db.ChangeTracker.Clear();
            throw new LuxMapException(
                ErrorCodes.AssetInUse,
                HttpStatusCode.Conflict,
                "That device still has relays wired or commands recorded, so it cannot be deleted. Unwire its relays first.",
                new Dictionary<string, object?> { ["constraint"] = postgres.ConstraintName, ["table"] = postgres.TableName });
        }
    }

    /// <summary>
    /// Wires relay <paramref name="relayNo"/> to <paramref name="feederId"/>, or unwires it (<c>null</c>). Re-wiring a relay to
    /// another feeder replaces its row — the reported mode of the old wiring does not carry over.
    /// </summary>
    public async Task SetRelayAsync(string nodeId, int relayNo, string? feederId, CancellationToken ct)
    {
        if (relayNo is < 1 or > MaxRelayNo)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                $"relay_no must be between 1 and {MaxRelayNo}.", new Dictionary<string, object?> { ["relay_no"] = relayNo });
        }

        // Scope FIRST, through the commune filter: a device of another commune is a 404 before any lock is taken, so a manager
        // can neither wait on nor time the lock of a device outside their scope (Codex review P2).
        var commune = await db.Set<IotNode>().AsNoTracking()
            .Where(candidate => candidate.NodeId == nodeId)
            .Select(candidate => candidate.CommuneId)
            .FirstOrDefaultAsync(ct) ?? throw NotFound("device");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Lock, then read again under the lock (the FaultLocks shape).
        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM iot_node WHERE node_id = {0} AND commune_id = {1} FOR UPDATE", [nodeId, commune], ct);
        var node = await RequireAsync(nodeId, ct);

        var current = await db.Set<FeederControl>()
            .FirstOrDefaultAsync(control => control.NodeId == node.NodeId && control.RelayNo == relayNo, ct);

        if (feederId is not null)
        {
            if (current?.FeederId == feederId)
            {
                return;
            }

            var feeder = await db.Set<Feeder>().AsNoTracking()
                .Where(candidate => candidate.FeederId == feederId)
                .Select(candidate => new { candidate.FeederId, candidate.CabinetId })
                .FirstOrDefaultAsync(ct) ?? throw NotFound("feeder");

            if (!string.Equals(feeder.CabinetId, node.CabinetId, StringComparison.Ordinal))
            {
                throw new LuxMapException(
                    ErrorCodes.FeederNotInCabinet,
                    HttpStatusCode.Conflict,
                    "That feeder does not leave from this device's cabinet, so a relay of this device cannot switch it.",
                    new Dictionary<string, object?>
                    {
                        ["feeder_id"] = feeder.FeederId,
                        ["feeder_cabinet_id"] = feeder.CabinetId,
                        ["device_cabinet_id"] = node.CabinetId,
                    });
            }

            if (await db.Set<FeederControl>().AsNoTracking()
                    .Where(control => control.FeederId == feeder.FeederId)
                    .Select(control => new { control.NodeId, control.RelayNo })
                    .FirstOrDefaultAsync(ct) is { } held)
            {
                throw new LuxMapException(
                    ErrorCodes.AssetInUse,
                    HttpStatusCode.Conflict,
                    "That feeder is already switched by another relay. Unwire it there first.",
                    new Dictionary<string, object?> { ["feeder_id"] = feeder.FeederId, ["node_id"] = held.NodeId, ["relay_no"] = held.RelayNo });
            }
        }

        // 3.8: a command still open on this relay must never switch the feeder the relay no longer (or newly) carries.
        var now = UtcMicrosecondClock.UtcNow(clock);
        await lighting.ExpireStaleAsync([node.NodeId], now, ct);
        await lighting.SupersedeRelayAsync(node.NodeId, (short)relayNo, now, ct);

        if (current is not null)
        {
            db.Remove(current);
        }

        if (feederId is not null)
        {
            db.Add(new FeederControl
            {
                FeederId = feederId,
                NodeId = node.NodeId,
                CommuneId = node.CommuneId,
                CabinetId = node.CabinetId,
                RelayNo = (short)relayNo,
            });
        }

        Stamp(node);

        // Removal and insert in one SaveChanges: EF deletes before it inserts, so re-wiring relay N keeps
        // ux_feeder_control_node_id_relay_no satisfied.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure) when (Constraint(failure) is CabinetConstraints.RelayFeederSameCabinet or "pk_feeder_control")
        {
            // The feeder was moved to another cabinet, or taken by another device's relay, between the checks above and the
            // insert — the feeder row is not under this device's lock. The database settled it; answer like the checks would.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw Constraint(failure) == "pk_feeder_control"
                ? new LuxMapException(ErrorCodes.AssetInUse, HttpStatusCode.Conflict,
                    "That feeder was just wired to another relay. Unwire it there first.",
                    new Dictionary<string, object?> { ["feeder_id"] = feederId })
                : new LuxMapException(ErrorCodes.FeederNotInCabinet, HttpStatusCode.Conflict,
                    "That feeder no longer leaves from this device's cabinet.",
                    new Dictionary<string, object?> { ["feeder_id"] = feederId, ["device_cabinet_id"] = node.CabinetId });
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>Issues a new secret. Returned ONCE; only its SHA-256 is stored, and the previous one stops working at once.</summary>
    public async Task<IotNodeCredential> IssueCredentialAsync(string nodeId, CancellationToken ct)
    {
        var node = await RequireAsync(nodeId, ct);
        var secret = DeviceSecret.Create();
        var now = UtcMicrosecondClock.UtcNow(clock);

        node.CredentialHash = DeviceSecret.Hash(secret);
        node.CredentialSetAt = now;
        node.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return new IotNodeCredential(node.NodeId, secret, now);
    }

    private System.Linq.Expressions.Expression<Func<IotNode, IotNodeItem>> Row =>
        node => new IotNodeItem
        {
            NodeId = node.NodeId,
            CabinetId = node.CabinetId,
            CommuneId = node.CommuneId,
            NodeRole = node.NodeRole,
            DataSource = node.DataSource,
            SupportsRemoteControl = node.SupportsRemoteControl,
            HasCredential = node.CredentialHash != null,
            CredentialSetAt = node.CredentialSetAt,
            LastReportAt = node.LastReportAt,
            Relays = db.Set<FeederControl>()
                .Where(control => control.NodeId == node.NodeId)
                .OrderBy(control => control.RelayNo)
                .Select(control => new IotNodeRelay
                {
                    RelayNo = control.RelayNo,
                    FeederId = control.FeederId,
                    ControlMode = control.ControlMode,
                    ModeReportedAt = control.ModeReportedAt,
                })
                .ToList(),
            UpdatedAt = node.UpdatedAt,
        };

    private void Stamp(IotNode node) => node.UpdatedAt = UtcMicrosecondClock.UtcNow(clock);

    private async Task<IotNode> RequireAsync(string nodeId, CancellationToken ct)
        => await db.Set<IotNode>().FirstOrDefaultAsync(node => node.NodeId == nodeId, ct) ?? throw NotFound("device");

    /// <summary>D-R10: the team installs no device in the field — the CHECK refuses it too, this answers 400 first.</summary>
    private static DataSource RequireDeviceSource(DataSource source)
        => source is DataSource.CalibrationRig or DataSource.Simulated
            ? source
            : throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "A device is testbed hardware (calibration_rig) or demo data (simulated) — never field or public_imagery.",
                new Dictionary<string, object?> { ["data_source"] = WireEnum.Name(source) });

    private static LuxMapException CabinetTaken(string cabinetId)
        => new(ErrorCodes.AssetInUse, HttpStatusCode.Conflict, "That cabinet already carries a device (one per cabinet).",
            new Dictionary<string, object?> { ["cabinet_id"] = cabinetId, ["constraint"] = "ux_iot_node_cabinet_id" });

    private static LuxMapException NotFound(string what)
        => new(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound,
            $"That {what} does not exist, or it is outside your permitted commune scope.");

    private static string? Constraint(DbUpdateException failure) => (failure.InnerException as PostgresException)?.ConstraintName;
}
