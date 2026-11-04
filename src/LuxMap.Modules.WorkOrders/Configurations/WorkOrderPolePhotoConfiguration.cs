using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Persistence.Versions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.WorkOrders.Configurations;

public sealed class WorkOrderPolePhotoConfiguration : IEntityTypeConfiguration<WorkOrderPolePhoto>
{
    public void Configure(EntityTypeBuilder<WorkOrderPolePhoto> b)
    {
        b.ToTable("work_order_pole_photo", t =>
        {
            t.HasCheckConstraint("ck_pole_photo_lat", "lat BETWEEN -90 AND 90 AND lat <> 'NaN'::float8");
            t.HasCheckConstraint("ck_pole_photo_lng", "lng BETWEEN -180 AND 180 AND lng <> 'NaN'::float8");
            t.HasCheckConstraint("ck_pole_photo_sizes", "byte_count BETWEEN 1 AND 16777216 AND thumbnail_bytes > 0 AND width > 0 AND height > 0 AND width::bigint * height <= 40000000");
            t.HasCheckConstraint("ck_pole_photo_keys", "btrim(object_key) <> '' AND btrim(thumbnail_key) <> '' AND request_hash ~ '^[0-9a-f]{64}$' AND client_op_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            t.HasCheckConstraint("ck_pole_photo_ai", "(cv_state IS NULL AND confidence IS NULL AND model_version IS NULL AND artifact_version IS NULL AND analyzed_at IS NULL) OR (cv_state IS NOT NULL AND cv_state IN ('on','off','inconclusive') AND model_version IS NOT NULL AND btrim(model_version) <> '' AND artifact_version IS NOT NULL AND btrim(artifact_version) <> '' AND analyzed_at IS NOT NULL AND ((cv_state = 'inconclusive' AND confidence IS NULL) OR (cv_state IN ('on','off') AND confidence IS NOT NULL AND confidence BETWEEN 0 AND 1 AND confidence <> 'NaN'::float8)))");
            t.HasCheckConstraint("ck_pole_photo_review_complete", "(final_cv_state IS NULL AND review_note IS NULL AND reviewed_by IS NULL AND reviewed_at IS NULL) OR (final_cv_state IS NOT NULL AND final_cv_state IN ('on','off') AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)");
            t.HasCheckConstraint("ck_pole_photo_review_note", "review_note IS NULL OR (char_length(review_note) BETWEEN 1 AND 2000 AND btrim(review_note, E' \\t\\n\\r\\f' || chr(11)) <> '')");
            t.HasCheckConstraint("ck_pole_photo_review_note_required", "final_cv_state IS NULL OR final_cv_state IS NOT DISTINCT FROM cv_state OR review_note IS NOT NULL");
            t.HasCheckConstraint("ck_pole_photo_decision", "(decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL AND NOT status_applied AND status_reason IS NULL) OR (decision IS NOT NULL AND decision IN ('confirm','reject') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND status_reason IS NOT NULL AND (decision <> 'confirm' OR (COALESCE(final_cv_state, cv_state) IS NOT NULL AND COALESCE(final_cv_state, cv_state) IN ('on','off'))) AND (NOT status_applied OR decision = 'confirm'))");
            // POLE-VIDEO D-1: clip columns are all-or-none; ceilings (duration, frame cap) are service settings, so only invariants live here.
            t.HasCheckConstraint("ck_pole_photo_video_complete", "(video_object_key IS NULL AND video_sha256 IS NULL AND video_bytes IS NULL AND video_duration_ms IS NULL AND video_sampled_frames IS NULL AND video_on_frames IS NULL AND video_off_frames IS NULL AND video_daylight_frames IS NULL AND video_median_luma IS NULL AND video_hdr IS NULL AND video_extractor_version_id IS NULL) OR (video_object_key IS NOT NULL AND video_sha256 IS NOT NULL AND video_bytes IS NOT NULL AND video_duration_ms IS NOT NULL AND video_sampled_frames IS NOT NULL AND video_on_frames IS NOT NULL AND video_off_frames IS NOT NULL AND video_daylight_frames IS NOT NULL AND video_median_luma IS NOT NULL AND video_hdr IS NOT NULL AND video_extractor_version_id IS NOT NULL)");
            t.HasCheckConstraint("ck_pole_photo_video_values", "video_object_key IS NULL OR (btrim(video_object_key) <> '' AND video_sha256 ~ '^[0-9a-f]{64}$' AND video_bytes > 0 AND video_duration_ms > 0 AND video_sampled_frames > 0 AND video_on_frames >= 0 AND video_off_frames >= 0 AND video_daylight_frames >= 0 AND video_on_frames + video_off_frames + video_daylight_frames <= video_sampled_frames AND video_median_luma BETWEEN 0 AND 1 AND video_median_luma <> 'NaN'::float8)");
            t.HasCheckConstraint("ck_pole_photo_video_confidence", "(video_confidence_min IS NULL AND video_confidence_median IS NULL AND video_confidence_max IS NULL AND (video_object_key IS NULL OR video_on_frames + video_off_frames = 0)) OR (video_object_key IS NOT NULL AND video_on_frames + video_off_frames > 0 AND video_confidence_min IS NOT NULL AND video_confidence_median IS NOT NULL AND video_confidence_max IS NOT NULL AND video_confidence_min BETWEEN 0 AND 1 AND video_confidence_median BETWEEN 0 AND 1 AND video_confidence_max BETWEEN 0 AND 1 AND video_confidence_min <> 'NaN'::float8 AND video_confidence_median <> 'NaN'::float8 AND video_confidence_max <> 'NaN'::float8 AND video_confidence_min <= video_confidence_median AND video_confidence_median <= video_confidence_max)");
            t.HasCheckConstraint("ck_pole_photo_video_reason", "video_reason_code IS NULL OR (video_object_key IS NOT NULL AND btrim(video_reason_code) <> '')");
            t.HasCheckConstraint("ck_pole_photo_video_viewed", "(video_viewed_by IS NULL AND video_viewed_at IS NULL) OR (video_viewed_by IS NOT NULL AND video_viewed_at IS NOT NULL AND video_object_key IS NOT NULL)");
        });
        b.HasKey(x => x.PhotoId);
        b.HasAlternateKey(x => new { x.PhotoId, x.PoleId, x.CommuneId });
        b.Property(x => x.PhotoId).HasPrefixedId(PrefixedIds.WorkOrderPolePhoto);
        b.HasContractEnum(x => x.DataSource);
        b.HasCommuneScope();
        b.HasCommuneReference(x => x.CommuneId);
        b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
        // Same-commune pole FK is DB-only, to avoid turning mutable Pole.CommuneId into an EF key.
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pole_photo_reviewed_by");
        b.HasIndex(x => x.ReviewedBy).HasDatabaseName("ix_pole_photo_reviewed_by");
        b.HasOne<ArtifactVersion>().WithMany().HasForeignKey(x => x.VideoExtractorVersionId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pole_photo_video_extractor_version");
        b.HasIndex(x => x.VideoExtractorVersionId).HasDatabaseName("ix_pole_photo_video_extractor_version");
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.VideoViewedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pole_photo_video_viewed_by");
        b.HasIndex(x => x.VideoViewedBy).HasDatabaseName("ix_pole_photo_video_viewed_by");
        b.HasIndex(x => x.CommuneId);
        b.HasIndex(x => new { x.WorkOrderId, x.CapturedAt, x.UploadedAt });
        b.HasIndex(x => new { x.PoleId, x.CapturedAt });
        b.HasIndex(x => new { x.UploadedBy, x.ClientOpId }).IsUnique().HasDatabaseName("ux_pole_photo_user_operation");
    }
}

/// <summary>Owned by WorkOrders; Faults does not reference the photo module back.</summary>
public sealed class FaultOriginPolePhotoConfiguration : IEntityTypeConfiguration<Fault>
{
    public void Configure(EntityTypeBuilder<Fault> b)
    {
        b.HasOne<WorkOrderPolePhoto>().WithMany().HasForeignKey(x => new { x.OriginPolePhotoId, x.PoleId, x.CommuneId })
            .HasPrincipalKey(x => new { x.PhotoId, x.PoleId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("fault", t => t.HasCheckConstraint("ck_fault_pole_photo_source",
            "origin_pole_photo_id IS NULL OR (source_channel IN ('cv','field_report') AND fault_type = 'lamp_out' AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL AND (source_channel <> 'field_report' OR (reported_by IS NOT NULL AND status_confidence IS NULL AND detection_model_version IS NULL)))"));
    }
}
