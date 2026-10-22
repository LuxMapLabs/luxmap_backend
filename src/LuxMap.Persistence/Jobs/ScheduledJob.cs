using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Jobs;

/// <summary>
/// One recurring background job and its lease (BE-26, D-2). System-wide: no commune, not audited — it is bookkeeping about the
/// runner, never a business record. Rows are created by the runner for every registered job (<c>ON CONFLICT DO NOTHING</c>).
/// </summary>
public sealed class ScheduledJob
{
    public required string JobName { get; set; }

    /// <summary>When the job is due next. A job is claimed when this has passed and no live lease holds it.</summary>
    public DateTime NextRunAt { get; set; }

    /// <summary>The holder's fence: only the run that took this token may finish or fail it.</summary>
    public Guid? LeaseToken { get; set; }

    public DateTime? LeaseExpiresAt { get; set; }

    public DateTime? LastStartedAt { get; set; }

    public DateTime? LastFinishedAt { get; set; }

    /// <summary>The last failure, cleared by the next success (BE-35 reads it).</summary>
    public string? LastError { get; set; }

    /// <summary>When the last failure finished — kept after a success, so a job failing now and then still shows (BE-35, D-4).</summary>
    public DateTime? LastFailedAt { get; set; }

    /// <summary>Failures since the last success; a success sets it back to 0 (BE-35, D-4).</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Successful runs only.</summary>
    public long RunCount { get; set; }
}

public sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> builder)
    {
        builder.ToTable("scheduled_job", table =>
        {
            table.HasCheckConstraint("ck_scheduled_job_lease_together", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
            table.HasCheckConstraint("ck_scheduled_job_name", "job_name ~ '^[a-z][a-z0-9_]{1,62}$'");
            table.HasCheckConstraint("ck_scheduled_job_last_error_length", "last_error IS NULL OR char_length(last_error) <= 1000");
            table.HasCheckConstraint("ck_scheduled_job_run_count", "run_count >= 0");
            table.HasCheckConstraint("ck_scheduled_job_consecutive_failures",
                "consecutive_failures >= 0 AND (consecutive_failures = 0 OR last_failed_at IS NOT NULL)");
        });
        builder.HasKey(job => job.JobName);
        builder.Property(job => job.JobName).HasColumnType("text");
        builder.Property(job => job.LastError).HasColumnType("text");
    }
}
