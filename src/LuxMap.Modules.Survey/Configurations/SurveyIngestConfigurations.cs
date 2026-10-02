using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Survey.Configurations;

public sealed class SurveySweepConfiguration : IEntityTypeConfiguration<SurveySweep>
{
    public void Configure(EntityTypeBuilder<SurveySweep> b)
    {
        b.ToTable("survey_sweep", t =>
        {
            t.HasCheckConstraint("ck_survey_sweep_interval", "started_elapsed_ns >= 0 AND (ended_elapsed_ns IS NULL OR ended_elapsed_ns >= started_elapsed_ns)");
            t.HasCheckConstraint("ck_survey_sweep_clock", "elapsed_anchor_ns >= 0 AND utc_uncertainty_ms >= 0 AND utc_uncertainty_ms < 'Infinity'::float8");
            t.HasCheckConstraint("ck_survey_sweep_request_hash", "create_request_hash ~ '^[0-9a-f]{64}$'");
        });
        b.HasKey(x => x.SweepId);
        b.Property(x => x.SweepId).HasPrefixedId(PrefixedIds.SurveySweep);
        b.Property(x => x.Version).IsRowVersion();
        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        b.HasContractEnum(x => x.Status);
        b.HasContractEnum(x => x.ProcessingStatus);
        b.HasContractEnum(x => x.DataSource);
        b.HasCommuneScope();
        b.HasCommuneReference(x => x.CommuneId);
        b.HasIndex(x => x.CommuneId);
        b.HasIndex(x => new { x.CapturedBy, x.ClientOpId }).IsUnique();
        b.HasIndex(x => new { x.WorkOrderId, x.CreatedAt });
        b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CapturedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Pays the BE-09 debt: last_sweep_id was text with no FK until survey_sweep existed.</summary>
public sealed class PoleCurrentStatusSweepConfiguration : IEntityTypeConfiguration<PoleCurrentStatus>
{
    public void Configure(EntityTypeBuilder<PoleCurrentStatus> b)
        => b.HasOne<SurveySweep>().WithMany().HasForeignKey(x => x.LastSweepId).OnDelete(DeleteBehavior.Restrict);
}

public sealed class SurveyVideoClipConfiguration : IEntityTypeConfiguration<SurveyVideoClip>
{
    public void Configure(EntityTypeBuilder<SurveyVideoClip> b)
    {
        b.ToTable("survey_video_clip", t =>
        {
            t.HasCheckConstraint("ck_survey_video_clip_number", "clip_no >= 0");
            t.HasCheckConstraint("ck_survey_video_clip_bytes", "byte_count > 0 AND byte_count <= 314572800");
            t.HasCheckConstraint("ck_survey_video_clip_hash", "sha256 ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_survey_video_clip_type", "content_type = 'video/mp4'");
        });
        b.HasKey(x => x.ClipId);
        b.Property(x => x.ClipId).UseIdentityAlwaysColumn();
        b.HasIndex(x => new { x.SweepId, x.ClipNo }).IsUnique();
        b.HasOne<SurveySweep>().WithMany().HasForeignKey(x => x.SweepId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyRawFileConfiguration : IEntityTypeConfiguration<SurveyRawFile>
{
    public void Configure(EntityTypeBuilder<SurveyRawFile> b)
    {
        b.ToTable("survey_raw_file", t =>
        {
            t.HasCheckConstraint("ck_survey_raw_file_bytes", "byte_count > 0");
            t.HasCheckConstraint("ck_survey_raw_file_hash", "sha256 ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_survey_raw_file_schema", "schema_version = 1");
        });
        b.HasKey(x => new { x.SweepId, x.Kind });
        b.HasContractEnum(x => x.Kind);
        b.HasOne<SurveySweep>().WithMany().HasForeignKey(x => x.SweepId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyGpsSampleConfiguration : IEntityTypeConfiguration<SurveyGpsSample>
{
    public void Configure(EntityTypeBuilder<SurveyGpsSample> b)
    {
        b.ToTable("survey_gps_sample", t =>
        {
            t.HasCheckConstraint("ck_survey_gps_sample_time", "sample_no >= 0 AND phone_elapsed_ns >= 0");
            t.HasCheckConstraint("ck_survey_gps_sample_accuracy", "accuracy_m >= 0 AND accuracy_m < 'Infinity'::float8");
            t.HasCheckConstraint("ck_survey_gps_sample_heading", "heading_deg IS NULL OR (heading_deg >= 0 AND heading_deg < 360)");
            t.HasCheckConstraint("ck_survey_gps_sample_speed", "speed_mps IS NULL OR (speed_mps >= 0 AND speed_mps < 'Infinity'::float8)");
            t.HasCheckConstraint("ck_survey_gps_sample_geom", "NOT ST_IsEmpty(geom) AND ST_X(geom) BETWEEN -180 AND 180 AND ST_Y(geom) BETWEEN -90 AND 90");
        });
        b.HasKey(x => new { x.SweepId, x.SampleNo });
        b.Property(x => x.Geom).HasColumnType("geometry(Point,4326)");
        b.HasIndex(x => x.Geom).HasMethod("gist");
        b.HasIndex(x => new { x.SweepId, x.PhoneElapsedNs });
        b.HasOne<SurveySweep>().WithMany().HasForeignKey(x => x.SweepId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyLuxSampleConfiguration : IEntityTypeConfiguration<SurveyLuxSample>
{
    public void Configure(EntityTypeBuilder<SurveyLuxSample> b)
    {
        b.ToTable("survey_lux_sample", t =>
        {
            t.HasCheckConstraint("ck_survey_lux_sample_time", "sample_no >= 0 AND phone_elapsed_ns >= 0 AND module_ms >= 0 AND module_epoch >= 0 AND seq >= 0");
            t.HasCheckConstraint("ck_survey_lux_sample_lux", "lux >= 0 AND lux < 'Infinity'::float8");
        });
        b.HasKey(x => new { x.SweepId, x.SampleNo });
        b.HasIndex(x => new { x.SweepId, x.ModuleEpoch, x.Seq }).IsUnique();
        b.HasIndex(x => new { x.SweepId, x.PhoneElapsedNs });
        b.HasOne<SurveySweep>().WithMany().HasForeignKey(x => x.SweepId).OnDelete(DeleteBehavior.Restrict);
    }
}
