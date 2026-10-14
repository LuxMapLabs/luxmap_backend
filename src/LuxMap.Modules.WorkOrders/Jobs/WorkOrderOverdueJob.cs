using System.Linq.Expressions;
using LuxMap.Modules.Notifications;
using LuxMap.Modules.WorkOrders.Configurations;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Modules.WorkOrders.Jobs;

/// <summary>
/// BE-26 (D-3, D-4): a work order whose due date is before TONIGHT — the agenda's night, cut at <c>WorkOrders:Agenda:NightStartsAt</c>
/// (12:00 Asia/Ho_Chi_Minh), not the calendar date — and still open, assigned or in progress, is reported once to its assignee and the
/// commune's Managers. <c>done</c> (awaiting verification) is not late work; a null due date never is.
/// </summary>
/// <remarks>
/// Candidates are found across communes in an unscoped, read-only context; each report is written in a context scoped to the order's
/// commune, re-reading the order there (it may have been finished or rescheduled since). The notice row and the notifications are ONE
/// save: a second instance writing the same (order, due date) hits the primary key and skips — never a second notice.
/// </remarks>
public sealed class WorkOrderOverdueJob(JobContextFactory contexts, WorkOrderAgendaOptions agenda, ILogger<WorkOrderOverdueJob> log)
    : IScheduledJob
{
    public const int Batch = 500;

    private static readonly WorkOrderStatus[] Late = [WorkOrderStatus.Open, WorkOrderStatus.Assigned, WorkOrderStatus.InProgress];

    /// <summary>The ONE definition used by the scan and by the re-check under the commune's scope (the agenda's <c>Overdue</c> flag agrees).</summary>
    public static Expression<Func<WorkOrder, bool>> OverdueOn(DateOnly night)
        => w => w.DueDate != null && w.DueDate < night && Late.Contains(w.WoStatus);

    public string Name => "work_order_overdue";

    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task RunAsync(DateTime now, string correlationId, CancellationToken ct)
    {
        var night = agenda.NightOf(new DateTimeOffset(now, TimeSpan.Zero));
        var reported = 0;
        while (true)
        {
            var due = await CandidatesAsync(night, ct);
            var before = reported;
            foreach (var commune in due.GroupBy(c => c.CommuneId))
            {
                await using var db = contexts.Open(commune.Key);
                foreach (var candidate in commune) reported += await ReportAsync(db, candidate.WorkOrderId, night, now, ct) ? 1 : 0;
            }

            // A full batch where nothing could be reported would come back unchanged: stop rather than loop.
            if (due.Count < Batch || reported == before) break;
        }

        if (reported > 0) log.LogInformation("Reported {Count} overdue work orders ({Correlation}).", reported, correlationId);
    }

    private async Task<List<Candidate>> CandidatesAsync(DateOnly night, CancellationToken ct)
    {
        await using var scan = contexts.Unscoped();
        return await scan.Set<WorkOrder>().IgnoreQueryFilters().AsNoTracking()
            .Where(OverdueOn(night))
            .Where(w => !scan.Set<WorkOrderOverdueNotice>().IgnoreQueryFilters()
                .Any(n => n.WorkOrderId == w.WorkOrderId && n.DueDate == w.DueDate))
            .OrderBy(w => w.DueDate).ThenBy(w => w.WorkOrderId.Length).ThenBy(w => w.WorkOrderId)
            .Select(w => new Candidate(w.WorkOrderId, w.CommuneId))
            .Take(Batch).ToListAsync(ct);
    }

    private static async Task<bool> ReportAsync(
        Persistence.LuxMapDbContext db, string workOrderId, DateOnly night, DateTime now, CancellationToken ct)
    {
        var wo = await db.Set<WorkOrder>().AsNoTracking().Where(w => w.WorkOrderId == workOrderId).Where(OverdueOn(night))
            .SingleOrDefaultAsync(ct);
        if (wo is not { DueDate: { } dueDate }) return false;

        db.Add(new WorkOrderOverdueNotice { WorkOrderId = wo.WorkOrderId, DueDate = dueDate, CommuneId = wo.CommuneId, NotifiedAt = now });
        var recipients = new List<string?> { wo.AssignedTo };
        recipients.AddRange(await Notifier.ManagersCoveringAsync(db, [wo.CommuneId], ct));
        Notifier.Stage(db, WorkOrderNotices.Overdue(wo), recipients, null, now);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: WorkOrderOverdueNoticeConfiguration.PrimaryKey })
        {
            // Another instance reported it first: nothing more to do, and nothing of ours was saved.
            db.ChangeTracker.Clear();
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private sealed record Candidate(string WorkOrderId, string CommuneId);
}
