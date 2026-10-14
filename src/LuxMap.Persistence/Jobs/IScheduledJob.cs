namespace LuxMap.Persistence.Jobs;

/// <summary>
/// A recurring background job (BE-26). Registered in DI by the module that owns the work; the runner claims it under a DB lease.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>A job scans STATE, not time slots</b> (D-2): one run handles everything that is due, so a server down for three hours catches up
/// in its first run — there is no backlog of missed slots to replay.
/// </para>
/// <para>
/// 🔴 <b>Idempotency comes from a UNIQUE constraint in the database, not from the lease.</b> A run can outlive its lease, and two instances
/// can both run a job; whatever a job writes must carry a natural key (a work order and its due date; a device and its silence) so the
/// second writer hits the constraint and skips. Never "check, then insert" alone.
/// </para>
/// </remarks>
public interface IScheduledJob
{
    /// <summary>Lowercase snake_case, the key of its <c>scheduled_job</c> row.</summary>
    string Name { get; }

    /// <summary>The wait after a successful run.</summary>
    TimeSpan Interval { get; }

    /// <param name="now">One snapshot of the clock for the whole run, truncated to microseconds.</param>
    /// <param name="correlationId">For the audit events and logs of this run: <c>job:&lt;name&gt;:&lt;lease token&gt;</c>.</param>
    Task RunAsync(DateTime now, string correlationId, CancellationToken ct);
}
