using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.WorkOrders.Configurations;

public sealed class FeederRepairPeriodConfiguration : IEntityTypeConfiguration<FeederRepairPeriod>
{
    public void Configure(EntityTypeBuilder<FeederRepairPeriod> builder)
    {
        builder.ToTable("feeder_repair_period", table => table.HasCheckConstraint(
            "ck_feeder_repair_period_time", "ended_at IS NULL OR ended_at >= started_at"));
        builder.HasKey(x => x.Id);
        builder.HasCommuneScope();
        builder.HasCommuneReference(x => x.CommuneId);
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Feeder>().WithMany().HasForeignKey(x => new { x.FeederId, x.CommuneId })
            .HasPrincipalKey(x => new { x.FeederId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.FeederId).IsUnique().HasFilter("ended_at IS NULL");
        builder.HasIndex(x => new { x.FeederId, x.StartedAt });
        builder.HasIndex(x => new { x.WorkOrderId, x.StartedAt });
    }
}
