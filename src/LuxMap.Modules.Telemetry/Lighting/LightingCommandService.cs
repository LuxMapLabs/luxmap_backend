using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.Telemetry.Lighting;

/// <summary>
/// The whole life of a lighting command, independent of how it travels (LIGHT-CTRL D-1): a Manager's press, delivery to the
/// device, the device's acknowledgement, expiry and supersession. The HTTPS poll is the first adapter; an MQTT one would call
/// these same methods.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Every write locks the device row first</b> (<c>SELECT 1 … FOR UPDATE</c>, after a scoped read — the order the 2a
/// review settled) and reads again under the lock. A press on several devices locks them in ordinal <c>node_id</c> order;
/// relay wiring (<see cref="Registry.IotNodeRegistryService"/>) takes the same lock. The lock serialises the server's
/// writes; the ORDER the device executes in is <c>seq</c> (D-10).
/// </para>
/// <para>
/// Audit (3.5): exactly one event per <c>SaveChanges</c>, so a step that touches several commands saves once per command
/// inside the one transaction. Expiry is stored by whichever locked write comes next, as actor <c>system</c>.
/// </para>
/// </remarks>
public sealed class LightingCommandService(
    LuxMapDbContext db, IAuditTrail audit, ICurrentActorAccessor actor, TimeProvider clock, LightingOptions options)
{
    // ── Manager ────────────────────────────────────────────────────────────────────────────────

    public async Task<LightingPreview> PreviewAsync(string? feederId, string? segmentId, CancellationToken ct)
    {
        var plan = await PlanAsync(feederId, segmentId, ct);
        return new LightingPreview
        {
            TargetKind = plan.Kind,
            TargetId = plan.TargetId,
            Targets = [.. plan.Relays.Select(relay => relay.ToWire())],
            Excluded = plan.Excluded,
            AffectedSegmentIds = plan.AffectedSegmentIds,
            UncontrollablePoleCount = plan.UncontrollablePoleCount,
        };
    }

    /// <summary>
    /// Places a press: one command per switchable relay, the rest listed in <c>excluded</c> (D-8). Open commands on the same
    /// relays are superseded. <c>Replayed</c> is true when the same <c>client_op_id</c> and content were already placed.
    /// </summary>
    public async Task<(LightingRequestResult Result, bool Replayed)> CreateAsync(CreateLightingRequest request, CancellationToken ct)
    {
        var actorId = actor.UserId
            ?? throw new LuxMapException(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized, "Authentication required.");
        var key = request.ClientOpId!.Value;
        if (key == Guid.Empty)
        {
            throw Invalid("client_op_id", "client_op_id must be a non-empty UUID.");
        }

        var mode = request.Mode!.Value;
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { actorId, request.FeederId, request.SegmentId, mode }, LuxMapJsonOptions.Default)));

        if (await ReplayAsync(key, hash, actorId, ct) is { } replay)
        {
            return (replay, true);
        }

        var plan = await PlanAsync(request.FeederId, request.SegmentId, ct);
        if (plan.Relays.Count == 0)
        {
            throw new LuxMapException(ErrorCodes.NoControllableRelay, HttpStatusCode.Conflict,
                "No relay of this target can be switched remotely. See details.excluded.",
                new Dictionary<string, object?>
                {
                    ["excluded"] = plan.Excluded,
                    ["uncontrollable_pole_count"] = plan.UncontrollablePoleCount,
                });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var nodes = plan.Relays.Select(relay => (relay.NodeId, relay.CommuneId)).Distinct()
            .OrderBy(node => node.NodeId, StringComparer.Ordinal).ToArray();
        foreach (var (nodeId, communeId) in nodes)
        {
            await LockAsync(nodeId, communeId, ct);
        }

        // D-8: the press is checked again under the locks — the preview, and the plan above, may be stale.
        var locked = await PlanAsync(request.FeederId, request.SegmentId, ct);
        if (!locked.Relays.SequenceEqual(plan.Relays))
        {
            throw new LuxMapException(ErrorCodes.LightingTargetChanged, HttpStatusCode.Conflict,
                "The wiring of this target changed while the request was being placed. Preview it again, then send.");
        }

        plan = locked;

        var now = Now();
        var nodeIds = nodes.Select(node => node.NodeId).ToArray();
        await ExpireStaleAsync(nodeIds, now, ct);

        var relays = plan.Relays.Select(relay => (relay.NodeId, (short)relay.RelayNo)).ToHashSet();
        var superseded = (await db.Set<LightingCommand>()
                .Where(command => nodeIds.Contains(command.NodeId)
                    && (command.Status == LightingCommandStatus.Pending || command.Status == LightingCommandStatus.Delivered))
                .ToListAsync(ct))
            .Where(command => relays.Contains((command.NodeId, command.RelayNo)))
            .ToList();
        foreach (var command in superseded)
        {
            command.Status = LightingCommandStatus.Superseded;
            command.CompletedAt = now;
        }

        var requestId = Guid.CreateVersion7();
        var commands = new List<LightingCommand>();
        foreach (var relay in plan.Relays)
        {
            commands.Add(new LightingCommand
            {
                CommandId = await NextCommandIdAsync(ct),
                RequestId = requestId,
                NodeId = relay.NodeId,
                FeederId = relay.FeederId,
                CommuneId = relay.CommuneId,
                RelayNo = (short)relay.RelayNo,
                CabinetId = relay.CabinetId,
                DataSource = relay.DataSource,
                RequestedMode = mode,
                Status = LightingCommandStatus.Pending,
                CreatedAt = now,
                ExpiresAt = now + options.CommandTtl,
            });
        }

        db.Add(new LightingRequest
        {
            RequestId = requestId,
            ClientOpId = key,
            RequestHash = hash,
            CommuneId = plan.CommuneId,
            TargetKind = plan.Kind,
            TargetId = plan.TargetId,
            RequestedMode = mode,
            RequestedBy = actorId,
            RequestedAt = now,
            AffectedSegmentIds = [.. plan.AffectedSegmentIds],
            Excluded = JsonSerializer.Serialize(plan.Excluded, LuxMapJsonOptions.Default),
            UncontrollablePoleCount = plan.UncontrollablePoleCount,
        });
        db.AddRange(commands);

        audit.Record(new AuditChange(now, AuditActorKind.User, actorId, actor.Role, plan.CommuneId,
            AuditEntityType.LightingRequest, requestId.ToString(), AuditAction.Requested, null,
            new
            {
                target_kind = plan.Kind,
                target_id = plan.TargetId,
                mode,
                commands = commands.Select(command => new { command.CommandId, command.NodeId, command.RelayNo, command.FeederId }),
                superseded = superseded.Select(command => command.CommandId),
                excluded = plan.Excluded,
            }));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure)
            when ((failure.InnerException as PostgresException)?.ConstraintName == "ux_lighting_request_client_op_id")
        {
            // Two presses with one key raced past the replay check; the unique index settled it.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return (await ReplayAsync(key, hash, actorId, ct) ?? throw IdempotencyConflict(), true);
        }

        await transaction.CommitAsync(ct);
        return (await ResultAsync(requestId, ct), false);
    }

    /// <summary>Commands in the caller's communes, newest first (by <c>seq</c>, never by the text id).</summary>
    public async Task<PagedResult<LightingCommandItem>> ListAsync(
        Guid? requestId, string? feederId, LightingCommandStatus? status, PageRequest page, CancellationToken ct)
    {
        var now = Now();
        var query = db.Set<LightingCommand>().AsNoTracking();
        if (requestId is { } id)
        {
            query = query.Where(command => command.RequestId == id);
        }

        if (feederId is not null)
        {
            query = query.Where(command => command.FeederId == feederId);
        }

        // The filter matches the status as READ: an open command past its expiry is `expired`, not `pending`.
        query = status switch
        {
            null => query,
            LightingCommandStatus.Pending or LightingCommandStatus.Delivered
                => query.Where(command => command.Status == status && command.ExpiresAt > now),
            LightingCommandStatus.Expired => query.Where(command => command.Status == LightingCommandStatus.Expired
                || ((command.Status == LightingCommandStatus.Pending || command.Status == LightingCommandStatus.Delivered)
                    && command.ExpiresAt <= now)),
            _ => query.Where(command => command.Status == status),
        };

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(command => command.Seq).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return PagedResult<LightingCommandItem>.From(page, total, [.. rows.Select(row => Item(row, now))]);
    }

    // ── Device ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The device's open commands, newest per relay — a command already delivered is delivered AGAIN until acknowledged or
    /// expired, so a lost response loses nothing; the firmware drops duplicates by <c>command_id</c>. Only the first delivery
    /// changes the status. Also proves the control channel alive (<c>last_report_at</c>, D-5).
    /// </summary>
    public async Task<DeviceCommandBatch> PollAsync(string nodeId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var node = await LockDeviceAsync(nodeId, ct);
        var now = Now();

        if (node.LastReportAt is not { } last || last < now)
        {
            node.LastReportAt = now;
            await db.SaveChangesAsync(ct);
        }

        await ExpireStaleAsync([nodeId], now, ct);

        var wiring = await db.Set<FeederControl>().AsNoTracking()
            .Where(control => control.NodeId == nodeId)
            .ToDictionaryAsync(control => control.RelayNo, control => control.FeederId, ct);
        var open = await db.Set<LightingCommand>()
            .Where(command => command.NodeId == nodeId
                && (command.Status == LightingCommandStatus.Pending || command.Status == LightingCommandStatus.Delivered))
            .OrderByDescending(command => command.Seq)
            .ToListAsync(ct);

        var delivered = new List<DeviceCommand>();
        foreach (var command in open.GroupBy(command => command.RelayNo).Select(relay => relay.First()))
        {
            if (!wiring.TryGetValue(command.RelayNo, out var feeder) || feeder != command.FeederId)
            {
                // Wiring moved under the command without superseding it — never switch a feeder nobody asked to switch.
                await CloseAsync(command, LightingCommandStatus.Superseded, AuditActorKind.System, now, ct);
                continue;
            }

            if (command.Status == LightingCommandStatus.Pending)
            {
                command.Status = LightingCommandStatus.Delivered;
                command.DeliveredAt = now;
                audit.Record(new AuditChange(now, AuditActorKind.Iot, null, null, command.CommuneId,
                    AuditEntityType.LightingCommand, command.CommandId, AuditAction.Delivered,
                    new { status = LightingCommandStatus.Pending }, new { status = command.Status, node_id = nodeId, command.Seq }));
                await db.SaveChangesAsync(ct);
            }

            delivered.Add(new DeviceCommand(command.CommandId, command.Seq, command.RelayNo, command.RequestedMode, command.ExpiresAt));
        }

        await transaction.CommitAsync(ct);
        return new DeviceCommandBatch(now, [.. delivered.OrderBy(command => command.RelayNo)]);
    }

    /// <summary>
    /// The device's report after executing (3.4). A repeat of the same report is 200 with no change; a different one is 409.
    /// A report on a closed command is 409 <c>COMMAND_CLOSED</c> but is still KEPT, and its mode still written when newer.
    /// </summary>
    public async Task<DeviceAckResult> AckAsync(string nodeId, string commandId, DeviceAckRequest ack, CancellationToken ct)
    {
        var result = ack.Result!.Value;
        var error = string.IsNullOrWhiteSpace(ack.Error) ? null : ack.Error.Trim();
        if (result == LightingAckResult.Applied && ack.ReportedMode is null)
        {
            throw Invalid("reported_mode", "An applied command must report the mode the relay is now in.");
        }

        if (result == LightingAckResult.Failed && error is null)
        {
            throw Invalid("error", "A failed command must say why.");
        }

        if (result == LightingAckResult.Applied && error is not null)
        {
            throw Invalid("error", "An applied command carries no error.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Another device's command is the same 404 as no command: the filter alone would let a device of the same commune
        // read it, so the device's own node_id is part of every lookup (3.2).
        if (!await db.Set<LightingCommand>().AnyAsync(command => command.CommandId == commandId && command.NodeId == nodeId, ct))
        {
            throw CommandNotFound();
        }

        await LockDeviceAsync(nodeId, ct);
        var now = Now();
        await ExpireStaleAsync([nodeId], now, ct);
        var command = await db.Set<LightingCommand>().SingleAsync(row => row.CommandId == commandId, ct);

        if (ack.Seq != command.Seq)
        {
            throw Invalid("seq", "seq is not this command's seq.");
        }

        if (result == LightingAckResult.Applied && ack.ReportedMode != command.RequestedMode)
        {
            throw Invalid("reported_mode", "An applied command must report the mode it was asked to set; otherwise report failed.");
        }

        var control = await db.Set<FeederControl>().SingleOrDefaultAsync(row => row.FeederId == command.FeederId, ct);
        var wired = control is not null && control.NodeId == nodeId && control.RelayNo == command.RelayNo;

        if (command.Status == LightingCommandStatus.Delivered && !wired)
        {
            await CloseAsync(command, LightingCommandStatus.Superseded, AuditActorKind.System, now, ct);
        }

        switch (command.Status)
        {
            case LightingCommandStatus.Pending:
                await transaction.CommitAsync(ct);
                throw new LuxMapException(ErrorCodes.CommandNotDelivered, HttpStatusCode.Conflict,
                    "That command has not been fetched yet; fetch it, execute it, then acknowledge it.");

            case LightingCommandStatus.Delivered:
            {
                var before = new { status = command.Status };
                command.Status = result == LightingAckResult.Applied ? LightingCommandStatus.Applied : LightingCommandStatus.Failed;
                command.CompletedAt = now;
                command.ReportedMode = ack.ReportedMode;
                command.Error = error;
                var recorded = Record(control!, ack.ReportedMode, command.Seq, now);
                audit.Record(new AuditChange(now, AuditActorKind.Iot, null, null, command.CommuneId,
                    AuditEntityType.LightingCommand, command.CommandId,
                    result == LightingAckResult.Applied ? AuditAction.Applied : AuditAction.Failed,
                    before, new { status = command.Status, reported_mode = ack.ReportedMode, error, command.Seq, mode_recorded = recorded }));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new DeviceAckResult(command.CommandId, command.Status);
            }

            case LightingCommandStatus.Applied or LightingCommandStatus.Failed:
            {
                var same = command.Status == (result == LightingAckResult.Applied ? LightingCommandStatus.Applied : LightingCommandStatus.Failed)
                    && command.ReportedMode == ack.ReportedMode && command.Error == error;
                await transaction.CommitAsync(ct);
                return same ? new DeviceAckResult(command.CommandId, command.Status) : throw IdempotencyConflict();
            }

            default:
            {
                // Expired or superseded: the device's report is still the truth about the relay (I-6).
                var recorded = wired && Record(control!, ack.ReportedMode, command.Seq, now);
                audit.Record(new AuditChange(now, AuditActorKind.Iot, null, null, command.CommuneId,
                    AuditEntityType.LightingCommand, command.CommandId, AuditAction.Reported, null,
                    new { status = command.Status, result, reported_mode = ack.ReportedMode, error, command.Seq, mode_recorded = recorded }));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                throw new LuxMapException(ErrorCodes.CommandClosed, HttpStatusCode.Conflict,
                    "That command already closed; the report was kept in its history.",
                    new Dictionary<string, object?> { ["status"] = WireEnum.Name(command.Status), ["mode_recorded"] = recorded });
            }
        }
    }

    // ── Shared with the registry (relay wiring) ────────────────────────────────────────────────

    /// <summary>Stores <c>expired</c> on open commands past their expiry, one save + one <c>system</c> audit each. Caller holds the lock.</summary>
    public async Task ExpireStaleAsync(IReadOnlyCollection<string> nodeIds, DateTime now, CancellationToken ct)
    {
        var stale = await db.Set<LightingCommand>()
            .Where(command => nodeIds.Contains(command.NodeId)
                && (command.Status == LightingCommandStatus.Pending || command.Status == LightingCommandStatus.Delivered)
                && command.ExpiresAt <= now)
            .OrderBy(command => command.Seq)
            .ToListAsync(ct);

        foreach (var command in stale)
        {
            await CloseAsync(command, LightingCommandStatus.Expired, AuditActorKind.System, now, ct);
        }
    }

    /// <summary>
    /// Supersedes the open commands of one relay before it is unwired or rewired (3.8) — one save + one audit each, actor the
    /// Manager doing the wiring. Caller holds the lock and has stored expiry first.
    /// </summary>
    public async Task SupersedeRelayAsync(string nodeId, short relayNo, DateTime now, CancellationToken ct)
    {
        var open = await db.Set<LightingCommand>()
            .Where(command => command.NodeId == nodeId && command.RelayNo == relayNo
                && (command.Status == LightingCommandStatus.Pending || command.Status == LightingCommandStatus.Delivered))
            .OrderBy(command => command.Seq)
            .ToListAsync(ct);

        foreach (var command in open)
        {
            await CloseAsync(command, LightingCommandStatus.Superseded, AuditActorKind.User, now, ct);
        }
    }

    // ── Internals ──────────────────────────────────────────────────────────────────────────────

    private async Task CloseAsync(LightingCommand command, LightingCommandStatus status, AuditActorKind by, DateTime now, CancellationToken ct)
    {
        var before = new { status = command.Status };
        command.Status = status;
        command.CompletedAt = now;
        var user = by == AuditActorKind.User;
        audit.Record(new AuditChange(now, by, user ? actor.UserId : null, user ? actor.Role : null, command.CommuneId,
            AuditEntityType.LightingCommand, command.CommandId,
            status == LightingCommandStatus.Expired ? AuditAction.Expired : AuditAction.Superseded,
            before, new { status }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>D-10: a reported mode is written only when its seq is newer than the one that last set the relay.</summary>
    private static bool Record(FeederControl control, FeederControlMode? mode, long seq, DateTime now)
    {
        if (mode is not { } reported || control.ModeSeq is { } last && last >= seq)
        {
            return false;
        }

        control.ControlMode = reported;
        control.ModeReportedAt = now;
        control.ModeSeq = seq;
        return true;
    }

    private async Task<Plan> PlanAsync(string? feederId, string? segmentId, CancellationToken ct)
    {
        if ((feederId is null) == (segmentId is null))
        {
            throw Invalid("feeder_id", "Send exactly one of feeder_id or segment_id.");
        }

        var kind = feederId is not null ? LightingTargetKind.Feeder : LightingTargetKind.Segment;
        string communeId;
        string[] feeders;
        IQueryable<Pole> scope;
        if (feederId is not null)
        {
            communeId = await db.Set<Feeder>().Where(feeder => feeder.FeederId == feederId)
                .Select(feeder => feeder.CommuneId).FirstOrDefaultAsync(ct) ?? throw NotFound("feeder");
            feeders = [feederId];
            scope = db.Set<Pole>().Where(pole => pole.FeederId == feederId);
        }
        else
        {
            communeId = await db.Set<RoadSegment>().Where(segment => segment.SegmentId == segmentId)
                .Select(segment => segment.CommuneId).FirstOrDefaultAsync(ct) ?? throw NotFound("segment");
            scope = db.Set<Pole>().Where(pole => pole.SegmentId == segmentId);
            feeders = [.. (await scope.Where(pole => pole.FeederId != null).Select(pole => pole.FeederId!).Distinct().ToListAsync(ct))
                .OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal)];
        }

        var wiring = await (
                from control in db.Set<FeederControl>()
                where feeders.Contains(control.FeederId)
                join node in db.Set<IotNode>() on control.NodeId equals node.NodeId
                select new
                {
                    control.FeederId, control.NodeId, control.RelayNo, control.CabinetId, node.CommuneId,
                    node.SupportsRemoteControl, HasCredential = node.CredentialHash != null, node.DataSource,
                })
            .AsNoTracking()
            .ToDictionaryAsync(row => row.FeederId, ct);

        var relays = new List<PlannedRelay>();
        var excluded = new List<LightingExclusion>();
        foreach (var feeder in feeders)
        {
            if (!wiring.TryGetValue(feeder, out var row))
            {
                excluded.Add(new LightingExclusion { FeederId = feeder, Reason = LightingExclusionReason.NotWired });
            }
            else if (!row.SupportsRemoteControl || !row.HasCredential)
            {
                excluded.Add(new LightingExclusion
                {
                    FeederId = feeder,
                    NodeId = row.NodeId,
                    RelayNo = row.RelayNo,
                    Reason = row.SupportsRemoteControl ? LightingExclusionReason.NoCredential : LightingExclusionReason.RemoteControlUnsupported,
                });
            }
            else
            {
                relays.Add(new PlannedRelay(row.NodeId, row.RelayNo, row.FeederId, row.CabinetId, row.CommuneId, row.DataSource));
            }
        }

        var switched = relays.Select(relay => relay.FeederId).ToArray();
        var left = excluded.Select(exclusion => exclusion.FeederId).ToArray();
        var uncontrollable = await scope.CountAsync(pole => pole.FeederId == null || left.Contains(pole.FeederId), ct);
        var affected = (await db.Set<Pole>().Where(pole => pole.FeederId != null && switched.Contains(pole.FeederId))
                .Select(pole => pole.SegmentId).Distinct().ToListAsync(ct))
            .OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal).ToArray();

        return new Plan(kind, feederId ?? segmentId!, communeId, relays, excluded, affected, uncontrollable);
    }

    private async Task<LightingRequestResult?> ReplayAsync(Guid key, string hash, string actorId, CancellationToken ct)
    {
        // Unfiltered on purpose: a key is global. Nothing of another commune's request is returned — only a 409.
        var existing = await db.Set<LightingRequest>().IgnoreQueryFilters().AsNoTracking()
            .Where(request => request.ClientOpId == key)
            .Select(request => new { request.RequestId, request.RequestHash, request.RequestedBy })
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return null;
        }

        return existing.RequestedBy == actorId && existing.RequestHash == hash
            ? await ResultAsync(existing.RequestId, ct)
            : throw IdempotencyConflict();
    }

    private async Task<LightingRequestResult> ResultAsync(Guid requestId, CancellationToken ct)
    {
        var request = await db.Set<LightingRequest>().AsNoTracking().FirstOrDefaultAsync(row => row.RequestId == requestId, ct)
            ?? throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound,
                "That request is outside your permitted commune scope.");
        var commands = await db.Set<LightingCommand>().AsNoTracking()
            .Where(command => command.RequestId == requestId).OrderBy(command => command.Seq).ToListAsync(ct);
        var now = Now();

        return new LightingRequestResult
        {
            RequestId = request.RequestId,
            ClientOpId = request.ClientOpId,
            TargetKind = request.TargetKind,
            TargetId = request.TargetId,
            Mode = request.RequestedMode,
            RequestedBy = request.RequestedBy,
            RequestedAt = request.RequestedAt,
            Commands = [.. commands.Select(command => Item(command, now))],
            Excluded = JsonSerializer.Deserialize<LightingExclusion[]>(request.Excluded, LuxMapJsonOptions.Default) ?? [],
            AffectedSegmentIds = request.AffectedSegmentIds,
            UncontrollablePoleCount = request.UncontrollablePoleCount,
        };
    }

    private static LightingCommandItem Item(LightingCommand command, DateTime now) => new()
    {
        CommandId = command.CommandId,
        RequestId = command.RequestId,
        Seq = command.Seq,
        NodeId = command.NodeId,
        RelayNo = command.RelayNo,
        FeederId = command.FeederId,
        CabinetId = command.CabinetId,
        DataSource = command.DataSource,
        RequestedMode = command.RequestedMode,
        Status = command.IsOpen && command.ExpiresAt <= now ? LightingCommandStatus.Expired : command.Status,
        CreatedAt = command.CreatedAt,
        ExpiresAt = command.ExpiresAt,
        DeliveredAt = command.DeliveredAt,
        CompletedAt = command.CompletedAt,
        ReportedMode = command.ReportedMode,
        Error = command.Error,
    };

    /// <summary>The device reads ITSELF through the filter (its scope is its commune), then locks, then reads again tracked.</summary>
    private async Task<IotNode> LockDeviceAsync(string nodeId, CancellationToken ct)
    {
        var communeId = await db.Set<IotNode>().Where(node => node.NodeId == nodeId)
            .Select(node => node.CommuneId).FirstOrDefaultAsync(ct) ?? throw NotFound("device");
        await LockAsync(nodeId, communeId, ct);
        return await db.Set<IotNode>().SingleAsync(node => node.NodeId == nodeId, ct);
    }

    private Task LockAsync(string nodeId, string communeId, CancellationToken ct)
        => db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM iot_node WHERE node_id = {0} AND commune_id = {1} FOR UPDATE", [nodeId, communeId], ct);

    /// <summary>The same draw the column DEFAULT makes; the id is needed before saving, for the request's audit event.</summary>
    private Task<string> NextCommandIdAsync(CancellationToken ct)
    {
        var sql = $"SELECT {Shared.Contracts.PrefixedIds.LightingCommand.DefaultValueSql} AS \"Value\"";
        return db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
    }

    private DateTime Now() => UtcMicrosecondClock.UtcNow(clock);

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message, new Dictionary<string, object?> { ["field"] = field });

    private static LuxMapException NotFound(string what)
        => new(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, $"That {what} does not exist, or it is outside your permitted commune scope.");

    private static LuxMapException CommandNotFound()
        => new(ErrorCodes.CommandNotFound, HttpStatusCode.NotFound, "No such command for this device.");

    private static LuxMapException IdempotencyConflict()
        => new("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict, "That key was already used for a different request.");

    private sealed record PlannedRelay(string NodeId, int RelayNo, string FeederId, string CabinetId, string CommuneId, DataSource DataSource)
    {
        public LightingTarget ToWire() => new() { NodeId = NodeId, RelayNo = RelayNo, FeederId = FeederId, CabinetId = CabinetId };
    }

    private sealed record Plan(
        LightingTargetKind Kind,
        string TargetId,
        string CommuneId,
        IReadOnlyList<PlannedRelay> Relays,
        IReadOnlyList<LightingExclusion> Excluded,
        IReadOnlyList<string> AffectedSegmentIds,
        int UncontrollablePoleCount);
}
