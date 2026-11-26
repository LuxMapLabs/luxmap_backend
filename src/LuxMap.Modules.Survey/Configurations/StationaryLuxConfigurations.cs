using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Persistence.Versions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Survey.Configurations;

/// <summary>LUX-BASELINE D-7 (B): stationary baselines live beside, never inside, the sweep baselines.</summary>
public sealed class StationaryBaselineConfiguration : IEntityTypeConfiguration<StationaryBaseline>
{
    public void Configure(EntityTypeBuilder<StationaryBaseline> b)
    {
        b.ToTable("stationary_baseline", t => t.HasCheckConstraint("ck_stationary_baseline_value",
            "value > 0 AND value < 'Infinity'::float8 AND version > 0 AND member_count > 0"
            + " AND member_cv >= 0 AND member_cv < 'Infinity'::float8 AND length(btrim(protocol_fingerprint)) > 0"));
        b.HasKey(x => x.BaselineId); b.Property(x => x.BaselineId).UseIdentityAlwaysColumn();
        b.HasAlternateKey(x => new { x.BaselineId, x.PoleId, x.CommuneId });
        b.HasIndex(x => new { x.PoleId, x.Version }).IsUnique();
        b.Property(x => x.ProtocolFingerprint).HasColumnType("text");
        b.HasCommuneScope(); b.HasCommuneReference(x => x.CommuneId); b.HasIndex(x => x.CommuneId);
        b.HasContractEnum(x => x.DataSource);
        b.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ArtifactVersion>().WithMany().HasForeignKey(x => x.AlgorithmVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StationaryBaselineMemberConfiguration : IEntityTypeConfiguration<StationaryBaselineMember>
{
    public void Configure(EntityTypeBuilder<StationaryBaselineMember> b)
    {
        b.ToTable("stationary_baseline_member"); b.HasKey(x => new { x.BaselineId, x.LuxId });
        b.Property(x => x.LuxId).HasColumnType("text");
        b.HasOne(x => x.Baseline).WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LuxReading>().WithMany().HasForeignKey(x => x.LuxId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LuxEvaluationConfiguration : IEntityTypeConfiguration<LuxEvaluation>
{
    public void Configure(EntityTypeBuilder<LuxEvaluation> b)
    {
        b.ToTable("lux_evaluation", t =>
        {
            t.HasCheckConstraint("ck_lux_evaluation_finite",
                "lux_value >= 0 AND lux_value < 'Infinity'::float8"
                + " AND (baseline_value IS NULL OR (baseline_value > 0 AND baseline_value < 'Infinity'::float8))"
                + " AND (baseline_ratio IS NULL OR (baseline_ratio >= 0 AND baseline_ratio < 'Infinity'::float8))"
                + " AND dim_threshold_ratio > 0 AND dim_threshold_ratio <= 1"
                + " AND (expected_ratio IS NULL OR (expected_ratio > 0 AND expected_ratio <= 1))");
            // Same reading of the classification as luminance_history: out only from OFF, normal / dim only from ON, dim only
            // when judged against a baseline; a filter test never feeds a baseline.
            t.HasCheckConstraint("ck_lux_evaluation_classification",
                "cv_state IN ('on','off') AND classified_as IN ('normal','dim','out')"
                + " AND (classified_as <> 'out' OR cv_state = 'off') AND (classified_as NOT IN ('normal','dim') OR cv_state = 'on')"
                + " AND (NOT dim_evaluation_eligible OR (baseline_id IS NOT NULL AND baseline_ratio IS NOT NULL AND cv_state = 'on'))"
                + " AND (classified_as <> 'dim' OR dim_evaluation_eligible)"
                + " AND (baseline_id IS NULL) = (baseline_value IS NULL)"
                + " AND (NOT baseline_eligible OR (cv_state = 'on' AND classified_as = 'normal' AND expected_ratio IS NULL))"
                + " AND jsonb_typeof(reason_codes) = 'array'");
        });
        b.HasKey(x => x.LuxId);
        b.Property(x => x.LuxId).HasColumnType("text");
        b.Property(x => x.PhotoId).HasColumnType("text");
        b.Property(x => x.WorkOrderId).HasColumnType("text");
        b.Property(x => x.CvState).HasColumnType("text");
        b.Property(x => x.ReasonCodes).HasColumnType("jsonb");
        b.Property(x => x.ProtocolFingerprint).HasColumnType("text");
        b.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId).OnDelete(DeleteBehavior.Restrict);
        b.HasContractEnum(x => x.ClassifiedAs);
        b.HasContractEnum(x => x.DataSource);
        b.HasCommuneScope(); b.HasCommuneReference(x => x.CommuneId); b.HasIndex(x => x.CommuneId);
        b.HasIndex(x => new { x.PoleId, x.MeasuredAt });
        b.HasIndex(x => new { x.PoleId, x.ProtocolFingerprint }).HasFilter("baseline_eligible");
        b.HasIndex(x => x.PhotoId).IsUnique();
        b.HasOne<LuxReading>().WithMany().HasForeignKey(x => x.LuxId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkOrderPolePhoto>().WithMany().HasForeignKey(x => new { x.PhotoId, x.PoleId, x.CommuneId })
            .HasPrincipalKey(p => new { p.PhotoId, p.PoleId, p.CommuneId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_lux_evaluation_pole_photo");
        b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StationaryBaseline>().WithMany().HasForeignKey(x => new { x.BaselineId, x.PoleId, x.CommuneId })
            .HasPrincipalKey(x => new { x.BaselineId, x.PoleId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_lux_evaluation_stationary_baseline");
        b.HasOne<ArtifactVersion>().WithMany().HasForeignKey(x => x.AlgorithmVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.EvaluatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
