using LuxMap.Shared.Storage;

namespace LuxMap.Modules.Admin.Monitoring;

/// <summary>
/// A background job's health (BE-35, D-1). <c>running</c>: holds a live lease. <c>stalled</c>: due for longer than a poll and a lease
/// and nobody holds it — no worker is taking it. <c>failing</c>: the last run failed. <c>ok</c>: none of these.
/// </summary>
public enum JobState { Ok, Failing, Stalled, Running }

/// <summary><c>GET /admin/system/status</c> (BE-35, D-2): one page for the system admin's monitoring screen.</summary>
public sealed record SystemStatusResponse(
    DateTime GeneratedAt, IReadOnlyList<JobStatusItem> Jobs, SurveyProcessingSummary SurveyProcessing, StorageSummary Storage);

/// <param name="NextRunAt">Null only for a registered job that has no row yet — the runner has not started.</param>
public sealed record JobStatusItem(
    string Name, JobState State, DateTime? NextRunAt, DateTime? LastStartedAt, DateTime? LastFinishedAt, string? LastError,
    DateTime? LastFailedAt, int ConsecutiveFailures, long RunCount);

/// <param name="Stuck">Processing with an expired lease: the worker that took it is gone and nothing has taken it back.</param>
/// <param name="RecentFailures">The newest failed runs, at most <see cref="SystemStatusService.RecentFailureCount"/>.</param>
public sealed record SurveyProcessingSummary(SweepProcessingCounts ByStatus, int Stuck, IReadOnlyList<SurveyRunFailure> RecentFailures);

/// <summary>Sweeps per <c>processing_status</c>; every status appears, at zero when none.</summary>
public sealed record SweepProcessingCounts(int NotStarted, int Queued, int Processing, int Succeeded, int Failed);

public sealed record SurveyRunFailure(string SweepId, int Attempt, string ErrorCode, string Stage, DateTime FinishedAt);

/// <param name="Disk">Null when the disk could not be measured (the path is missing or unreadable).</param>
/// <param name="DatabaseBytes"><c>pg_database_size</c> — tables, indexes and TOAST of this database.</param>
public sealed record StorageSummary(DiskUsage? Disk, long DatabaseBytes, IReadOnlyList<BucketUsage> Buckets);

/// <param name="Low">Free share below <c>Monitoring:LowDiskRatio</c> — the same line the alert mail uses.</param>
public sealed record DiskUsage(long TotalBytes, long FreeBytes, double FreeRatio, bool Low);

/// <param name="ObjectCount">What the ROWS name: originals and thumbnails, counted from the database.</param>
/// <param name="Reconciliation">The last orphan check of this bucket; null before the first one.</param>
public sealed record BucketUsage(StorageBucket Bucket, long ObjectCount, long Bytes, BucketReconciliation? Reconciliation);

public sealed record BucketReconciliation(
    DateTime CheckedAt, long StoreObjectCount, long StoreBytes, long OrphanCount, long OrphanBytes, long MissingCount,
    IReadOnlyList<string> OrphanSample, IReadOnlyList<string> MissingSample);
