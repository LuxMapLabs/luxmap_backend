using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Configurations;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Modules.WorkOrders.Jobs;

public sealed class SurveyRecurringGenerateJob(JobContextFactory contexts, WorkOrderAgendaOptions agenda,
    ILogger<SurveyRecurringGenerateJob> log) : IScheduledJob
{
    public string Name => "survey_recurring_generate";
    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task RunAsync(DateTime now, string correlationId, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(now), agenda.Zone()).DateTime;
        await using var scan = contexts.Unscoped();
        var candidates = await scan.Set<SurveyRecurringSchedule>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.IsActive).OrderBy(s => s.CreatedAt).ThenBy(s => s.ScheduleId.Length).ThenBy(s => s.ScheduleId)
            .Select(s => new { s.ScheduleId, s.CommuneId, s.StartsOnWeek, s.DayOfWeek }).ToListAsync(ct);
        var generated = 0;
        foreach (var candidate in candidates)
        foreach (var date in SurveyScheduleCalendar.Dates(local, candidate.StartsOnWeek, candidate.DayOfWeek))
        {
            try
            {
                if (await Generate(candidate.ScheduleId, candidate.CommuneId, date, local, now, correlationId, ct)) generated++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                log.LogError(error, "Recurring survey {Schedule} on {Date} failed ({Correlation}).", candidate.ScheduleId, date, correlationId);
            }
        }
        log.LogInformation("Generated {Count} recurring survey orders ({Correlation}).", generated, correlationId);
    }

    private async Task<bool> Generate(string id, string commune, DateOnly date, DateTime local, DateTime now,
        string correlationId, CancellationToken ct)
    {
        await using var db = contexts.Open(commune);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var schedule = await db.Set<SurveyRecurringSchedule>().AsNoTracking().SingleOrDefaultAsync(x => x.ScheduleId == id, ct);
        if (schedule is null || !schedule.IsActive || !SurveyScheduleCalendar.Dates(local, schedule.StartsOnWeek, schedule.DayOfWeek).Contains(date)) return false;
        if (await db.Set<SurveyRecurringOccurrence>().AnyAsync(x => x.ScheduleId == id && x.ScheduledDate == date, ct)) return false;
        var monday = EngineerLoadBoard.WeekStart(date);
        var sunday = monday.AddDays(6);
        // All survey orders, including manual ones, suppress a second visit to this road in this week.
        if (await db.Set<WorkOrder>().IgnoreQueryFilters().AnyAsync(w => w.TaskKind == TaskKind.Survey && w.WoStatus != WorkOrderStatus.Cancelled
            && w.ScheduledDate >= monday && w.ScheduledDate <= sunday
            && db.Set<WorkOrderSegment>().IgnoreQueryFilters().Any(link => link.WorkOrderId == w.WorkOrderId && link.SegmentId == schedule.SegmentId), ct)) return false;
        var road = await db.Set<RoadSegment>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.SegmentId == schedule.SegmentId, ct);
        var eligible = await WorkOrderAssignees.EligibleUsers(db, commune).AsNoTracking()
            .Select(u => new DispatchEngineer(u.UserId, u.FullName, commune)).ToListAsync(ct);
        var required = await WorkOrderAssignees.SurveyCommunes(db, [road.SegmentId], ct, includeReferencedRoad: true);
        foreach (var requiredCommune in required)
        {
            var ids = await WorkOrderAssignees.EligibleUsers(db, requiredCommune).Select(u => u.UserId).ToArrayAsync(ct);
            eligible.RemoveAll(u => !ids.Contains(u.UserId));
        }
        var assigned = eligible.FirstOrDefault(u => u.UserId == schedule.AssignedTo)?.UserId;
        if (assigned is null)
        {
            var loads = await WorkOrderLoads.Read(db, eligible.Select(u => u.UserId).ToArray(), monday, sunday, ct, acrossCommunes: true);
            assigned = new EngineerLoadBoard(loads, 1).SelectOn(eligible, date)?.UserId;
        }
        if (assigned is null) log.LogWarning("No eligible engineer for recurring survey {Schedule} on {Date}.", id, date);
        var wo = await WorkOrderService.StageSurvey(db, JobContextFactory.AuditFor(db, correlationId), commune,
            [road.SegmentId], $"Khảo sát toàn tuyến - {road.SegmentName}", assigned, date, date,
            schedule.Note ?? $"Tự động sinh từ lịch khảo sát định kỳ {id}", null, schedule.CreatedBy, null, now, ct);
        db.Add(new SurveyRecurringOccurrence { ScheduleId = id, ScheduledDate = date, CommuneId = commune,
            WorkOrderId = wo.WorkOrderId, CreatedAt = now });
        try
        {
            // Atomic receipt + order + targets + audit + Assigned notifications. PK settles concurrent runs.
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SurveyRecurringOccurrenceConfiguration.PrimaryKey })
        {
            await transaction.RollbackAsync(ct);
            return false;
        }
    }
}
