using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Admin.Monitoring;

/// <summary>
/// Reads the system admin's monitoring page (BE-35): background jobs, survey processing, disk, database and buckets. Read only.
/// </summary>
/// <remarks>
/// Every "now" here is the DATABASE's, the clock the job runner and the sweep worker judge leases by — an instance whose clock is off
/// must not call a healthy job stalled.
/// </remarks>
public sealed class SystemStatusService(
    LuxMapDbContext db, IEnumerable<IScheduledJob> jobs, ScheduledJobOptions jobOptions, StorageUsage usage, IDiskProbe disk,
    MonitoringOptions options, ILogger<SystemStatusService> log)
{
    public const int RecentFailureCount = 20;

    /// <summary><c>ck_survey_run_state</c>: a run ends <c>succeeded</c> or <c>failed</c>.</summary>
    private const string FailedRun = "failed";

    public async Task<SystemStatusResponse> GetAsync(CancellationToken ct)
    {
        var now = await db.Database.SqlQueryRaw<DateTime>("SELECT now() AS \"Value\"").SingleAsync(ct);
        return new(now, await JobsAsync(now, ct), await SurveyAsync(now, ct), await StorageAsync(ct));
    }

    /// <summary>
    /// The state of one job. A run is overdue only past a poll AND a lease: a job is claimed within a poll of falling due, and a
    /// holder that died is replaced once its lease runs out.
    /// </summary>
    public static JobState Classify(ScheduledJob? row, DateTime now, ScheduledJobOptions jobs)
    {
        if (row is null) return JobState.Stalled;
        if (row.LeaseExpiresAt > now) return JobState.Running;
        if (row.NextRunAt < now - TimeSpan.FromSeconds(jobs.PollSeconds + jobs.LeaseSeconds)) return JobState.Stalled;
        return row.ConsecutiveFailures > 0 ? JobState.Failing : JobState.Ok;
    }

    /// <summary>The jobs this build registers; a row left by a job since removed is not a job.</summary>
    private async Task<IReadOnlyList<JobStatusItem>> JobsAsync(DateTime now, CancellationToken ct)
    {
        var names = jobs.Select(job => job.Name).Order(StringComparer.Ordinal).ToArray();
        var rows = await db.Set<ScheduledJob>().AsNoTracking().Where(job => names.Contains(job.JobName)).ToDictionaryAsync(job => job.JobName, ct);
        return [.. names.Select(name =>
        {
            var row = rows.GetValueOrDefault(name);
            return new JobStatusItem(name, Classify(row, now, jobOptions), row?.NextRunAt, row?.LastStartedAt, row?.LastFinishedAt,
                row?.LastError, row?.LastFailedAt, row?.ConsecutiveFailures ?? 0, row?.RunCount ?? 0);
        })];
    }

    private async Task<SurveyProcessingSummary> SurveyAsync(DateTime now, CancellationToken ct)
    {
        var counts = await db.Set<SurveySweep>().AsNoTracking().GroupBy(sweep => sweep.ProcessingStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, ct);
        var stuck = await db.Set<SurveySweep>().AsNoTracking()
            .CountAsync(sweep => sweep.ProcessingStatus == SweepProcessingStatus.Processing && sweep.ProcessingLeaseExpiresAt < now, ct);
        var failures = await db.Set<SurveyProcessingRun>().AsNoTracking().Where(run => run.ResultState == FailedRun)
            .OrderByDescending(run => run.FinishedAt).ThenByDescending(run => run.RunId).Take(RecentFailureCount)
            .Select(run => new SurveyRunFailure(run.SweepId, run.Attempt, run.ErrorCode!, run.Stage, run.FinishedAt))
            .ToListAsync(ct);

        int Count(SweepProcessingStatus status) => counts.GetValueOrDefault(status);
        return new(new(Count(SweepProcessingStatus.NotStarted), Count(SweepProcessingStatus.Queued), Count(SweepProcessingStatus.Processing),
            Count(SweepProcessingStatus.Succeeded), Count(SweepProcessingStatus.Failed)), stuck, failures);
    }

    private async Task<StorageSummary> StorageAsync(CancellationToken ct)
    {
        var totals = await usage.TotalsAsync(ct);
        var checks = await db.Set<StorageReconciliation>().AsNoTracking().ToDictionaryAsync(row => row.Bucket, ct);
        var buckets = totals.OrderBy(pair => pair.Key).Select(pair => new BucketUsage(pair.Key, pair.Value.Objects, pair.Value.Bytes,
            checks.GetValueOrDefault(pair.Key) is { } c
                ? new BucketReconciliation(c.CheckedAt, c.StoreObjectCount, c.StoreBytes, c.OrphanCount, c.OrphanBytes, c.MissingCount,
                    c.OrphanSample, c.MissingSample)
                : null)).ToList();
        return new(Disk(), await usage.DatabaseBytesAsync(ct), buckets);
    }

    private DiskUsage? Disk()
    {
        try
        {
            var space = disk.Measure();
            return new(space.TotalBytes, space.FreeBytes, space.FreeRatio, space.FreeRatio < options.LowDiskRatio);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            log.LogWarning(error, "Could not measure the disk at {Path}.", options.DiskPath);
            return null;
        }
    }
}
