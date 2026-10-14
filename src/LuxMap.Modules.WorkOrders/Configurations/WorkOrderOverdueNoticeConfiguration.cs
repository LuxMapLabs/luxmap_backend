using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.WorkOrders.Configurations;

public sealed class WorkOrderOverdueNoticeConfiguration : IEntityTypeConfiguration<WorkOrderOverdueNotice>
{
    public const string PrimaryKey = "pk_work_order_overdue_notice";

    public void Configure(EntityTypeBuilder<WorkOrderOverdueNotice> builder)
    {
        builder.ToTable("work_order_overdue_notice");
        builder.HasKey(notice => new { notice.WorkOrderId, notice.DueDate }).HasName(PrimaryKey);
        builder.Property(notice => notice.WorkOrderId).HasColumnType("text");

        // Composite with commune_id (work_order's alternate key): the notice lives in its order's commune.
        builder.HasOne<WorkOrder>().WithMany()
            .HasForeignKey(notice => new { notice.WorkOrderId, notice.CommuneId })
            .HasPrincipalKey(order => new { order.WorkOrderId, order.CommuneId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_work_order_overdue_notice_work_order"); // the generated name passes Postgres' 63-character cut
        builder.HasIndex(notice => new { notice.WorkOrderId, notice.CommuneId });
        builder.HasIndex(notice => notice.CommuneId);

        builder.HasCommuneScope();
        builder.HasCommuneReference(notice => notice.CommuneId);
    }
}
