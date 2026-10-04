using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Survey.Configurations;

public sealed class LuminanceBaselineConfiguration : IEntityTypeConfiguration<LuminanceBaseline>
{
    public void Configure(EntityTypeBuilder<LuminanceBaseline> b)
    {
        b.ToTable("luminance_baseline", t =>
        {
            t.HasCheckConstraint("ck_luminance_baseline_value", "value > 0 AND value < 'Infinity'::float8 AND version > 0 AND member_count > 0");
            t.HasCheckConstraint("ck_luminance_baseline_direction", "direction IN ('forward','reverse')");
        });
        b.HasKey(x => x.BaselineId); b.Property(x => x.BaselineId).UseIdentityAlwaysColumn();
        b.HasAlternateKey(x => new { x.BaselineId, x.PoleId, x.CommuneId });
        b.HasIndex(x => new { x.PoleId, x.Version }).IsUnique();
        b.HasCommuneScope(); b.HasCommuneReference(x => x.CommuneId); b.HasIndex(x => x.CommuneId);
        b.HasContractEnum(x => x.DataSource);
        b.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ArtifactVersion>().WithMany().HasForeignKey(x => x.AlgorithmVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class BaselineMemberConfiguration : IEntityTypeConfiguration<BaselineMember>
{
    public void Configure(EntityTypeBuilder<BaselineMember> b)
    {
        b.ToTable("baseline_member"); b.HasKey(x => new { x.BaselineId, x.ObservationId });
        b.HasOne(x => x.Baseline).WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PoleObservation>().WithMany().HasForeignKey(x => x.ObservationId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LuminanceHistoryConfiguration : IEntityTypeConfiguration<LuminanceHistory>
{
    public void Configure(EntityTypeBuilder<LuminanceHistory> b)
    {
        b.ToTable("luminance_history", t =>
        {
            t.HasCheckConstraint("ck_luminance_history_unobserved", "observation_id IS NOT NULL OR (classified_as = 'unknown' AND peak_lux IS NULL AND cv_state IS NULL AND baseline_id IS NULL AND baseline_ratio IS NULL AND status_confidence IS NULL)");
            t.HasCheckConstraint("ck_luminance_history_finite", "(peak_lux IS NULL OR (peak_lux >= 0 AND peak_lux < 'Infinity'::float8)) AND (baseline_ratio IS NULL OR (baseline_ratio >= 0 AND baseline_ratio < 'Infinity'::float8)) AND association_confidence BETWEEN 0 AND 1 AND (status_confidence IS NULL OR status_confidence BETWEEN 0 AND 1)");
            t.HasCheckConstraint("ck_luminance_history_classification", "(classified_as = 'unknown') = (status_confidence IS NULL) AND (classified_as = 'unknown' OR cv_state IS NOT NULL) AND (cv_state IS NULL OR cv_state IN ('on','off')) AND (classified_as <> 'out' OR cv_state = 'off') AND (classified_as NOT IN ('normal','dim') OR cv_state = 'on') AND (NOT dim_evaluation_eligible OR (baseline_id IS NOT NULL AND baseline_ratio IS NOT NULL AND cv_state = 'on')) AND (classified_as <> 'dim' OR dim_evaluation_eligible) AND jsonb_typeof(reason_codes) = 'array'");
        });
        b.HasKey(x => new { x.SweepId, x.PoleId });
        b.HasCommuneScope(); b.HasCommuneReference(x => x.CommuneId); b.HasIndex(x => x.CommuneId);
        b.HasIndex(x => new { x.PoleId, x.EvaluatedAt }); b.HasIndex(x => x.DataSource);
        b.HasContractEnum(x => x.DataSource); b.HasContractEnum(x => x.ClassifiedAs);
        b.Property(x => x.ReasonCodes).HasColumnType("jsonb");
        b.HasOne<SurveyProcessingRun>().WithMany().HasForeignKey(x => new { x.RunId, x.SweepId })
            .HasPrincipalKey(x => new { x.RunId, x.SweepId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PoleObservation>().WithMany().HasForeignKey(x => new { x.ObservationId, x.PoleId, x.CommuneId, x.RunId })
            .HasPrincipalKey(x => new { x.ObservationId, x.PoleId, x.CommuneId, x.RunId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LuminanceBaseline>().WithMany().HasForeignKey(x => new { x.BaselineId, x.PoleId, x.CommuneId })
            .HasPrincipalKey(x => new { x.BaselineId, x.PoleId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PublishedBy).OnDelete(DeleteBehavior.Restrict);
        // The composite FKs above skip a row whose observation_id and baseline_id are null (MATCH SIMPLE),
        // which is every not_observed row. Published history must still keep its pole from being deleted.
        b.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ObservationBaselineConfiguration : IEntityTypeConfiguration<PoleObservation>
{
    public void Configure(EntityTypeBuilder<PoleObservation> b)
    {
        b.ToTable("pole_observation", t => t.HasCheckConstraint("ck_pole_observation_baseline_value",
            "(baseline_id IS NULL) = (baseline_value IS NULL) AND (baseline_value IS NULL OR (baseline_value > 0 AND baseline_value < 'Infinity'::float8))"));
        b.HasAlternateKey(x => new { x.ObservationId, x.PoleId, x.CommuneId });
        b.HasAlternateKey(x => new { x.ObservationId, x.PoleId, x.CommuneId, x.RunId });
        b.HasOne<LuminanceBaseline>().WithMany().HasForeignKey(x => new { x.BaselineId, x.PoleId, x.CommuneId })
            .HasPrincipalKey(x => new { x.BaselineId, x.PoleId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class FaultObservationConfiguration : IEntityTypeConfiguration<Fault>
{
    public void Configure(EntityTypeBuilder<Fault> b)
    {
        b.ToTable("fault", t => t.HasCheckConstraint("ck_fault_observation_source",
            "origin_observation_id IS NULL OR (source_channel = 'cv' AND pole_id IS NOT NULL)"));
        b.HasOne<PoleObservation>().WithMany().HasForeignKey(x => new { x.OriginObservationId, x.PoleId, x.CommuneId })
            .HasPrincipalKey(x => new { x.ObservationId, x.PoleId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class CurrentStatusRunConfiguration : IEntityTypeConfiguration<PoleCurrentStatus>
{
    public void Configure(EntityTypeBuilder<PoleCurrentStatus> b)
    {
        b.HasOne<SurveyProcessingRun>().WithMany().HasForeignKey(x => new { x.LastRunId, x.LastSweepId })
            .HasPrincipalKey(x => new { x.RunId, x.SweepId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class SweepReviewConfiguration : IEntityTypeConfiguration<SurveySweep>
{
    public void Configure(EntityTypeBuilder<SurveySweep> b)
    {
        b.ToTable("survey_sweep", t =>
        {
            t.HasCheckConstraint("ck_survey_sweep_review", "(status IN ('accepted','returned')) = (reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL AND review_client_op_id IS NOT NULL AND review_request_hash IS NOT NULL) AND (status = 'accepted') = (accepted_run_id IS NOT NULL) AND (status <> 'returned' OR (review_note IS NOT NULL AND length(btrim(review_note)) > 0))");
        });
        b.HasOne<SurveyProcessingRun>().WithMany().HasForeignKey(x => new { x.AcceptedRunId, x.SweepId })
            .HasPrincipalKey(x => new { x.RunId, x.SweepId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ReviewedBy, x.ReviewClientOpId }).IsUnique().HasFilter("review_client_op_id IS NOT NULL");
    }
}
