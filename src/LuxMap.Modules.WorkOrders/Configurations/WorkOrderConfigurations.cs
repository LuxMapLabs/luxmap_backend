using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.WorkOrders.Configurations;

public sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_order", table =>
        {
            table.HasCheckConstraint("ck_work_order_survey_target", "task_kind <> 'survey' OR (segment_id IS NULL AND cluster_id IS NULL)");
            // WO-POLE-GPS PG-1/PG-2: one road, no cluster, never a step of a chain.
            table.HasCheckConstraint("ck_work_order_pole_gps_target",
                "task_kind <> 'pole_gps_update' OR (segment_id IS NOT NULL AND cluster_id IS NULL AND parent_work_order_id IS NULL)");
            table.HasCheckConstraint("ck_work_order_title_not_blank", "btrim(title) <> ''");
            table.HasCheckConstraint("ck_work_order_assignee_matches_status", "(wo_status = 'open' AND assigned_to IS NULL) OR (wo_status IN ('assigned','in_progress','done','verified') AND assigned_to IS NOT NULL) OR wo_status = 'cancelled'");
            table.HasCheckConstraint("ck_work_order_assigned_at_matches", "(assigned_to IS NULL) = (assigned_at IS NULL)");
            table.HasCheckConstraint("ck_work_order_started", "wo_status NOT IN ('in_progress','done','verified') OR started_at IS NOT NULL");
            table.HasCheckConstraint("ck_work_order_completed", "wo_status NOT IN ('done','verified') OR (completed_at IS NOT NULL AND report_note IS NOT NULL)");
            table.HasCheckConstraint("ck_work_order_closed", "(wo_status IN ('verified','cancelled')) = (closed_at IS NOT NULL)");
            table.HasCheckConstraint("ck_work_order_schedule_before_due", "scheduled_date IS NULL OR due_date IS NULL OR scheduled_date <= due_date");
            // Blank means "nothing written": the API stores it as NULL, and the table refuses the other spelling.
            table.HasCheckConstraint("ck_work_order_materials_note_not_blank", "materials_note IS NULL OR btrim(materials_note) <> ''");
            table.HasCheckConstraint("ck_work_order_materials_used_not_blank", "materials_used IS NULL OR btrim(materials_used) <> ''");
            // A root has neither; a follow-up has both (FR-2).
            table.HasCheckConstraint("ck_work_order_chain_complete", "(parent_work_order_id IS NULL) = (root_work_order_id IS NULL)");
        });
        builder.HasKey(x => x.WorkOrderId);
        builder.HasAlternateKey(x => new { x.WorkOrderId, x.CommuneId });
        builder.Property(x => x.WorkOrderId).HasPrefixedId(PrefixedIds.WorkOrder);
        builder.Property(x => x.Version).IsRowVersion();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasContractEnum(x => x.TaskKind);
        builder.HasContractEnum(x => x.WoStatus);
        builder.HasContractEnum(x => x.CancellationRequestStatus);
        builder.Property(x => x.CancellationRequestStatus).HasDefaultValue(CancellationRequestStatus.None);
        builder.ToTable(table => table.HasCheckConstraint("ck_work_order_cancellation_request_state", """
            (cancellation_request_status = 'none' AND cancellation_request_note IS NULL AND cancellation_requested_at IS NULL
                AND cancellation_reject_note IS NULL AND cancellation_rejected_at IS NULL)
            OR (wo_status = 'assigned' AND cancellation_request_note IS NOT NULL
                AND char_length(btrim(cancellation_request_note)) BETWEEN 1 AND 500 AND cancellation_requested_at IS NOT NULL
                AND ((cancellation_request_status = 'pending' AND cancellation_reject_note IS NULL AND cancellation_rejected_at IS NULL)
                    OR (cancellation_request_status = 'rejected' AND cancellation_reject_note IS NOT NULL
                        AND char_length(btrim(cancellation_reject_note)) BETWEEN 1 AND 500
                        AND cancellation_rejected_at IS NOT NULL AND cancellation_rejected_at >= cancellation_requested_at)))
            """));
        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
        builder.HasOne<RoadSegment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FaultCluster>().WithMany().HasForeignKey(x => x.ClusterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AssignedTo).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        // A chain never leaves its commune: both links carry commune_id, as the fault links do.
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.ParentWorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.RootWorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in new[] { "CommuneId", "AssignedTo", "CreatedBy", "SegmentId", "ClusterId", "WoStatus" })
        {
            builder.HasIndex(property);
        }
    }
}

