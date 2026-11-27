using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.WorkOrders;

public sealed record FeederRepairRequest(string? Action, string? FeederId = null);

public sealed record FeederRepairResult(long Id, string WorkOrderId, string FeederId, string Status,
    DateTime StartedAt, DateTime? EndedAt);

public sealed partial class WorkOrderService
{
    public async Task<FeederRepairResult> RepairFeederAsync(string id, FeederRepairRequest request, CancellationToken ct)
    {
        if (request.Action is not ("start" or "finish")) throw OptionalJson.Invalid("action");
        if (request.FeederId is { } requested && string.IsNullOrWhiteSpace(requested))
            throw OptionalJson.Invalid("feeder_id");

        var order = await Find(id, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockRepairOrderAsync(order, ct);
        if (order.AssignedTo != ActorId) throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
        if (order.TaskKind != TaskKind.Repair || order.WoStatus != WorkOrderStatus.InProgress)
            throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict,
                ("work_order_id", id), ("wo_status", order.WoStatus), ("task_kind", order.TaskKind));

        var periods = db.Set<FeederRepairPeriod>().Where(x => x.WorkOrderId == id && x.CommuneId == order.CommuneId);
        FeederRepairPeriod period;
        FeederRepairResult? before = null;
        if (request.Action == "start")
        {
            if (request.FeederId is null)
            {
                var existing = await periods.Where(x => x.EndedAt == null).OrderBy(x => x.FeederId).ToArrayAsync(ct);
                if (existing.Length == 1) return ToRepairResult(existing[0]);
                if (existing.Length > 1) throw SelectFeederError(existing.Select(x => x.FeederId).ToArray());
            }
            var feederId = SelectFeeder(await RepairFeederIdsAsync(order, ct), request.FeederId);
            await LockRepairFeederAsync(feederId, order.CommuneId, ct);
            var active = await db.Set<FeederRepairPeriod>()
                .SingleOrDefaultAsync(x => x.FeederId == feederId && x.EndedAt == null, ct);
            if (active is not null)
            {
                if (active.WorkOrderId == id) return ToRepairResult(active);
                throw Error("FEEDER_ALREADY_REPAIRING", HttpStatusCode.Conflict, ("feeder_id", feederId));
            }

            period = new() {
                FeederId = feederId, WorkOrderId = id, CommuneId = order.CommuneId,
                StartedAt = UtcMicrosecondClock.UtcNow(),
            };
            db.Add(period);
        }
        else
        {
            if (request.FeederId is { } feederId) periods = periods.Where(x => x.FeederId == feederId);
            var active = await periods.Where(x => x.EndedAt == null)
                .OrderBy(x => x.FeederId).ToArrayAsync(ct);
            if (active.Length > 1) throw SelectFeederError(active.Select(x => x.FeederId).ToArray());
            if (active.Length == 0)
            {
                var latest = await periods.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync(ct);
                return latest is not null ? ToRepairResult(latest)
                    : throw Error("FEEDER_REPAIR_NOT_STARTED", HttpStatusCode.Conflict, ("work_order_id", id));
            }

            period = active[0];
            await LockRepairFeederAsync(period.FeederId, order.CommuneId, ct);
            before = ToRepairResult(period);
            period.EndedAt = FinishAt(period, UtcMicrosecondClock.UtcNow());
        }

        var now = period.EndedAt ?? period.StartedAt;
        order.UpdatedAt = now;
        Record(order, AuditAction.DetailsChanged, before,
            new { feeder_repair_action = request.Action, period.FeederId, period.StartedAt, period.EndedAt }, now);
        await Save(id, [], ct);
        await transaction.CommitAsync(ct);
        return ToRepairResult(period);
    }

    private async Task<string[]> RepairFeederIdsAsync(WorkOrder order, CancellationToken ct)
    {
        var candidates = await (
            from link in db.Set<WorkOrderFault>()
            join fault in db.Set<Fault>() on new { link.FaultId, link.CommuneId } equals new { fault.FaultId, fault.CommuneId }
            join pole in db.Set<Pole>().Where(x => x.CommuneId == order.CommuneId)
                on fault.PoleId equals pole.PoleId into poles
            from pole in poles.DefaultIfEmpty()
            where link.WorkOrderId == order.WorkOrderId && link.CommuneId == order.CommuneId && link.ReleasedAt == null
            select fault.FeederId ?? (pole == null ? null : pole.FeederId)).Distinct().ToArrayAsync(ct);
        return await db.Set<Feeder>().Where(x => x.CommuneId == order.CommuneId && candidates.Contains(x.FeederId))
            .OrderBy(x => x.FeederId).Select(x => x.FeederId).ToArrayAsync(ct);
    }

    private static string SelectFeeder(string[] candidates, string? requested)
    {
        if (candidates.Length == 0) throw Error("WORK_ORDER_FEEDER_NOT_FOUND", HttpStatusCode.Conflict);
        if (requested is not null && candidates.Contains(requested, StringComparer.Ordinal)) return requested;
        if (requested is null && candidates.Length == 1) return candidates[0];
        throw SelectFeederError(candidates);
    }

    private static LuxMapException SelectFeederError(string[] candidates) =>
        Error("WORK_ORDER_FEEDER_SELECTION_REQUIRED", HttpStatusCode.Conflict, ("feeder_ids", candidates));

    private async Task LockRepairOrderAsync(WorkOrder order, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            SELECT 1 FROM work_order WHERE work_order_id = {order.WorkOrderId} AND commune_id = {order.CommuneId} FOR UPDATE
            """, ct);
        await db.Entry(order).ReloadAsync(ct);
        if (db.Entry(order).State == EntityState.Detached)
            throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
        if (actor.Role == UserRole.FieldEngineer && order.AssignedTo != ActorId)
            throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
    }

    private async Task LockRepairFeederAsync(string feederId, string communeId, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<int>($"""
            SELECT 1 AS "Value" FROM feeder WHERE feeder_id = {feederId} AND commune_id = {communeId} FOR UPDATE
            """).ToArrayAsync(ct);
        if (rows.Length == 0) throw Error("WORK_ORDER_FEEDER_NOT_FOUND", HttpStatusCode.Conflict);
    }

    private async Task<FeederRepairResult[]> CloseFeederRepairsAsync(string id, DateTime now, CancellationToken ct)
    {
        var periods = await db.Set<FeederRepairPeriod>().Where(x => x.WorkOrderId == id && x.EndedAt == null)
            .OrderBy(x => x.FeederId).ToArrayAsync(ct);
        foreach (var period in periods)
        {
            await LockRepairFeederAsync(period.FeederId, period.CommuneId, ct);
            period.EndedAt = FinishAt(period, now);
        }
        return periods.Select(ToRepairResult).ToArray();
    }

    private static DateTime FinishAt(FeederRepairPeriod period, DateTime now) =>
        now < period.StartedAt ? period.StartedAt : now;

    private static FeederRepairResult ToRepairResult(FeederRepairPeriod period) =>
        new(period.Id, period.WorkOrderId, period.FeederId, period.EndedAt is null ? "repairing" : "finished",
            period.StartedAt, period.EndedAt);
}
