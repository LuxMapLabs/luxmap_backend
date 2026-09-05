using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Faults.Configurations;

/// <summary>
/// Shared wording for the two float CHECKs, so the NaN reasoning is written once.
/// </summary>
/// <remarks>
/// ⚠️ <c>&gt;= 0</c> does NOT exclude <c>NaN</c> or <c>Infinity</c>: PostgreSQL sorts <c>NaN</c>
/// ABOVE every other float, the opposite of IEEE 754. And the NaN test is <c>&lt;&gt; 'NaN'</c>, NOT
/// <c>x = x</c> — PostgreSQL treats NaN as equal to itself, so that idiom is a real tautology here.
/// Both were established by experiment during the BE-42 fix; see CLAUDE.md.
/// </remarks>
internal static class FiniteChecks
{
    public static string Finite(string column)
        => $"{column} <> 'NaN'::float8 AND {column} <> 'Infinity'::float8 AND {column} <> '-Infinity'::float8";
}

public sealed class FaultClusterConfiguration : IEntityTypeConfiguration<FaultCluster>
{
    public void Configure(EntityTypeBuilder<FaultCluster> builder)
    {
        builder.ToTable("fault_cluster");
        builder.HasKey(cluster => cluster.ClusterId);

        builder.Property(cluster => cluster.ClusterId).HasPrefixedId(PrefixedIds.FaultCluster);
        builder.Property(cluster => cluster.SegmentId).HasColumnType("text").IsRequired();
        builder.Property(cluster => cluster.ClusteringModelVersion).HasColumnType("text");
        builder.Property(cluster => cluster.ClusteredAt).IsRequired();
        builder.Property(cluster => cluster.CreatedAt).HasDefaultValueSql("now()");

        builder.HasCommuneScope();
        builder.HasCommuneReference(cluster => cluster.CommuneId);

        builder.HasOne<RoadSegment>()
            .WithMany()
            .HasForeignKey(cluster => cluster.SegmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(cluster => cluster.SegmentId)
            .HasDatabaseName("ix_fault_cluster_segment_id");
    }
}

public sealed class FaultConfiguration : IEntityTypeConfiguration<Fault>
{
    public void Configure(EntityTypeBuilder<Fault> builder)
    {
        builder.ToTable("fault");
        builder.HasKey(fault => fault.FaultId);

        builder.Property(fault => fault.FaultId).HasPrefixedId(PrefixedIds.Fault);

        foreach (var text in new[] { "ClientOpId", "PoleId", "FixtureId", "SegmentId", "ClusterId",
                                     "Note", "ReportedBy", "ConfirmedBy", "ResolvedBy",
                                     "DetectionModelVersion" })
        {
            builder.Property(text).HasColumnType("text");
        }

        builder.Property(fault => fault.Lat).HasColumnType("double precision");
        builder.Property(fault => fault.Lng).HasColumnType("double precision");
        builder.Property(fault => fault.PriorityScore).HasColumnType("double precision");
        builder.Property(fault => fault.StatusConfidence).HasColumnType("double precision");

        builder.Property(fault => fault.DetectedAt).IsRequired();
        builder.Property(fault => fault.UpdatedAt).HasDefaultValueSql("now()");
        builder.Property(fault => fault.CreatedAt).HasDefaultValueSql("now()");

        // Five Contract section 1 enums, each stored as the exact string the API returns and pinned
        // by a generated CHECK.
        builder.HasContractEnum(fault => fault.FaultType);
        builder.HasContractEnum(fault => fault.FaultStatus);
        builder.HasContractEnum(fault => fault.Severity);
        builder.HasContractEnum(fault => fault.SourceChannel);
        builder.HasContractEnum(fault => fault.DataSource);

        builder.HasCommuneScope();
        builder.HasCommuneReference(fault => fault.CommuneId);

        builder.ToTable(table =>
        {
            // Contract section 2.8: LOCATION_REQUIRED exists precisely for the case where neither is
            // given. Enforced at the table because it is a rule ABOUT TWO COLUMNS — a per-column
            // constraint cannot express it, and leaving it to the API means the next writer (CSV
            // import, sync push, a seeder) can create a fault nobody can go and find.
            table.HasCheckConstraint(
                "ck_fault_pole_or_location",
                "pole_id IS NOT NULL OR (lat IS NOT NULL AND lng IS NOT NULL)");

            // A ranking score that is not a number would sort into an arbitrary position and poison
            // every average BE-28 computes. NULL stays legal: CV-16 may not have run yet.
            table.HasCheckConstraint(
                "ck_fault_priority_score_finite",
                $"priority_score IS NULL OR ({FiniteChecks.Finite("priority_score")})");

            // 0..1 is stated in Contract section 2.4 and, unlike pole_current_status.status_confidence,
            // is actually enforced here. That column accepts NaN and 42.5 today — see drift 24.
            table.HasCheckConstraint(
                "ck_fault_status_confidence_range",
                $"status_confidence IS NULL OR (status_confidence >= 0 AND status_confidence <= 1 "
                + $"AND {FiniteChecks.Finite("status_confidence")})");

            // Latitude and longitude are only ever read back, but a NaN here would put a marker
            // nowhere on the map with no error anywhere.
            table.HasCheckConstraint(
                "ck_fault_location_finite",
                $"(lat IS NULL OR ({FiniteChecks.Finite("lat")} AND lat BETWEEN -90 AND 90)) AND "
                + $"(lng IS NULL OR ({FiniteChecks.Finite("lng")} AND lng BETWEEN -180 AND 180))");
        });

        // RESTRICT everywhere. A fault is an event that happened: removing a pole must not erase the
        // record that its lamp was once out, and the audit columns on this row are the acceptance
        // criterion of BE-18. Cascade would also widen the blind spot recorded in CLAUDE.md 1c,
        // where the SaveChanges guard cannot see deletions the database performs.
        builder.HasOne<Pole>().WithMany()
            .HasForeignKey(fault => fault.PoleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Fixture>().WithMany()
            .HasForeignKey(fault => fault.FixtureId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RoadSegment>().WithMany()
            .HasForeignKey(fault => fault.SegmentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FaultCluster>().WithMany()
            .HasForeignKey(fault => fault.ClusterId).OnDelete(DeleteBehavior.Restrict);

        // Three separate foreign keys to app_user. Each is a different question — who reported it,
        // who judged it, who fixed it — and collapsing them into one column would lose the answers.
        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(fault => fault.ReportedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(fault => fault.ConfirmedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(fault => fault.ResolvedBy).OnDelete(DeleteBehavior.Restrict);

        // PARTIAL: engine faults carry no client_op_id, and only the reported ones need to be unique.
        builder.HasIndex(fault => fault.ClientOpId)
            .IsUnique()
            .HasFilter("client_op_id IS NOT NULL")
            .HasDatabaseName("ux_fault_client_op_id");

        builder.HasIndex(fault => fault.PoleId).HasDatabaseName("ix_fault_pole_id");
        builder.HasIndex(fault => fault.SegmentId).HasDatabaseName("ix_fault_segment_id");
        builder.HasIndex(fault => fault.ClusterId).HasDatabaseName("ix_fault_cluster_id");
        builder.HasIndex(fault => fault.FaultStatus).HasDatabaseName("ix_fault_fault_status");

        // Serves the default ordering of Contract section 2.4 (-priority_score). DESC with NULLS
        // LAST matches how the list is read: faults CV-16 has not scored belong at the end, not the
        // top, and a plain DESC index would put NULLs first in PostgreSQL.
        builder.HasIndex(fault => fault.PriorityScore)
            .IsDescending()
            .HasDatabaseName("ix_fault_priority_score");
    }
}
