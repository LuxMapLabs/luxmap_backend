using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.WorkOrders.Configurations;

public sealed class WorkOrderPhotoCopyConfiguration : IEntityTypeConfiguration<WorkOrderPhotoCopy>
{
    public const string PrimaryKey = "pk_work_order_photo_copy";

    public void Configure(EntityTypeBuilder<WorkOrderPhotoCopy> builder)
    {
        builder.ToTable("work_order_photo_copy", table =>
            table.HasCheckConstraint("ck_work_order_photo_copy_photo", "(outcome = 'copied') = (photo_id IS NOT NULL)"));
        builder.HasKey(x => x.EvidenceId).HasName(PrimaryKey);
        builder.HasContractEnum(x => x.Outcome);

        builder.HasOne<RepairEvidence>().WithMany().HasForeignKey(x => x.EvidenceId).OnDelete(DeleteBehavior.Restrict);
        // Composite with commune_id (work_order's alternate key): the row lives in its order's commune.
        builder.HasOne<WorkOrder>().WithMany()
            .HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_work_order_photo_copy_work_order");
        builder.HasIndex(x => new { x.WorkOrderId, x.CommuneId });
        builder.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.PoleId);
        builder.HasOne<PoleAssetPhoto>().WithMany().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.PhotoId);
        builder.HasIndex(x => x.CommuneId);

        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
    }
}