public sealed class WorkOrderFaultConfiguration : IEntityTypeConfiguration<WorkOrderFault>
{
    public void Configure(EntityTypeBuilder<WorkOrderFault> builder)
    {
        builder.ToTable("work_order_fault", table => table.HasCheckConstraint(
            "ck_work_order_fault_release_after_link", "released_at IS NULL OR released_at >= linked_at"));
        builder.HasKey(x => new { x.WorkOrderId, x.FaultId });
        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
        builder.HasContractEnum(x => x.InspectionOutcome);
        builder.Property(x => x.LinkedAt).HasDefaultValueSql("now()");
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Fault>().WithMany().HasForeignKey(x => new { x.FaultId, x.CommuneId })
            .HasPrincipalKey(x => new { x.FaultId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.FaultId, "ux_work_order_fault_fault_id_active").HasDatabaseName("ux_work_order_fault_fault_id_active").IsUnique().HasFilter("released_at IS NULL");
        builder.HasIndex(x => x.FaultId);
        builder.HasIndex(x => x.CommuneId);
    }
}

public sealed class WorkOrderSegmentConfiguration : IEntityTypeConfiguration<WorkOrderSegment>
{
    public void Configure(EntityTypeBuilder<WorkOrderSegment> builder)
    {
        builder.ToTable("work_order_segment", t => t.HasCheckConstraint("ck_work_order_segment_position", "position >= 0"));
        builder.HasKey(x => new { x.WorkOrderId, x.Position });
        builder.HasIndex(x => new { x.WorkOrderId, x.SegmentId }).IsUnique();
        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
        builder.HasIndex(x => x.CommuneId);
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RoadSegment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WorkOrderPoleTargetConfiguration : IEntityTypeConfiguration<WorkOrderPoleTarget>
{
    public const string ActiveIndex = "ux_work_order_pole_target_pole_id_active";

    public void Configure(EntityTypeBuilder<WorkOrderPoleTarget> builder)
    {
        builder.ToTable("work_order_pole_target", table =>
        {
            table.HasCheckConstraint("ck_work_order_pole_target_position", "position >= 0");
            // A report is all four values or none — each named, so a NULL operand cannot let a half report through.
            table.HasCheckConstraint("ck_work_order_pole_target_report_complete", """
                (reported_lat IS NULL AND reported_lng IS NULL AND reported_accuracy_m IS NULL AND reported_captured_at IS NULL)
                OR (reported_lat IS NOT NULL AND reported_lng IS NOT NULL AND reported_accuracy_m IS NOT NULL AND reported_captured_at IS NOT NULL)
                """);
            // Measured floats: NaN sorts above every number in Postgres, so each bound is spelled out (migrations.md).
            table.HasCheckConstraint("ck_work_order_pole_target_lat", "reported_lat IS NULL OR (reported_lat >= -90 AND reported_lat <= 90 AND reported_lat <> 'NaN'::float8)");
            table.HasCheckConstraint("ck_work_order_pole_target_lng", "reported_lng IS NULL OR (reported_lng >= -180 AND reported_lng <= 180 AND reported_lng <> 'NaN'::float8)");
            table.HasCheckConstraint("ck_work_order_pole_target_accuracy", "reported_accuracy_m IS NULL OR (reported_accuracy_m >= 0 AND reported_accuracy_m <> 'NaN'::float8 AND reported_accuracy_m <> 'Infinity'::float8)");
        });
        builder.HasKey(x => new { x.WorkOrderId, x.Position });
        builder.HasIndex(x => new { x.WorkOrderId, x.PoleId }).IsUnique();
        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
        builder.HasIndex(x => x.CommuneId);
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        // Same-commune pole FK is DB-only (migration), as for pole photos: Pole.CommuneId stays mutable in EF.
        builder.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.PoleId);
        // PG-3: one open pole_gps_update order per pole. The service recognises the conflict by this name.
        builder.HasIndex(x => x.PoleId, ActiveIndex).HasDatabaseName(ActiveIndex).IsUnique().HasFilter("released_at IS NULL");
    }
}

public sealed class WorkOrderAttachmentConfiguration : IEntityTypeConfiguration<WorkOrderAttachment>
{
    public void Configure(EntityTypeBuilder<WorkOrderAttachment> builder)
    {
        builder.ToTable("work_order_attachment", table =>
        {
            table.HasCheckConstraint("ck_work_order_attachment_bytes", "byte_count > 0");
            table.HasCheckConstraint("ck_work_order_attachment_file_name", "btrim(file_name) <> '' AND char_length(file_name) <= 200");
            table.HasCheckConstraint("ck_work_order_attachment_key_not_blank", "btrim(object_key) <> ''");
            table.HasCheckConstraint("ck_work_order_attachment_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_work_order_attachment_removed_together", "(removed_at IS NULL) = (removed_by IS NULL)");
            table.HasCheckConstraint("ck_work_order_attachment_removed_after_upload", "removed_at IS NULL OR removed_at >= uploaded_at");
        });
        builder.HasKey(x => x.AttachmentId);
        builder.Property(x => x.AttachmentId).HasPrefixedId(PrefixedIds.WorkOrderAttachment);
        builder.Property(x => x.UploadedAt).HasDefaultValueSql("now()");
        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
        builder.HasIndex(x => x.CommuneId);
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.WorkOrderId, x.CommuneId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RemovedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.UploadedBy);
        builder.HasIndex(x => x.RemovedBy);
    }
}

public sealed class RepairEvidenceConfiguration : IEntityTypeConfiguration<RepairEvidence>
{
    public void Configure(EntityTypeBuilder<RepairEvidence> builder)
    {
        builder.ToTable("repair_evidence", table =>
        {
            table.HasCheckConstraint("ck_repair_evidence_lat", "lat >= -90 AND lat <= 90");
            table.HasCheckConstraint("ck_repair_evidence_lng", "lng >= -180 AND lng <= 180");
            table.HasCheckConstraint("ck_repair_evidence_bytes", "byte_count > 0 AND thumbnail_bytes > 0");
            table.HasCheckConstraint("ck_repair_evidence_keys_not_blank", "btrim(object_key) <> '' AND btrim(thumbnail_key) <> ''");
            // One parent, never two and never none; a fault photo is what was SEEN, so it is always an observation.
            table.HasCheckConstraint("ck_repair_evidence_one_parent", "(work_order_id IS NULL) <> (fault_id IS NULL)");
            table.HasCheckConstraint("ck_repair_evidence_fault_observation", "fault_id IS NULL OR kind = 'observation'");
            table.HasCheckConstraint("ck_repair_evidence_fault_no_pole", "fault_id IS NULL OR pole_id IS NULL");
        });
        builder.HasKey(x => x.EvidenceId);
        builder.Property(x => x.EvidenceId).HasPrefixedId(PrefixedIds.RepairEvidence);
        builder.Property(x => x.UploadedAt).HasDefaultValueSql("now()");
        builder.HasContractEnum(x => x.Kind);
        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
        builder.HasIndex(x => x.CommuneId);
        // Composite pole/commune FK is DB-only: do not freeze Pole.CommuneId as an EF alternate key.
        builder.HasIndex(x => x.PoleId);
        // Same commune as the order, by schema (khuôn O-7), so a photo can never sit in another commune.
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.WorkOrderId, x.CommuneId });
        // Same commune as the fault, by schema; Fault.CommuneId is already an alternate key for work_order_fault.
        builder.HasOne<Fault>().WithMany().HasForeignKey(x => new { x.FaultId, x.CommuneId })
            .HasPrincipalKey(x => new { x.FaultId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.FaultId, x.CommuneId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        // The partial unique index below leads with uploaded_by, and EF then skips the FK index by convention
        // — but a partial index does not cover rows without a client_op_id. Declared, not assumed.
        builder.HasIndex(x => x.UploadedBy);
        builder.HasIndex(x => new { x.UploadedBy, x.ClientOpId }).IsUnique().HasFilter("client_op_id IS NOT NULL")
            .HasDatabaseName("ux_repair_evidence_uploaded_by_client_op_id");
    }
}
