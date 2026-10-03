using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Survey.Configurations;

public sealed class SurveyFrameConfiguration : IEntityTypeConfiguration<SurveyFrame>
{
    public void Configure(EntityTypeBuilder<SurveyFrame> b)
    {
        b.ToTable("survey_frame", t =>
        {
            t.HasCheckConstraint("ck_survey_frame_dimensions", "width > 0 AND height > 0 AND byte_count > 0 AND thumbnail_bytes > 0 AND phone_elapsed_ns >= 0");
            t.HasCheckConstraint("ck_survey_frame_hash", "sha256 ~ '^[0-9a-f]{64}$'");
        });
        b.HasKey(x => x.FrameId); b.Property(x => x.FrameId).HasPrefixedId(PrefixedIds.SurveyFrame);
        b.HasAlternateKey(x => new { x.FrameId, x.SweepId });
        b.HasIndex(x => new { x.ClipId, x.PtsNs, x.ExtractorVersionId }).IsUnique();
        b.HasContractEnum(x => x.DataSource);
        b.HasOne<SurveySweep>().WithMany().HasForeignKey(x => new { x.SweepId, x.DataSource })
            .HasPrincipalKey(x => new { x.SweepId, x.DataSource }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SurveyVideoClip>().WithMany().HasForeignKey(x => new { x.ClipId, x.SweepId })
            .HasPrincipalKey(x => new { x.ClipId, x.SweepId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ArtifactVersion>().WithMany().HasForeignKey(x => x.ExtractorVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DetectionConfiguration : IEntityTypeConfiguration<Detection>
{
    public void Configure(EntityTypeBuilder<Detection> b)
    {
        b.ToTable("detection", t =>
        {
            t.HasCheckConstraint("ck_detection_state", "cv_state IN ('on','off') AND item_no >= 0");
            t.HasCheckConstraint("ck_detection_bbox", "confidence BETWEEN 0 AND 1 AND bbox_x BETWEEN 0 AND 1 AND bbox_y BETWEEN 0 AND 1 AND bbox_width > 0 AND bbox_width <= 1 AND bbox_height > 0 AND bbox_height <= 1 AND bbox_x + bbox_width <= 1 AND bbox_y + bbox_height <= 1");
            t.HasCheckConstraint("ck_detection_raw", "jsonb_typeof(raw_prediction) = 'object'");
        });
        b.HasKey(x => x.DetectionId); b.Property(x => x.DetectionId).HasPrefixedId(PrefixedIds.Detection);
        b.HasIndex(x => new { x.RunId, x.FrameId, x.ItemNo }).IsUnique();
        b.Property(x => x.RawPrediction).HasColumnType("jsonb");
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => new { x.RunId, x.SweepId })
            .HasPrincipalKey(x => new { x.RunId, x.SweepId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SurveyFrame>().WithMany().HasForeignKey(x => new { x.FrameId, x.SweepId })
            .HasPrincipalKey(x => new { x.FrameId, x.SweepId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ArtifactVersion>().WithMany().HasForeignKey(x => x.ModelVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
