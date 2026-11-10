using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Configurations;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.WorkOrders;

public sealed class SurveyRecurringService(LuxMapDbContext db, ICurrentActorAccessor actor, IAuditTrail audit,
    TimeProvider clock, WorkOrderAgendaOptions agenda)
{
    private string ActorId => actor.UserId ?? throw Error("UNAUTHENTICATED", HttpStatusCode.Unauthorized);
    private static LuxMapException Error(string code, HttpStatusCode status) => new(code, status, code.Replace('_', ' '));
    private DateTime Now => UtcMicrosecondClock.UtcNow(clock);
    private DateOnly Starts => SurveyScheduleCalendar.StartsOnWeek(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), agenda.Zone()).DateTime);

    private async Task<SurveyRecurringSchedule> Find(string id, CancellationToken ct)
        => await db.Set<SurveyRecurringSchedule>().SingleOrDefaultAsync(x => x.ScheduleId == id, ct)
            ?? throw Error(ErrorCodes.SurveyScheduleNotFound, HttpStatusCode.NotFound);

    public async Task<PagedResult<SurveyRecurringItem>> List(string? commune, PageRequest page, CancellationToken ct)
    {
        var scope = CommuneFilter.Narrow(db.CurrentCommuneScope, commune is null ? null : [commune]);
        var query = db.Set<SurveyRecurringSchedule>().AsNoTracking();
        if (scope is not null) query = query.Where(x => scope.Contains(x.CommuneId));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.ScheduleId.Length).ThenBy(x => x.ScheduleId)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return PagedResult<SurveyRecurringItem>.From(page, total, await Items(rows, ct));
    }

    private async Task<SurveyRecurringItem> Item(SurveyRecurringSchedule row, CancellationToken ct)
        => (await Items([row], ct)).Single();

    private async Task<SurveyRecurringItem[]> Items(IReadOnlyList<SurveyRecurringSchedule> rows, CancellationToken ct)
    {
        var segments = rows.Select(x => x.SegmentId).Distinct().ToArray();
        var assignees = rows.Select(x => x.AssignedTo).OfType<string>().Distinct().ToArray();
        var roads = await db.Set<RoadSegment>().Where(x => segments.Contains(x.SegmentId))
            .ToDictionaryAsync(x => x.SegmentId, x => x.SegmentName, ct);
        var people = await db.Set<AppUser>().Where(x => assignees.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, x => x.FullName, ct);
        return rows.Select(row => new SurveyRecurringItem(row.ScheduleId, row.CommuneId, row.SegmentId,
            roads.GetValueOrDefault(row.SegmentId), row.DayOfWeek, row.AssignedTo,
            row.AssignedTo is null ? null : people.GetValueOrDefault(row.AssignedTo),
            row.IsActive, row.Note, row.StartsOnWeek, SurveyScheduleCalendar.FirstOccurrence(row.StartsOnWeek, row.DayOfWeek),
            row.CreatedAt, row.CreatedBy, row.UpdatedAt)).ToArray();
    }

    private async Task Assignee(string? assigned, string commune, string segment, CancellationToken ct)
    {
        if (assigned is null) return;
        await WorkOrderAssignees.RequireAssignee(db, assigned, commune, ct);
        await WorkOrderAssignees.RequireSurveyAssignee(db, assigned, [segment], ct);
    }

    private static string? Note(string? value)
        => value?.Length > 1000 ? throw OptionalJson.Invalid("note") : value;

    public async Task<SurveyRecurringItem> Create(CreateSurveyRecurringRequest request, CancellationToken ct)
    {
        if (new[] { request.ScheduleId, request.StartsOnWeek, request.IsActive, request.CreatedBy, request.FirstOccurrenceDate }.Any(OptionalJson.Present))
            throw Error(ErrorCodes.ServerOwnedField, HttpStatusCode.BadRequest);
        if (request.DayOfWeek.ValueKind != JsonValueKind.Number || !request.DayOfWeek.TryGetInt16(out var day) || day is < 1 or > 7)
            throw OptionalJson.Invalid("day_of_week");
        if (string.IsNullOrWhiteSpace(request.CommuneId)) throw OptionalJson.Invalid("commune_id");
        if (string.IsNullOrWhiteSpace(request.SegmentId)) throw OptionalJson.Invalid("segment_id");
        CommuneFilter.Narrow(db.CurrentCommuneScope, [request.CommuneId]);
        var road = await db.Set<RoadSegment>().SingleOrDefaultAsync(x => x.SegmentId == request.SegmentId, ct)
            ?? throw Error(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound);
        if (road.CommuneId != request.CommuneId && road.RoadClass != RoadClass.InterCommune)
            throw Error(ErrorCodes.CrossCommuneReference, HttpStatusCode.Conflict);
        await Assignee(request.AssignedTo, request.CommuneId, road.SegmentId, ct);
        var now = Now;
        var row = new SurveyRecurringSchedule { CommuneId = request.CommuneId, SegmentId = road.SegmentId,
            DayOfWeek = day, AssignedTo = request.AssignedTo, Note = Note(request.Note), IsActive = true,
            StartsOnWeek = Starts, CreatedBy = ActorId, CreatedAt = now, UpdatedAt = now };
        // Allocate before staging the audit, as generated display IDs are part of the audit identity.
        var idSql = $"SELECT {Shared.Contracts.PrefixedIds.SurveyRecurringSchedule.DefaultValueSql} AS \"Value\"";
        row.ScheduleId = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);
        db.Add(row);
        Record(row, AuditAction.Created, null, State(row), now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SurveyRecurringScheduleConfiguration.UniqueSegmentDay })
        {
            db.ChangeTracker.Clear();
            throw Error(ErrorCodes.SurveyScheduleExists, HttpStatusCode.Conflict);
        }
        return await Item(row, ct);
    }

    public async Task<SurveyRecurringItem> Patch(string id, PatchSurveyRecurringRequest request, CancellationToken ct)
    {
        if (new[] { request.DayOfWeek, request.SegmentId, request.CommuneId, request.ScheduleId, request.StartsOnWeek,
            request.CreatedBy, request.FirstOccurrenceDate }.Any(OptionalJson.Present))
            throw Error(ErrorCodes.ServerOwnedField, HttpStatusCode.BadRequest);
        if (!new[] { request.IsActive, request.AssignedTo, request.Note }.Any(OptionalJson.Present))
            throw OptionalJson.Invalid("body");
        var row = await Find(id, ct);
        var before = State(row);
        if (OptionalJson.Present(request.IsActive))
        {
            if (request.IsActive.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw OptionalJson.Invalid("is_active");
            var active = request.IsActive.GetBoolean();
            if (active && !row.IsActive) row.StartsOnWeek = Starts;
            row.IsActive = active;
        }
        if (OptionalJson.Present(request.AssignedTo))
        {
            var assigned = OptionalJson.Text(request.AssignedTo, "assigned_to");
            await Assignee(assigned, row.CommuneId, row.SegmentId, ct);
            row.AssignedTo = assigned;
        }
        if (OptionalJson.Present(request.Note)) row.Note = Note(OptionalJson.Text(request.Note, "note"));
        row.UpdatedAt = Now;
        Record(row, AuditAction.DetailsChanged, before, State(row), row.UpdatedAt);
        await db.SaveChangesAsync(ct);
        return await Item(row, ct);
    }

    public async Task Delete(string id, CancellationToken ct)
    {
        var row = await Find(id, ct);
        Record(row, AuditAction.Deleted, State(row), null, Now);
        db.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private void Record(SurveyRecurringSchedule row, AuditAction action, object? before, object? after, DateTime now)
        => audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, row.CommuneId,
            AuditEntityType.SurveyRecurringSchedule, row.ScheduleId, action, before, after));

    private static object State(SurveyRecurringSchedule row) => new { row.ScheduleId, row.CommuneId, row.SegmentId,
        row.DayOfWeek, row.AssignedTo, row.IsActive, row.StartsOnWeek, row.Note, row.CreatedBy, row.CreatedAt, row.UpdatedAt };
}
