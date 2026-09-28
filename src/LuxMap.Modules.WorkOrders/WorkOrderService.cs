using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.WorkOrders;

public sealed class WorkOrderService(LuxMapDbContext db, ICurrentActorAccessor actor,
    IAuditTrail audit, FaultTransitions transitions)
{
    private string ActorId => actor.UserId ?? throw new LuxMapException("UNAUTHENTICATED", HttpStatusCode.Unauthorized, "Authentication required.");

    private static LuxMapException Error(string code, HttpStatusCode status, params (string Key, object? Value)[] details)
        => new(code, status, code.Replace('_', ' '), details.ToDictionary(x => x.Key, x => x.Value));

    private async Task<WorkOrder> Find(string id, CancellationToken ct)
        => await db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.WorkOrderId == id, ct)
            ?? throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);

    private IQueryable<AppUser> EligibleUsers(string commune) => db.Set<AppUser>().Where(user =>
        user.Role == UserRole.FieldEngineer && !user.IsLocked
        && db.Set<AppUserCommune>().Any(link => link.UserId == user.UserId && link.CommuneId == commune));

    private async Task RequireAssignee(string id, string commune, CancellationToken ct)
    {
        if (!await EligibleUsers(commune).AnyAsync(user => user.UserId == id, ct))
            throw Error("ASSIGNEE_NOT_ELIGIBLE", HttpStatusCode.Conflict, ("assigned_to", id));
    }

    public async Task<PagedResult<WorkOrderAssignee>> Assignees(string commune, PageRequest page, CancellationToken ct)
    {
        CommuneFilter.Narrow(db.CurrentCommuneScope, [commune]);
        var query = EligibleUsers(commune);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.UserId.Length).ThenBy(x => x.UserId)
            .Skip(page.Skip).Take(page.PageSize).Select(x => new WorkOrderAssignee(x.UserId, x.FullName)).ToListAsync(ct);
        return PagedResult<WorkOrderAssignee>.From(page, count, items);
    }

    public async Task<PagedResult<WorkOrderItem>> List(WorkOrderStatus[]? statuses, TaskKind? kind,
        string? assigned, string? segment, string[]? communes, DateOnly? from, DateOnly? to,
        PageRequest page, CancellationToken ct)
    {
        if (from > to) throw OptionalJson.Invalid("scheduled_from");
        var scope = CommuneFilter.Narrow(db.CurrentCommuneScope, communes);
        var query = db.Set<WorkOrder>().AsNoTracking();
        if (scope is not null) query = query.Where(x => scope.Contains(x.CommuneId));
        if (statuses is not null) query = query.Where(x => statuses.Contains(x.WoStatus));
        if (kind is not null) query = query.Where(x => x.TaskKind == kind);
        if (assigned is not null)
        {
            var user = assigned == "me" ? ActorId : assigned;
            query = query.Where(x => x.AssignedTo == user);
        }
        if (segment is not null) query = query.Where(x => x.SegmentId == segment);
        if (from is not null) query = query.Where(x => x.ScheduledDate >= from);
        if (to is not null) query = query.Where(x => x.ScheduledDate <= to);
        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.WorkOrderId.Length)
            .ThenByDescending(x => x.WorkOrderId).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        var ids = orders.Select(x => x.WorkOrderId).ToArray();
        var members = await (from link in db.Set<WorkOrderFault>()
                             join fault in db.Set<Fault>() on link.FaultId equals fault.FaultId
                             where ids.Contains(link.WorkOrderId)
                             orderby fault.CreatedAt, fault.FaultId.Length, fault.FaultId
                             select new { link.WorkOrderId, fault.FaultId, fault.PriorityScore }).ToListAsync(ct);
        var items = orders.Select(wo => new WorkOrderItem
        {
            WorkOrderId = wo.WorkOrderId, Title = wo.Title, CommuneId = wo.CommuneId, TaskKind = wo.TaskKind,
            SegmentId = wo.SegmentId, ClusterId = wo.ClusterId, WoStatus = wo.WoStatus, AssignedTo = wo.AssignedTo,
            CreatedAt = wo.CreatedAt, UpdatedAt = wo.UpdatedAt, DueDate = wo.DueDate, ScheduledDate = wo.ScheduledDate,
            FaultIds = members.Where(x => x.WorkOrderId == wo.WorkOrderId).Select(x => x.FaultId).ToArray(),
            PriorityScore = members.Where(x => x.WorkOrderId == wo.WorkOrderId).Select(x => x.PriorityScore).DefaultIfEmpty().Max(),
        }).ToArray();
        return PagedResult<WorkOrderItem>.From(page, total, items);
    }

    public async Task<WorkOrderDetail> Detail(string id, CancellationToken ct)
    {
        var wo = await Find(id, ct);
        var members = await (from link in db.Set<WorkOrderFault>()
                             join fault in db.Set<Fault>() on link.FaultId equals fault.FaultId
                             join pole in db.Set<Pole>() on fault.PoleId equals pole.PoleId into poles
                             from pole in poles.DefaultIfEmpty()
                             where link.WorkOrderId == id
                             orderby fault.CreatedAt, fault.FaultId.Length, fault.FaultId
                             select new { Fault = fault, link.InspectionOutcome,
                                 Lat = fault.Lat ?? (pole == null ? 0 : pole.Geom.Y),
                                 Lng = fault.Lng ?? (pole == null ? 0 : pole.Geom.X) }).ToListAsync(ct);
        return new WorkOrderDetail
        {
            WorkOrderId = id, Title = wo.Title, CommuneId = wo.CommuneId, TaskKind = wo.TaskKind,
            SegmentId = wo.SegmentId, ClusterId = wo.ClusterId, WoStatus = wo.WoStatus, AssignedTo = wo.AssignedTo,
            CreatedAt = wo.CreatedAt, UpdatedAt = wo.UpdatedAt, DueDate = wo.DueDate, ScheduledDate = wo.ScheduledDate,
            FaultIds = members.Select(x => x.Fault.FaultId).ToArray(),
            PriorityScore = members.Select(x => x.Fault.PriorityScore).DefaultIfEmpty().Max(),
            Note = wo.Note, ReviewNote = wo.ReviewNote, ReportNote = wo.ReportNote, CreatedBy = wo.CreatedBy,
            AssignedAt = wo.AssignedAt, StartedAt = wo.StartedAt, CompletedAt = wo.CompletedAt, ClosedAt = wo.ClosedAt,
            AssigneeEligible = wo.AssignedTo is null ? null : await EligibleUsers(wo.CommuneId).AnyAsync(x => x.UserId == wo.AssignedTo, ct),
            AllowedActions = WorkOrderRules.AllowedActions(wo.WoStatus, actor.Role, wo.AssignedTo == actor.UserId),
            Faults = members.Select(x => new WorkOrderFaultDetail(x.Fault.FaultId, x.Fault.PoleId, x.Fault.SegmentId,
                new(x.Lat, x.Lng), x.Fault.FaultType, x.Fault.FaultStatus, x.Fault.Severity, x.InspectionOutcome)).ToArray(),
        };
    }

    public async Task<WorkOrderDetail> Create(CreateWorkOrderRequest request, CancellationToken ct)
    {
        if (new[] { request.WorkOrderId, request.CommuneId, request.WoStatus, request.ClusterId, request.PriorityScore }.Any(OptionalJson.Present))
            throw Error("SERVER_OWNED_FIELD", HttpStatusCode.BadRequest);
        var title = ValidTitle(request.Title);
        Schedule(request.ScheduledDate, request.DueDate);
        var kind = request.TaskKind ?? throw OptionalJson.Invalid("task_kind");
        var ids = request.FaultIds ?? [];
        if (ids.Length > 200 || ids.Distinct().Count() != ids.Length || ids.Any(string.IsNullOrWhiteSpace)
            || (ids.Length > 0 && request.SegmentId is not null)
            || (ids.Length == 0 && (kind != TaskKind.Inspection || string.IsNullOrWhiteSpace(request.SegmentId))))
            throw OptionalJson.Invalid("fault_ids / segment_id");
        var faults = await db.Set<Fault>().Where(x => ids.Contains(x.FaultId))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).ToListAsync(ct);
        var missing = ids.Except(faults.Select(x => x.FaultId)).ToArray();
        if (missing.Length != 0) throw Error("FAULT_NOT_FOUND", HttpStatusCode.NotFound, ("fault_ids", missing));
        string commune;
        string? segment;
        if (faults.Count > 0)
        {
            if (faults.Select(x => x.CommuneId).Distinct().Count() != 1)
                throw Error("CROSS_COMMUNE_REFERENCE", HttpStatusCode.Conflict);
            commune = faults[0].CommuneId;
            segment = faults.Where(x => x.SegmentId is not null).OrderByDescending(x => x.PriorityScore)
                .ThenBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).FirstOrDefault()?.SegmentId;
            var invalid = faults.Where(x => !WorkOrderRules.Eligible(kind, x.FaultStatus)).ToArray();
            if (invalid.Length > 0) throw Error("FAULT_STATUS_NOT_ELIGIBLE", HttpStatusCode.Conflict,
                ("task_kind", kind), ("accepted_statuses", FaultStatusSets.Open.Where(x => WorkOrderRules.Eligible(kind, x)).ToArray()),
                ("faults", invalid.Select(x => new { x.FaultId, x.FaultStatus }).ToArray()));
            var conflict = await db.Set<WorkOrderFault>().FirstOrDefaultAsync(x => ids.Contains(x.FaultId) && x.ReleasedAt == null, ct);
            if (conflict is not null) throw LinkConflict(conflict);
        }
        else
        {
            var road = await db.Set<RoadSegment>().FirstOrDefaultAsync(x => x.SegmentId == request.SegmentId, ct)
                ?? throw Error("ASSET_NOT_FOUND", HttpStatusCode.NotFound);
            commune = road.CommuneId;
            segment = road.SegmentId;
        }
        if (request.AssignedTo is not null) await RequireAssignee(request.AssignedTo, commune, ct);
        var now = DateTime.UtcNow;
        var clusters = faults.Select(x => x.ClusterId).Where(x => x is not null).Distinct().ToArray();
        // SQL is constructed exclusively from the server's immutable prefixed-ID specification.
        var idSql = $"SELECT {PrefixedIds.WorkOrder.DefaultValueSql} AS \"Value\"";
        var id = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);
        var wo = new WorkOrder
        {
            WorkOrderId = id, CommuneId = commune, TaskKind = kind, Title = title, SegmentId = segment,
            ClusterId = clusters.Length == 1 ? clusters[0] : null, CreatedBy = ActorId,
            AssignedTo = request.AssignedTo, AssignedAt = request.AssignedTo is null ? null : now,
            WoStatus = request.AssignedTo is null ? WorkOrderStatus.Open : WorkOrderStatus.Assigned,
            DueDate = request.DueDate, ScheduledDate = request.ScheduledDate, Note = request.Note,
            CreatedAt = now, UpdatedAt = now,
        };
        db.Add(wo);
        foreach (var fault in faults) db.Add(new WorkOrderFault
        {
            WorkOrderId = id, FaultId = fault.FaultId, CommuneId = commune, LinkedAt = now,
        });
        Record(wo, AuditAction.Created, null, Snapshot(wo, ids), now, request.Note);
        await Save(id, ids, ct);
        return await Detail(id, ct);
    }

    public async Task<WorkOrderDetail> Patch(string id, PatchWorkOrderRequest request, CancellationToken ct)
    {
        if (OptionalJson.Present(request.CommuneId)) throw Error("SERVER_OWNED_FIELD", HttpStatusCode.BadRequest);
        if (new[] { request.WoStatus, request.AssignedTo, request.FaultIds, request.TaskKind, request.SegmentId }.Any(OptionalJson.Present))
            throw Error("VALIDATION_FAILED", HttpStatusCode.BadRequest, ("endpoint", "Use /assignee or a status action; targets and task_kind are immutable."));
        if (!new[] { request.Title, request.DueDate, request.ScheduledDate }.Any(OptionalJson.Present)) throw OptionalJson.Invalid("body");
        var title = OptionalJson.Present(request.Title) ? ValidTitle(OptionalJson.Text(request.Title, "title", false)) : null;
        var due = OptionalJson.Date(request.DueDate, "due_date");
        var scheduled = OptionalJson.Date(request.ScheduledDate, "scheduled_date");
        var wo = await Find(id, ct);
        RequireAction(wo, "edit");
        var before = Snapshot(wo);
        var nextDue = OptionalJson.Present(request.DueDate) ? due : wo.DueDate;
        var nextScheduled = OptionalJson.Present(request.ScheduledDate) ? scheduled : wo.ScheduledDate;
        Schedule(nextScheduled, nextDue);
        if ((title ?? wo.Title) == wo.Title && nextDue == wo.DueDate && nextScheduled == wo.ScheduledDate) return await Detail(id, ct);
        wo.Title = title ?? wo.Title;
        wo.DueDate = nextDue;
        wo.ScheduledDate = nextScheduled;
        var now = DateTime.UtcNow;
        wo.UpdatedAt = now;
        Record(wo, AuditAction.DetailsChanged, before, Snapshot(wo), now);
        await Save(id, [], ct);
        return await Detail(id, ct);
    }

    public async Task<WorkOrderDetail> Assign(string id, AssignWorkOrderRequest request, CancellationToken ct)
    {
        if (!OptionalJson.Present(request.AssignedTo)) throw OptionalJson.Invalid("assigned_to");
        var assigned = OptionalJson.Text(request.AssignedTo, "assigned_to");
        var wo = await Find(id, ct);
        RequireAction(wo, assigned is null ? "unassign" : "assign");
        if (assigned is not null) await RequireAssignee(assigned, wo.CommuneId, ct);
        if (wo.AssignedTo == assigned) return await Detail(id, ct);
        var before = Snapshot(wo);
        var action = assigned is null ? AuditAction.Unassigned : wo.AssignedTo is null ? AuditAction.Assigned : AuditAction.Reassigned;
        var now = DateTime.UtcNow;
        wo.AssignedTo = assigned;
        wo.AssignedAt = assigned is null ? null : now;
        wo.WoStatus = assigned is null ? WorkOrderStatus.Open : WorkOrderStatus.Assigned;
        wo.StartedAt = null;
        wo.UpdatedAt = now;
        Record(wo, action, before, Snapshot(wo), now);
        await Save(id, [], ct);
        return await Detail(id, ct);
    }

    public async Task<WorkOrderDetail> Act(string id, string action, string? note,
        JsonElement outcomes, CancellationToken ct)
    {
        note = note?.Trim();
        if ((action == "complete" && (note?.Length ?? 0) < 10)
            || ((action is "return" or "cancel") && string.IsNullOrEmpty(note))) throw OptionalJson.Invalid("note");
        var wo = await Find(id, ct);
        RequireAction(wo, action);
        var links = await db.Set<WorkOrderFault>().Where(x => x.WorkOrderId == id).ToListAsync(ct);
        var ids = links.Select(x => x.FaultId).ToArray();
        var before = Snapshot(wo, ids, links: links);
        if (action == "complete")
        {
            if (wo.TaskKind == TaskKind.Inspection && links.Count > 0)
            {
                FaultOutcomeRequest[]? reports;
                try { reports = outcomes.Deserialize<FaultOutcomeRequest[]>(LuxMapJsonOptions.Default); }
                catch (Exception error) when (error is JsonException or InvalidOperationException)
                { throw OptionalJson.Invalid("fault_outcomes"); }
                if (reports is null || reports.Length != links.Count || reports.Any(x => x is null || x.Outcome is null)
                    || !reports.Select(x => x.FaultId).Order().SequenceEqual(ids.Order())) throw OptionalJson.Invalid("fault_outcomes");
                foreach (var link in links) link.InspectionOutcome = reports.Single(x => x.FaultId == link.FaultId).Outcome;
            }
            else if (OptionalJson.Present(outcomes)) throw OptionalJson.Invalid("fault_outcomes");
        }
        var now = DateTime.UtcNow;
        var changes = new List<FaultChange>();
        var skipped = new List<string>();
        if (wo.TaskKind == TaskKind.Repair && action is "start" or "verify")
        {
            var faults = await db.Set<Fault>().Where(x => ids.Contains(x.FaultId)).ToListAsync(ct);
            foreach (var fault in faults)
            {
                var from = fault.FaultStatus;
                if (action == "start" && from == FaultStatus.Confirmed)
                    transitions.Apply(fault, FaultStatus.InProgress, now, ActorId);
                else if (action == "verify" && from == FaultStatus.InProgress)
                {
                    transitions.Apply(fault, FaultStatus.Resolved, now, wo.AssignedTo!, wo.CompletedAt);
                    transitions.Apply(fault, FaultStatus.Verified, now, ActorId);
                }
                else { skipped.Add(fault.FaultId); continue; }
                changes.Add(new(fault.FaultId, from, fault.FaultStatus));
            }
        }
        switch (action)
        {
            case "start": wo.WoStatus = WorkOrderStatus.InProgress; wo.StartedAt = now; break;
            case "complete": wo.WoStatus = WorkOrderStatus.Done; wo.CompletedAt = now; wo.ReportNote = note; break;
            case "return": wo.WoStatus = WorkOrderStatus.InProgress; wo.ReviewNote = note; wo.CompletedAt = null; break;
            case "verify": wo.WoStatus = WorkOrderStatus.Verified; wo.ReviewNote = note; wo.ClosedAt = now; break;
            case "cancel": wo.WoStatus = WorkOrderStatus.Cancelled; wo.ReviewNote = note; wo.ClosedAt = now; break;
        }
        if (wo.ClosedAt is not null) foreach (var link in links) link.ReleasedAt = now;
        wo.UpdatedAt = now;
        var auditAction = action switch
        {
            "start" => AuditAction.Started, "complete" => AuditAction.Completed,
            "return" => AuditAction.Returned, "verify" => AuditAction.Verified, _ => AuditAction.Cancelled,
        };
        Record(wo, auditAction, before, Snapshot(wo, ids, changes.ToArray(), skipped.ToArray(), links), now, note);
        await Save(id, [], ct);
        return await Detail(id, ct);
    }

    private void RequireAction(WorkOrder wo, string action)
    {
        if (!WorkOrderRules.Allows(wo.WoStatus, action)) throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict,
            ("work_order_id", wo.WorkOrderId), ("wo_status", wo.WoStatus), ("action", action),
            ("allowed_actions", WorkOrderRules.AllowedActions(wo.WoStatus, actor.Role, wo.AssignedTo == actor.UserId)));
    }

    private static string ValidTitle(string? value)
    {
        value = value?.Trim();
        return value is { Length: > 0 and <= 200 } ? value : throw OptionalJson.Invalid("title");
    }

    private static void Schedule(DateOnly? scheduled, DateOnly? due)
    {
        if (scheduled > due) throw OptionalJson.Invalid("scheduled_date");
    }

    private static LuxMapException LinkConflict(WorkOrderFault link) => Error("FAULT_ALREADY_IN_WORK_ORDER",
        HttpStatusCode.Conflict, ("fault_id", link.FaultId), ("work_order_id", link.WorkOrderId));

    private async Task Save(string id, string[] faultIds, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            // Reapply BOTH filters: reassignment during a race can make the WO invisible now.
            var current = await Find(id, ct);
            throw Error("CONCURRENT_MODIFICATION", HttpStatusCode.Conflict, ("wo_status", current.WoStatus));
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "ux_work_order_fault_fault_id_active" })
        {
            db.ChangeTracker.Clear();
            var link = await db.Set<WorkOrderFault>().FirstAsync(x => faultIds.Contains(x.FaultId) && x.ReleasedAt == null, ct);
            throw LinkConflict(link);
        }
    }

    private void Record(WorkOrder wo, AuditAction action, object? before, object after, DateTime now, string? note = null)
        => audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, wo.CommuneId,
            AuditEntityType.WorkOrder, wo.WorkOrderId, action, before, after, note));

    // Only explicit snapshots cross the audit boundary: never serialize an EF entity or AppUser.
    private static WorkOrderSnapshot Snapshot(WorkOrder wo, string[]? ids = null, FaultChange[]? changes = null,
        string[]? skipped = null, List<WorkOrderFault>? links = null)
        => new(wo.WorkOrderId, wo.Title, wo.TaskKind, wo.WoStatus, wo.AssignedTo, wo.AssignedAt,
            wo.DueDate, wo.ScheduledDate, wo.Note, wo.ReviewNote, wo.ReportNote, wo.StartedAt, wo.CompletedAt,
            wo.ClosedAt, wo.CreatedAt, wo.UpdatedAt, wo.CreatedBy, wo.CommuneId, wo.SegmentId, wo.ClusterId,
            ids, changes, skipped, links?.Select(x => new FaultOutcomeSnapshot(x.FaultId, x.InspectionOutcome, x.ReleasedAt)).ToArray());

    private sealed record FaultChange(string FaultId, FaultStatus From, FaultStatus To);
    private sealed record FaultOutcomeSnapshot(string FaultId, InspectionOutcome? InspectionOutcome, DateTime? ReleasedAt);
    private sealed record WorkOrderSnapshot(string WorkOrderId, string Title, TaskKind TaskKind, WorkOrderStatus WoStatus,
        string? AssignedTo, DateTime? AssignedAt, DateOnly? DueDate, DateOnly? ScheduledDate, string? Note,
        string? ReviewNote, string? ReportNote, DateTime? StartedAt, DateTime? CompletedAt, DateTime? ClosedAt,
        DateTime CreatedAt, DateTime UpdatedAt, string CreatedBy, string CommuneId, string? SegmentId, string? ClusterId,
        string[]? FaultIds, FaultChange[]? FaultChanges, string[]? FaultSkipped, FaultOutcomeSnapshot[]? FaultOutcomes);
}
