using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Notifications.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification", table =>
        {
            table.HasCheckConstraint("ck_notification_title_length", "char_length(title) BETWEEN 1 AND 200");
            table.HasCheckConstraint("ck_notification_body_length", "char_length(body) BETWEEN 1 AND 1000");
        });
        builder.HasKey(notice => notice.NotificationId);
        builder.Property(notice => notice.NotificationId).HasPrefixedId(PrefixedIds.Notification);
        builder.Property(notice => notice.RecipientUserId).HasColumnType("text");
        builder.Property(notice => notice.Title).HasColumnType("text");
        builder.Property(notice => notice.Body).HasColumnType("text");
        builder.Property(notice => notice.EntityId).HasColumnType("text");
        builder.HasContractEnum(notice => notice.Type);
        builder.HasContractEnum(notice => notice.EntityType);
        builder.HasCommuneReference(notice => notice.CommuneId);
        builder.HasCommuneScope();

        // A notice is someone's record of an event: deleting the account must not silently erase it.
        builder.HasOne<AppUser>().WithMany().HasForeignKey(notice => notice.RecipientUserId).OnDelete(DeleteBehavior.Restrict);

        // The list (newest first) and the unread badge, per person. The partial index serves only the
        // count; the full index still covers every row, and commune_id keeps its own index (see the
        // partial-index rule in CLAUDE.md).
        builder.HasIndex(notice => new { notice.RecipientUserId, notice.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_notification_recipient_created");
        builder.HasIndex(notice => notice.RecipientUserId)
            .HasFilter("read_at IS NULL")
            .HasDatabaseName("ix_notification_recipient_unread");
        builder.HasIndex(notice => notice.CommuneId);
    }
}
