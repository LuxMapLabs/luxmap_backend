using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Sync.Entities;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Sync.Configurations;

public sealed class SyncOperationConfiguration : IEntityTypeConfiguration<SyncOperation>
{
    public void Configure(EntityTypeBuilder<SyncOperation> builder)
    {
        builder.ToTable("sync_operation");

        // The key IS the idempotency rule: one client_op_id per person. Two pushes racing on the same
        // operation meet here, and the loser's whole SaveChanges — the step included — rolls back.
        builder.HasKey(operation => new { operation.UserId, operation.ClientOpId });

        builder.Property(operation => operation.UserId).HasColumnType("text");
        builder.Property(operation => operation.EntityId).HasColumnType("text").IsRequired();
        builder.HasContractEnum(operation => operation.OpType);

        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(operation => operation.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
