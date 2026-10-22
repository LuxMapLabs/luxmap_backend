namespace LuxMap.Modules.Admin.Monitoring;

/// <summary>Section <c>Monitoring</c> (BE-35): where the free space is measured, when it is low, how old an orphan must be.</summary>
public sealed record MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>
    /// A path on the disk to measure (D-3). The default, the app's own directory, is on the disk that holds Docker's volumes on the
    /// one-disk VPS — Postgres, MinIO, the frame scratch space and the backups all share it.
    /// </summary>
    public string DiskPath { get; init; } = AppContext.BaseDirectory;

    /// <summary>Free space below this share of the disk is low (D-7).</summary>
    public double LowDiskRatio { get; init; } = 0.15;

    /// <summary>
    /// An object with no row is an orphan only once it is this old (D-6): the object is written before its row is committed, so an
    /// upload in flight looks exactly like an orphan.
    /// </summary>
    public int OrphanGraceHours { get; init; } = 24;

    public TimeSpan OrphanGrace => TimeSpan.FromHours(OrphanGraceHours);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DiskPath) || !double.IsFinite(LowDiskRatio) || LowDiskRatio is <= 0 or >= 1
            || OrphanGraceHours is < 1 or > 720)
            throw new InvalidOperationException($"{SectionName} has an out-of-range setting.");
    }
}
