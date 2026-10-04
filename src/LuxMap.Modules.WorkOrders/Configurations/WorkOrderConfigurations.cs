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
        });
        builder.HasKey(x => x.EvidenceId);
        builder.Property(x => x.EvidenceId).HasPrefixedId(PrefixedIds.RepairEvidence);
        builder.Property(x => x.UploadedAt).HasDefaultValueSql("now()");
        builder.HasContractEnum(x => x.Kind);
        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
        builder.HasIndex(x => x.CommuneId);
        // Same commune as the order, by schema (khuôn O-7), so a photo can never sit in another commune.
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.WorkOrderId, x.CommuneId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        // The partial unique index below leads with uploaded_by, and EF then skips the FK index by convention
        // — but a partial index does not cover rows without a client_op_id. Declared, not assumed.
        builder.HasIndex(x => x.UploadedBy);
        builder.HasIndex(x => new { x.UploadedBy, x.ClientOpId }).IsUnique().HasFilter("client_op_id IS NOT NULL")
            .HasDatabaseName("ux_repair_evidence_uploaded_by_client_op_id");
    }
}
