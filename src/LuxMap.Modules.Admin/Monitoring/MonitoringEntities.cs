using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Admin.Monitoring;

/// <summary>
/// The last orphan check of one bucket (BE-35, D-6): one row per bucket, overwritten by each run. System-wide bookkeeping about the
/// store — no commune, not audited, like <c>scheduled_job</c>. Reports only: nothing is ever deleted on the strength of it.
/// </summary>
public sealed class StorageReconciliation
{
    public StorageBucket Bucket { get; set; }

    public DateTime CheckedAt { get; set; }

    /// <summary>What the store listed.</summary>
    public long StoreObjectCount { get; set; }

    public long StoreBytes { get; set; }

    /// <summary>Objects no row names, older than <see cref="MonitoringOptions.OrphanGrace"/>.</summary>
    public long OrphanCount { get; set; }

    public long OrphanBytes { get; set; }

    /// <summary>Keys a row names that the store does not hold — a photo that answers 503 in front of a user.</summary>
    public long MissingCount { get; set; }

    /// <summary>At most <see cref="StorageReconciliationJob.SampleSize"/> keys, to start the repair from.</summary>
    public string[] OrphanSample { get; set; } = [];

    public string[] MissingSample { get; set; } = [];
}

/// <summary>
/// One spell of low disk (BE-35, D-7): opened when free space falls below <see cref="MonitoringOptions.LowDiskRatio"/>, closed when it
/// is back above. At most one is open (<see cref="StorageAlertConfiguration.OneOpen"/>), so the system admins are mailed once per spell
/// however many instances run the check.
/// </summary>
public sealed class StorageAlert
{
    public long AlertId { get; set; }

    public DateTime OpenedAt { get; set; }

    public double FreeRatioAtOpen { get; set; }

    /// <summary>Set when the mail is claimed; put back to null when sending fails, so the next run tries again.</summary>
    public DateTime? NotifiedAt { get; set; }

    public DateTime? ClosedAt { get; set; }
}

public sealed class StorageReconciliationConfiguration : IEntityTypeConfiguration<StorageReconciliation>
{
    public void Configure(EntityTypeBuilder<StorageReconciliation> builder)
    {
        builder.ToTable("storage_reconciliation", table =>
        {
            table.HasCheckConstraint("ck_storage_reconciliation_counts",
                "store_object_count >= 0 AND store_bytes >= 0 AND orphan_count >= 0 AND orphan_bytes >= 0 AND missing_count >= 0"
                + " AND orphan_count <= store_object_count AND orphan_bytes <= store_bytes");
            table.HasCheckConstraint("ck_storage_reconciliation_samples",
                $"cardinality(orphan_sample) <= LEAST(orphan_count, {StorageReconciliationJob.SampleSize})"
                + $" AND cardinality(missing_sample) <= LEAST(missing_count, {StorageReconciliationJob.SampleSize})");
        });
        builder.HasKey(row => row.Bucket);
        builder.HasContractEnum(row => row.Bucket);
    }
}

public sealed class StorageAlertConfiguration : IEntityTypeConfiguration<StorageAlert>
{
    public const string OneOpen = "ux_storage_alert_open";

    public void Configure(EntityTypeBuilder<StorageAlert> builder)
    {
        builder.ToTable("storage_alert", table =>
        {
            table.HasCheckConstraint("ck_storage_alert_ratio",
                "free_ratio_at_open >= 0 AND free_ratio_at_open <= 1 AND free_ratio_at_open <> 'NaN'::float8");
            table.HasCheckConstraint("ck_storage_alert_order",
                "(notified_at IS NULL OR notified_at >= opened_at) AND (closed_at IS NULL OR closed_at >= opened_at)");
        });
        builder.HasKey(alert => alert.AlertId);
        builder.Property(alert => alert.AlertId).UseIdentityAlwaysColumn();

        // NULLS NOT DISTINCT: the open spells all have closed_at NULL, and a plain unique index would let any number of them in.
        builder.HasIndex(alert => alert.ClosedAt).IsUnique().AreNullsDistinct(false).HasFilter("closed_at IS NULL")
            .HasDatabaseName(OneOpen);
    }
}
