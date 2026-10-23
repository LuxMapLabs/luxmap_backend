using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Versions;

/// <summary>
/// One version of something that produces results: an algorithm, a model, a frame extractor, a firmware (ERD Q8 — one registry,
/// not one table per kind). Immutable: a result cites a row, so the row may never change under it.
/// </summary>
/// <remarks>
/// Lives in Persistence since BE-34 (D-2) so that every module can point at it — Survey for its runs, Telemetry for a device's
/// firmware, Admin for the registry API — without one module referencing another. The table itself did not move.
/// </remarks>
public sealed class ArtifactVersion : IImmutableRecord
{
    public long VersionId { get; set; }

    public required string Component { get; set; }

    public required string Version { get; set; }

    /// <summary>
    /// SHA-256 of the artifact, lowercase hex. Null only for a firmware (BE-34 D-3): a device states its version string and sends no
    /// hash; the system admin adds one by registering the firmware ahead with its binary's hash.
    /// </summary>
    public string? ArtifactHash { get; set; }

    public string Metadata { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }

    /// <summary>The system admin who registered it (<c>POST /admin/artifact-versions</c>); null for every row the server writes itself.</summary>
    public string? CreatedBy { get; set; }
}

public static class ArtifactComponents
{
    public const string ClockAlgorithm = "clock_algorithm";
    public const string AssociationAlgorithm = "association_algorithm";
    public const string ClassificationAlgorithm = "classification_algorithm";
    public const string CvModel = "cv_model";
    public const string FrameExtractor = "frame_extractor";

    /// <summary>The firmware of an IoT device in a cabinet, as its heartbeat states it (BE-34 D-4).</summary>
    public const string NodeFirmware = "node_firmware";

    /// <summary>The firmware of the BH1750 light-sensing module on the survey vehicle (BE-34).</summary>
    public const string LuxModuleFirmware = "lux_module_firmware";

    public static readonly IReadOnlyList<string> All =
        [ClockAlgorithm, AssociationAlgorithm, ClassificationAlgorithm, CvModel, FrameExtractor, NodeFirmware, LuxModuleFirmware];

    /// <summary>The components a person may register; everything else is written by the server from the code it runs.</summary>
    public static readonly IReadOnlyList<string> Firmware = [NodeFirmware, LuxModuleFirmware];
}

public sealed class ArtifactVersionConfiguration : IEntityTypeConfiguration<ArtifactVersion>
{
    public void Configure(EntityTypeBuilder<ArtifactVersion> b)
    {
        b.ToTable("artifact_version", t =>
        {
            t.HasCheckConstraint("ck_artifact_version_component",
                $"component IN ({string.Join(",", ArtifactComponents.All.Select(c => $"'{c}'"))})");
            // `IS NOT NULL` is load-bearing: `NULL ~ '…'` is NULL, `NULL OR false` is NULL, and a NULL CHECK PASSES — without it a
            // cv_model row with no hash got in (caught by the BE-34 migration probe).
            t.HasCheckConstraint("ck_artifact_version_hash",
                "length(version) > 0 AND ((artifact_hash IS NOT NULL AND artifact_hash ~ '^[0-9a-f]{64}$')"
                + $" OR (artifact_hash IS NULL AND component IN ({string.Join(",", ArtifactComponents.Firmware.Select(c => $"'{c}'"))})))");
            t.HasCheckConstraint("ck_artifact_version_metadata", "jsonb_typeof(metadata) = 'object'");
        });
        b.HasKey(x => x.VersionId);
        b.Property(x => x.VersionId).UseIdentityAlwaysColumn();
        b.Property(x => x.Metadata).HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.CreatedBy).HasColumnType("text");
        b.HasIndex(x => new { x.Component, x.Version }).IsUnique();
        b.HasIndex(x => x.CreatedBy);
    }
}

/// <summary>Find-or-insert of one registry row — the idempotent bootstrap every writer of versions shares.</summary>
public static class ArtifactRegistry
{
    /// <summary>The longest version string accepted from a device or a person; a version is a label, not a document.</summary>
    public const int VersionMaxLength = 100;

    /// <summary>
    /// A firmware version as a device or a person states it: trimmed, 1–<see cref="VersionMaxLength"/> characters, no control
    /// characters. Null when it is not one — the caller refuses or ignores it, never stores it half-cleaned.
    /// </summary>
    public static string? CleanVersion(string? version)
        => version?.Trim() is { Length: > 0 and <= VersionMaxLength } trimmed && !trimmed.Any(char.IsControl) ? trimmed : null;

    /// <summary>
    /// The row of <paramref name="component"/> at <paramref name="version"/>, inserted with no hash when nobody registered it
    /// first. <c>ON CONFLICT DO NOTHING</c>, so two writers at once both end on the same row.
    /// </summary>
    public static async Task<long> EnsureFirmwareAsync(LuxMapDbContext db, string component, string version, CancellationToken ct)
    {
        if (!ArtifactComponents.Firmware.Contains(component)) throw new ArgumentOutOfRangeException(nameof(component));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO artifact_version (component, version, artifact_hash, metadata, created_at)
            VALUES ({component}, {version}, NULL, jsonb_build_object(), now())
            ON CONFLICT (component, version) DO NOTHING
            """, ct);
        return await db.Set<ArtifactVersion>().Where(x => x.Component == component && x.Version == version)
            .Select(x => x.VersionId).SingleAsync(ct);
    }
}
