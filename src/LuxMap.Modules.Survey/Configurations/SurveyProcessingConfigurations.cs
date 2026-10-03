using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Survey.Configurations;

public sealed class ArtifactVersionConfiguration : IEntityTypeConfiguration<ArtifactVersion>
{
    public void Configure(EntityTypeBuilder<ArtifactVersion> b)
    {
        b.ToTable("artifact_version", t =>
        {
            t.HasCheckConstraint("ck_artifact_version_component", "component IN ('clock_algorithm','association_algorithm')");
            t.HasCheckConstraint("ck_artifact_version_hash", "artifact_hash ~ '^[0-9a-f]{64}$' AND length(version) > 0");
            t.HasCheckConstraint("ck_artifact_version_metadata", "jsonb_typeof(metadata) = 'object'");
        });
        b.HasKey(x => x.VersionId);
        b.Property(x => x.VersionId).UseIdentityAlwaysColumn();
        b.Property(x => x.Metadata).HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.HasIndex(x => new { x.Component, x.Version }).IsUnique();
    }
}

public sealed class SurveyProcessingRunConfiguration : IEntityTypeConfiguration<SurveyProcessingRun>
{
    public void Configure(EntityTypeBuilder<SurveyProcessingRun> b)
    {
        b.ToTable("survey_processing_run", t =>
        {
            t.HasCheckConstraint("ck_survey_run_state", "result_state IN ('succeeded','failed') AND ((result_state = 'failed') = (error_code IS NOT NULL))");
            t.HasCheckConstraint("ck_survey_run_attempt", "attempt > 0 AND finished_at >= started_at");
            t.HasCheckConstraint("ck_survey_run_hash", "input_hash ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_survey_run_json", "jsonb_typeof(settings_snapshot) = 'object' AND jsonb_typeof(gis_snapshot) = 'object' AND jsonb_typeof(clock_fit) = 'object'");
            t.HasCheckConstraint("ck_survey_run_coverage", "coverage_pct IS NULL OR coverage_pct BETWEEN 0 AND 100");
        });
        b.HasKey(x => x.RunId);
        b.Property(x => x.RunId).UseIdentityAlwaysColumn();
        b.HasAlternateKey(x => new { x.RunId, x.SweepId });
        b.HasIndex(x => new { x.SweepId, x.Attempt }).IsUnique();
        b.HasCommuneScope(); b.HasCommuneReference(x => x.CommuneId); b.HasIndex(x => x.CommuneId);
        b.Property(x => x.SettingsSnapshot).HasColumnType("jsonb");
        b.Property(x => x.GisSnapshot).HasColumnType("jsonb");
        b.Property(x => x.ClockFit).HasColumnType("jsonb");
        b.HasOne<SurveySweep>().WithMany().HasForeignKey(x => new { x.SweepId, x.CommuneId })
            .HasPrincipalKey(x => new { x.SweepId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ArtifactVersion>().WithMany().HasForeignKey(x => x.AlgorithmVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ArtifactVersion>().WithMany().HasForeignKey(x => x.ClockVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyPassConfiguration : IEntityTypeConfiguration<SurveyPass>
{
    public void Configure(EntityTypeBuilder<SurveyPass> b)
    {
        b.ToTable("survey_pass", t =>
        {
            t.HasCheckConstraint("ck_survey_pass_time", "start_elapsed_ns >= 0 AND end_elapsed_ns > start_elapsed_ns AND pass_no >= 0");
            t.HasCheckConstraint("ck_survey_pass_direction", "(direction = 'forward' AND from_fraction < to_fraction) OR (direction = 'reverse' AND from_fraction > to_fraction)");
            t.HasCheckConstraint("ck_survey_pass_fractions", "from_fraction BETWEEN 0 AND 1 AND to_fraction BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_survey_pass_quality", "jsonb_typeof(quality_flags) = 'object'");
        });
        b.HasKey(x => x.PassId); b.Property(x => x.PassId).UseIdentityAlwaysColumn();
        b.HasAlternateKey(x => new { x.PassId, x.RunId });
        b.HasIndex(x => new { x.RunId, x.PassNo }).IsUnique();
        b.Property(x => x.QualityFlags).HasColumnType("jsonb");
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RoadSegment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PoleObservationConfiguration : IEntityTypeConfiguration<PoleObservation>
{
    public void Configure(EntityTypeBuilder<PoleObservation> b)
    {
        b.ToTable("pole_observation", t =>
        {
            t.HasCheckConstraint("ck_pole_observation_time", "observed_elapsed_ns >= 0 AND (peak_at_elapsed_ns IS NULL OR peak_at_elapsed_ns >= 0)");
            t.HasCheckConstraint("ck_pole_observation_finite", "chainage_m >= 0 AND chainage_m < 'Infinity'::float8 AND speed_mps >= 0 AND speed_mps < 'Infinity'::float8 AND association_confidence BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_pole_observation_peak", "(peak_lux IS NULL) = (peak_at_elapsed_ns IS NULL) AND (peak_lux IS NULL OR (peak_lux >= 0 AND peak_lux < 'Infinity'::float8))");
            t.HasCheckConstraint("ck_pole_observation_quality", "jsonb_typeof(quality_flags) = 'array'");
        });
        b.HasKey(x => x.ObservationId); b.Property(x => x.ObservationId).UseIdentityAlwaysColumn();
        b.HasIndex(x => new { x.PassId, x.PoleId }).IsUnique();
        b.HasIndex(x => new { x.PoleId, x.ObservedAt });
        b.HasCommuneScope(); b.HasCommuneReference(x => x.CommuneId); b.HasIndex(x => x.CommuneId);
        b.HasContractEnum(x => x.DataSource);
        b.Property(x => x.QualityFlags).HasColumnType("jsonb");
        b.HasOne(x => x.Pass).WithMany().HasForeignKey(x => new { x.PassId, x.RunId })
            .HasPrincipalKey(x => new { x.PassId, x.RunId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        // Commune is copied from the scoped GIS snapshot, never supplied by an API caller.
        b.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SweepProcessingConfiguration : IEntityTypeConfiguration<SurveySweep>
{
    public void Configure(EntityTypeBuilder<SurveySweep> b)
    {
        b.ToTable("survey_sweep", t =>
        {
            t.HasCheckConstraint("ck_survey_sweep_lease", "(processing_lease_owner IS NULL) = (processing_lease_expires_at IS NULL) AND processing_attempt >= 0");
            t.HasCheckConstraint("ck_survey_sweep_coverage", "coverage_pct IS NULL OR coverage_pct BETWEEN 0 AND 100");
        });
        b.HasIndex(x => new { x.ProcessingStatus, x.ProcessingLeaseExpiresAt });
        b.HasIndex(x => x.CommuneId);
    }
}
