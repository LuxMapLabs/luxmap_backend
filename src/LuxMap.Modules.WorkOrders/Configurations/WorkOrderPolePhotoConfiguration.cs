using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
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
            t.HasCheckConstraint("ck_pole_photo_decision", "(decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL AND NOT status_applied AND status_reason IS NULL) OR (decision IS NOT NULL AND decision IN ('confirm','reject') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND status_reason IS NOT NULL AND (decision <> 'confirm' OR (cv_state IS NOT NULL AND cv_state IN ('on','off'))) AND (NOT status_applied OR decision = 'confirm'))");
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
            "origin_pole_photo_id IS NULL OR (source_channel = 'cv' AND fault_type = 'lamp_out' AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL)"));
    }
}
