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
