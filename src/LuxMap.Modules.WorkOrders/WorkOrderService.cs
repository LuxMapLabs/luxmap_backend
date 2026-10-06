using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications;
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
        PageRequest page, CancellationToken ct, string? caseId = null)
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
        if (caseId is not null) query = query.Where(x => x.WorkOrderId == caseId || x.RootWorkOrderId == caseId);
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
            ParentWorkOrderId = wo.ParentWorkOrderId, CaseId = wo.RootWorkOrderId ?? wo.WorkOrderId,
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
            ParentWorkOrderId = wo.ParentWorkOrderId, CaseId = wo.RootWorkOrderId ?? wo.WorkOrderId,
            SegmentIds = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == id).OrderBy(x => x.Position).Select(x => x.SegmentId).ToArrayAsync(ct),
            FaultIds = members.Select(x => x.Fault.FaultId).ToArray(),
            PriorityScore = members.Select(x => x.Fault.PriorityScore).DefaultIfEmpty().Max(),
            Note = wo.Note, ReviewNote = wo.ReviewNote, ReportNote = wo.ReportNote, CreatedBy = wo.CreatedBy,
            MaterialsNote = wo.MaterialsNote, MaterialsUsed = wo.MaterialsUsed,
            AssignedAt = wo.AssignedAt, StartedAt = wo.StartedAt, CompletedAt = wo.CompletedAt, ClosedAt = wo.ClosedAt,
            AssigneeEligible = wo.AssignedTo is null ? null : await EligibleUsers(wo.CommuneId).AnyAsync(x => x.UserId == wo.AssignedTo, ct),
            AllowedActions = WorkOrderRules.AllowedActions(wo.WoStatus, actor.Role, wo.AssignedTo == actor.UserId, wo.TaskKind),
            Faults = members.Select(x => new WorkOrderFaultDetail(x.Fault.FaultId, x.Fault.PoleId, x.Fault.SegmentId,
                new(x.Lat, x.Lng), x.Fault.FaultType, x.Fault.FaultStatus, x.Fault.Severity, x.InspectionOutcome)).ToArray(),
        };
    }

    public async Task<WorkOrderDetail> Create(CreateWorkOrderRequest request, CancellationToken ct)
    {
        if (new[] { request.WorkOrderId, request.WoStatus, request.ClusterId, request.PriorityScore }.Any(OptionalJson.Present))
            throw Error("SERVER_OWNED_FIELD", HttpStatusCode.BadRequest);
        var title = ValidTitle(request.Title);
        Schedule(request.ScheduledDate, request.DueDate);
        var kind = request.TaskKind ?? throw OptionalJson.Invalid("task_kind");
        if (kind == TaskKind.Survey) return await CreateSurvey(request, title, ct);
        if (OptionalJson.Present(request.CommuneId)) throw Error("SERVER_OWNED_FIELD", HttpStatusCode.BadRequest);
        if (request.SegmentIds is not null) throw OptionalJson.Invalid("segment_ids");
        var ids = request.FaultIds ?? [];
        if (ids.Length > 200 || ids.Distinct().Count() != ids.Length || ids.Any(string.IsNullOrWhiteSpace)
            || (ids.Length > 0 && request.SegmentId is not null)
            || (ids.Length == 0 && (kind != TaskKind.Inspection || string.IsNullOrWhiteSpace(request.SegmentId))))
            throw OptionalJson.Invalid("fault_ids / segment_id");
        var faults = await db.Set<Fault>().Where(x => ids.Contains(x.FaultId))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).ToListAsync(ct);
        var missing = ids.Except(faults.Select(x => x.FaultId)).ToArray();
        if (missing.Length != 0) throw Error("FAULT_NOT_FOUND", HttpStatusCode.NotFound, ("fault_ids", missing));
        var road = faults.Count > 0 ? (RoadSegment?)null
            : await db.Set<RoadSegment>().FirstOrDefaultAsync(x => x.SegmentId == request.SegmentId, ct)
                ?? throw Error("ASSET_NOT_FOUND", HttpStatusCode.NotFound);
        return await Insert(new(kind, title, faults, road, request.AssignedTo, request.DueDate, request.ScheduledDate,
            request.Note, Materials(request.MaterialsNote), null), ct);
    }

    // SELF-SIGNED BE-15 P2a: uses the existing commune_id as the explicit anchor.
    private async Task<WorkOrderDetail> CreateSurvey(CreateWorkOrderRequest request, string title, CancellationToken ct)
    {
        var ids = request.SegmentIds ?? [];
        var commune = OptionalJson.Text(request.CommuneId, "commune_id", false);
        if (string.IsNullOrWhiteSpace(commune) || ids.Length is 0 or > 200 || ids.Any(string.IsNullOrWhiteSpace)
            || ids.Distinct().Count() != ids.Length || request.SegmentId is not null || request.FaultIds is { Length: > 0 })
            throw OptionalJson.Invalid("segment_ids / commune_id / fault_ids");
        if (!db.CurrentCommuneScope.Allows(commune)
            || !await db.Set<AdministrativeUnit>().AnyAsync(x => x.CommuneId == commune, ct)
            || await db.Set<RoadSegment>().CountAsync(x => ids.Contains(x.SegmentId), ct) != ids.Length)
            throw Error("ASSET_NOT_FOUND", HttpStatusCode.NotFound);
        if (request.AssignedTo is not null)
        {
            await RequireAssignee(request.AssignedTo, commune, ct);
            await RequireSurveyAssignee(request.AssignedTo, ids, ct);
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // SQL expression comes only from the immutable server-side ID specification.
        var idSql = $"SELECT {PrefixedIds.WorkOrder.DefaultValueSql} AS \"Value\"";
        var id = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);
        var now = UtcMicrosecondClock.UtcNow();
        var wo = new WorkOrder { WorkOrderId = id, CommuneId = commune, TaskKind = TaskKind.Survey,
            Title = title, CreatedBy = ActorId, AssignedTo = request.AssignedTo,
            AssignedAt = request.AssignedTo is null ? null : now,
            WoStatus = request.AssignedTo is null ? WorkOrderStatus.Open : WorkOrderStatus.Assigned,
            DueDate = request.DueDate, ScheduledDate = request.ScheduledDate, Note = request.Note,
            MaterialsNote = Materials(request.MaterialsNote), CreatedAt = now, UpdatedAt = now };
        db.Add(wo);
        for (var position = 0; position < ids.Length; position++)
            db.Add(new WorkOrderSegment { WorkOrderId = id, CommuneId = commune, Position = position, SegmentId = ids[position] });
        Record(wo, AuditAction.Created, null, new { work_order = Snapshot(wo, []), segment_ids = ids }, now);
        Notify(WorkOrderNotices.Assigned(wo), [wo.AssignedTo], now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await Detail(id, ct);
    }

    private async Task RequireSurveyAssignee(string assigned, string[] ids, CancellationToken ct)
    {
        var roads = await db.Set<RoadSegment>().Where(x => ids.Contains(x.SegmentId)).Select(x => x.CommuneId).ToArrayAsync(ct);
        if (roads.Length != ids.Length) throw Error("ASSET_NOT_FOUND", HttpStatusCode.NotFound);
        // Q7: the eligible pole set is restricted to the assigning manager's scope.
        var communes = roads.Concat(await db.Set<Pole>().Where(x => ids.Contains(x.SegmentId)).Select(x => x.CommuneId).Distinct().ToArrayAsync(ct)).Distinct();
        foreach (var commune in communes) await RequireAssignee(assigned, commune, ct);
    }

    /// <summary>
    /// Creates the next step of a chain from a verified work order (drift FR-2). The server carries the
    /// target over — the faults the inspection found present — so the Manager only picks who and when.
    /// </summary>
    public async Task<WorkOrderDetail> FollowUp(string id, FollowUpWorkOrderRequest request, CancellationToken ct)
    {
        var kind = request.TaskKind ?? throw OptionalJson.Invalid("task_kind");
        Schedule(request.ScheduledDate, request.DueDate);
        var parent = await Find(id, ct);
        RequireAction(parent, "follow_up");
        if (!WorkOrderRules.FollowUpKinds(parent.TaskKind).Contains(kind))
            throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict, ("work_order_id", id),
                ("task_kind", parent.TaskKind), ("allowed_follow_up_kinds", WorkOrderRules.FollowUpKinds(parent.TaskKind)));
        var present = await db.Set<WorkOrderFault>()
            .Where(x => x.WorkOrderId == id && x.InspectionOutcome == InspectionOutcome.FaultPresent)
            .Select(x => x.FaultId).ToListAsync(ct);
        if (present.Count == 0) throw Error("NOTHING_TO_FOLLOW_UP", HttpStatusCode.Conflict, ("work_order_id", id));
        var ids = request.FaultIds ?? present.ToArray();
        if (ids.Length == 0 || ids.Distinct().Count() != ids.Length || ids.Except(present).Any())
            throw Error("VALIDATION_FAILED", HttpStatusCode.BadRequest, ("field", "fault_ids"), ("carried_fault_ids", present.Order().ToArray()));
        var faults = await db.Set<Fault>().Where(x => ids.Contains(x.FaultId))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).ToListAsync(ct);
        var title = request.Title is null ? parent.Title : ValidTitle(request.Title);
        return await Insert(new(kind, title, faults, null, request.AssignedTo, request.DueDate, request.ScheduledDate,
            request.Note, Materials(request.MaterialsNote), parent), ct);
    }

    private sealed record NewWorkOrder(TaskKind Kind, string Title, List<Fault> Faults, RoadSegment? Road,
        string? AssignedTo, DateOnly? DueDate, DateOnly? ScheduledDate, string? Note, string? MaterialsNote, WorkOrder? Parent);

    private async Task<WorkOrderDetail> Insert(NewWorkOrder order, CancellationToken ct)
    {
        var ids = order.Faults.Select(x => x.FaultId).ToArray();
        // Lock, then RE-READ: the statuses checked below must be the ones a concurrent fault review
        // (BE-19) cannot change before this work order commits (FaultLocks).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await FaultLocks.LockAsync(db, ids, ct);
        var faults = await db.Set<Fault>().AsNoTracking().Where(x => ids.Contains(x.FaultId))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).ToListAsync(ct);
        string commune;
        string? segment;
        if (faults.Count > 0)
        {
            if (faults.Select(x => x.CommuneId).Distinct().Count() != 1)
                throw Error("CROSS_COMMUNE_REFERENCE", HttpStatusCode.Conflict);
            commune = faults[0].CommuneId;
            segment = faults.Where(x => x.SegmentId is not null).OrderByDescending(x => x.PriorityScore)
                .ThenBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).FirstOrDefault()?.SegmentId;
            var invalid = faults.Where(x => !WorkOrderRules.Eligible(order.Kind, x.FaultStatus)).ToArray();
            if (invalid.Length > 0) throw Error("FAULT_STATUS_NOT_ELIGIBLE", HttpStatusCode.Conflict,
                ("task_kind", order.Kind), ("accepted_statuses", FaultStatusSets.Open.Where(x => WorkOrderRules.Eligible(order.Kind, x)).ToArray()),
                ("faults", invalid.Select(x => new { x.FaultId, x.FaultStatus }).ToArray()));
            var conflict = await db.Set<WorkOrderFault>().FirstOrDefaultAsync(x => ids.Contains(x.FaultId) && x.ReleasedAt == null, ct);
            if (conflict is not null) throw LinkConflict(conflict);
        }
        else
        {
            commune = order.Road!.CommuneId;
            segment = order.Road.SegmentId;
        }
        if (order.AssignedTo is not null) await RequireAssignee(order.AssignedTo, commune, ct);
        var now = UtcMicrosecondClock.UtcNow();
        var clusters = faults.Select(x => x.ClusterId).Where(x => x is not null).Distinct().ToArray();
        // SQL is constructed exclusively from the server's immutable prefixed-ID specification.
        var idSql = $"SELECT {PrefixedIds.WorkOrder.DefaultValueSql} AS \"Value\"";
        var id = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);
        var wo = new WorkOrder
        {
            WorkOrderId = id, CommuneId = commune, TaskKind = order.Kind, Title = order.Title, SegmentId = segment,
            ClusterId = clusters.Length == 1 ? clusters[0] : null, CreatedBy = ActorId,
            AssignedTo = order.AssignedTo, AssignedAt = order.AssignedTo is null ? null : now,
            WoStatus = order.AssignedTo is null ? WorkOrderStatus.Open : WorkOrderStatus.Assigned,
            DueDate = order.DueDate, ScheduledDate = order.ScheduledDate, Note = order.Note,
            MaterialsNote = order.MaterialsNote, CreatedAt = now, UpdatedAt = now,
            ParentWorkOrderId = order.Parent?.WorkOrderId,
            RootWorkOrderId = order.Parent is null ? null : order.Parent.RootWorkOrderId ?? order.Parent.WorkOrderId,
        };
        db.Add(wo);
        foreach (var fault in faults) db.Add(new WorkOrderFault
        {
            WorkOrderId = id, FaultId = fault.FaultId, CommuneId = commune, LinkedAt = now,
        });
        Record(wo, AuditAction.Created, null, Snapshot(wo, ids), now, order.Note);
        Notify(WorkOrderNotices.Assigned(wo), [wo.AssignedTo], now);
        await Save(id, ids, ct);
        await transaction.CommitAsync(ct);
        return await Detail(id, ct);
    }

    public async Task<WorkOrderDetail> Patch(string id, PatchWorkOrderRequest request, CancellationToken ct)
    {
        if (OptionalJson.Present(request.CommuneId)) throw Error("SERVER_OWNED_FIELD", HttpStatusCode.BadRequest);
        if (new[] { request.WoStatus, request.AssignedTo, request.FaultIds, request.TaskKind, request.SegmentId, request.SegmentIds }.Any(OptionalJson.Present))
            throw Error("VALIDATION_FAILED", HttpStatusCode.BadRequest, ("endpoint", "Use /assignee or a status action; targets and task_kind are immutable."));
        if (!new[] { request.Title, request.DueDate, request.ScheduledDate, request.MaterialsNote }.Any(OptionalJson.Present)) throw OptionalJson.Invalid("body");
        var title = OptionalJson.Present(request.Title) ? ValidTitle(OptionalJson.Text(request.Title, "title", false)) : null;
        var due = OptionalJson.Date(request.DueDate, "due_date");
        var scheduled = OptionalJson.Date(request.ScheduledDate, "scheduled_date");
        var materials = Materials(OptionalJson.Text(request.MaterialsNote, "materials_note"));
        var wo = await Find(id, ct);
        RequireAction(wo, "edit");
        var before = Snapshot(wo);
        var nextDue = OptionalJson.Present(request.DueDate) ? due : wo.DueDate;
        var nextScheduled = OptionalJson.Present(request.ScheduledDate) ? scheduled : wo.ScheduledDate;
        var nextMaterials = OptionalJson.Present(request.MaterialsNote) ? materials : wo.MaterialsNote;
        Schedule(nextScheduled, nextDue);
        if ((title ?? wo.Title) == wo.Title && nextDue == wo.DueDate && nextScheduled == wo.ScheduledDate
            && nextMaterials == wo.MaterialsNote) return await Detail(id, ct);
        var rescheduled = nextDue != wo.DueDate || nextScheduled != wo.ScheduledDate;
        wo.Title = title ?? wo.Title;
        wo.DueDate = nextDue;
        wo.ScheduledDate = nextScheduled;
        wo.MaterialsNote = nextMaterials;
        var now = UtcMicrosecondClock.UtcNow();
        wo.UpdatedAt = now;
        Record(wo, AuditAction.DetailsChanged, before, Snapshot(wo), now);
        if (rescheduled) Notify(WorkOrderNotices.Rescheduled(wo), [wo.AssignedTo], now);
        await Save(id, [], ct);
        return await Detail(id, ct);
    }

    public async Task<WorkOrderDetail> Assign(string id, AssignWorkOrderRequest request, CancellationToken ct)
    {
        if (!OptionalJson.Present(request.AssignedTo)) throw OptionalJson.Invalid("assigned_to");
        var assigned = OptionalJson.Text(request.AssignedTo, "assigned_to");
        var wo = await Find(id, ct);
        RequireAction(wo, assigned is null ? "unassign" : "assign");
        if (assigned is not null)
        {
            await RequireAssignee(assigned, wo.CommuneId, ct);
            if (wo.TaskKind == TaskKind.Survey)
            {
                var segments = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == id).Select(x => x.SegmentId).ToArrayAsync(ct);
                await RequireSurveyAssignee(assigned, segments, ct);
            }
        }
        if (wo.AssignedTo == assigned) return await Detail(id, ct);
        var before = Snapshot(wo);
        var action = assigned is null ? AuditAction.Unassigned : wo.AssignedTo is null ? AuditAction.Assigned : AuditAction.Reassigned;
        var now = UtcMicrosecondClock.UtcNow();
        var previous = wo.AssignedTo;
        wo.AssignedTo = assigned;
        wo.AssignedAt = assigned is null ? null : now;
        wo.WoStatus = assigned is null ? WorkOrderStatus.Open : WorkOrderStatus.Assigned;
        wo.StartedAt = null;
        wo.UpdatedAt = now;
        Record(wo, action, before, Snapshot(wo), now);
        Notify(WorkOrderNotices.Assigned(wo), [assigned], now);
        Notify(WorkOrderNotices.Unassigned(wo), [previous], now);
        await Save(id, [], ct);
        return await Detail(id, ct);
    }

    public async Task<WorkOrderDetail> Act(string id, string action, string? note,
        JsonElement outcomes, CancellationToken ct, string? materialsUsed = null, DateTime? performedAt = null)
    {
        note = note?.Trim();
        if ((action == "complete" && (note?.Length ?? 0) < 10)
            || ((action is "return" or "cancel") && string.IsNullOrEmpty(note))) throw OptionalJson.Invalid("note");
        var claimed = Performed(performedAt);
        var wo = await Find(id, ct);
        RequireAction(wo, action);
        // BE-43 D-6: the engineer's own clock may not put the step before the step it follows.
        if (claimed is { } at && at < (action == "start" ? wo.AssignedAt : wo.StartedAt)) throw OptionalJson.Invalid("performed_at");
        // BE-24: a repair is not finished until there is a photo of the lamp AFTER the repair — the
        // manager verifies against it. Inspections fix nothing, so they need no photo.
        if (action == "complete" && wo.TaskKind == TaskKind.Repair
            && !await db.Set<RepairEvidence>().AnyAsync(x => x.WorkOrderId == id && x.Kind == EvidenceKind.After, ct))
            throw Error("AFTER_EVIDENCE_REQUIRED", HttpStatusCode.Conflict);
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
        var now = UtcMicrosecondClock.UtcNow();
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
        // Drift FR-2a (C): verifying an inspection IS the Manager confirming what the engineer saw.
        // Only fault_present moves; fault_absent and inconclusive stay for a separate decision (BE-19).
        if (wo.TaskKind == TaskKind.Inspection && action == "verify")
        {
            var present = links.Where(x => x.InspectionOutcome == InspectionOutcome.FaultPresent).Select(x => x.FaultId).ToArray();
            var faults = await db.Set<Fault>().Where(x => present.Contains(x.FaultId)).ToListAsync(ct);
            foreach (var fault in faults)
            {
                if (fault.FaultStatus != FaultStatus.Detected) { skipped.Add(fault.FaultId); continue; }
                transitions.Apply(fault, FaultStatus.Confirmed, now, ActorId);
                changes.Add(new(fault.FaultId, FaultStatus.Detected, FaultStatus.Confirmed));
            }
        }
        switch (action)
        {
            case "start": wo.WoStatus = WorkOrderStatus.InProgress; wo.StartedAt = claimed ?? now; break;
            case "complete": wo.WoStatus = WorkOrderStatus.Done; wo.CompletedAt = claimed ?? now; wo.ReportNote = note;
                wo.MaterialsUsed = Materials(materialsUsed); break;
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
        switch (action)
        {
            case "complete": Notify(WorkOrderNotices.Completed(wo), await Notifier.ManagersCoveringAsync(db, [wo.CommuneId], ct), now); break;
            case "return": Notify(WorkOrderNotices.Returned(wo, note), [wo.AssignedTo], now); break;
            case "verify": Notify(WorkOrderNotices.Verified(wo), [wo.AssignedTo], now); break;
            case "cancel": Notify(WorkOrderNotices.Cancelled(wo, note), [wo.AssignedTo], now); break;
        }
        await Save(id, [], ct);
        return await Detail(id, ct);
    }

    /// <summary>
    /// BE-43 D-6 — when the engineer actually started or finished, for a step queued offline and sent later.
    /// Same rule as a fault's <c>detected_at</c> (drift R-4): microseconds, and no more than five minutes ahead.
    /// </summary>
    /// <remarks>
    /// Only <c>started_at</c> / <c>completed_at</c> take it. The fault transitions, the audit time and
    /// <c>updated_at</c> stay the moment the server received the step: that is when the system learnt of it.
    /// </remarks>
    private static DateTime? Performed(DateTime? claimed)
    {
        if (claimed is not { } value) return null;
        var utc = UtcNormalization.ToUtc(value);
        var at = new DateTime(utc.Ticks / 10 * 10, DateTimeKind.Utc);
        return at > UtcMicrosecondClock.UtcNow() + FaultReportService.FutureTolerance ? throw OptionalJson.Invalid("performed_at") : at;
    }

    private void RequireAction(WorkOrder wo, string action)
    {
        if (!WorkOrderRules.Allows(wo.WoStatus, action)) throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict,
            ("work_order_id", wo.WorkOrderId), ("wo_status", wo.WoStatus), ("action", action),
            ("allowed_actions", WorkOrderRules.AllowedActions(wo.WoStatus, actor.Role, wo.AssignedTo == actor.UserId, wo.TaskKind)));
    }

    private static string ValidTitle(string? value)
    {
        value = value?.Trim();
        return value is { Length: > 0 and <= 200 } ? value : throw OptionalJson.Invalid("title");
    }

    /// <summary>Free text; blank is stored as NULL, so "nothing written" has one spelling (FR-3).</summary>
    private static string? Materials(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
            // A failed statement aborts an open transaction; roll it back before the lookup below.
            if (db.Database.CurrentTransaction is { } open) await open.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            // Reapply BOTH filters: reassignment during a race can make the WO invisible now.
            var current = await Find(id, ct);
            throw Error("CONCURRENT_MODIFICATION", HttpStatusCode.Conflict, ("wo_status", current.WoStatus));
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "ux_work_order_fault_fault_id_active" })
        {
            if (db.Database.CurrentTransaction is { } open) await open.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var link = await db.Set<WorkOrderFault>().FirstAsync(x => faultIds.Contains(x.FaultId) && x.ReleasedAt == null, ct);
            throw LinkConflict(link);
        }
    }

    /// <summary>BE-27 — staged with the change, in the same save; the actor is never told about their own action.</summary>
    private void Notify(NotificationMessage message, IEnumerable<string?> recipients, DateTime now)
        => Notifier.Stage(db, message, recipients, ActorId, now);

    private void Record(WorkOrder wo, AuditAction action, object? before, object after, DateTime now, string? note = null)
        => audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, wo.CommuneId,
            AuditEntityType.WorkOrder, wo.WorkOrderId, action, before, after, note));

    // Only explicit snapshots cross the audit boundary: never serialize an EF entity or AppUser.
    private static WorkOrderSnapshot Snapshot(WorkOrder wo, string[]? ids = null, FaultChange[]? changes = null,
        string[]? skipped = null, List<WorkOrderFault>? links = null)
        => new(wo.WorkOrderId, wo.Title, wo.TaskKind, wo.WoStatus, wo.AssignedTo, wo.AssignedAt,
            wo.DueDate, wo.ScheduledDate, wo.Note, wo.ReviewNote, wo.ReportNote, wo.MaterialsNote, wo.MaterialsUsed,
            wo.ParentWorkOrderId, wo.RootWorkOrderId,
            wo.StartedAt, wo.CompletedAt,
            wo.ClosedAt, wo.CreatedAt, wo.UpdatedAt, wo.CreatedBy, wo.CommuneId, wo.SegmentId, wo.ClusterId,
            ids, changes, skipped, links?.Select(x => new FaultOutcomeSnapshot(x.FaultId, x.InspectionOutcome, x.ReleasedAt)).ToArray());

    private sealed record FaultChange(string FaultId, FaultStatus From, FaultStatus To);
    private sealed record FaultOutcomeSnapshot(string FaultId, InspectionOutcome? InspectionOutcome, DateTime? ReleasedAt);
    private sealed record WorkOrderSnapshot(string WorkOrderId, string Title, TaskKind TaskKind, WorkOrderStatus WoStatus,
        string? AssignedTo, DateTime? AssignedAt, DateOnly? DueDate, DateOnly? ScheduledDate, string? Note,
        string? ReviewNote, string? ReportNote, string? MaterialsNote, string? MaterialsUsed,
        string? ParentWorkOrderId, string? RootWorkOrderId, DateTime? StartedAt, DateTime? CompletedAt, DateTime? ClosedAt,
        DateTime CreatedAt, DateTime UpdatedAt, string CreatedBy, string CommuneId, string? SegmentId, string? ClusterId,
        string[]? FaultIds, FaultChange[]? FaultChanges, string[]? FaultSkipped, FaultOutcomeSnapshot[]? FaultOutcomes);
}
