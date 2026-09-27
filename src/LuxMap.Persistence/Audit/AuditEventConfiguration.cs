using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Audit;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_event", table =>
        {
            table.HasCheckConstraint("ck_audit_event_actor",
                "(actor_kind = 'user') = (actor_user_id IS NOT NULL) AND (actor_user_id IS NULL) = (actor_role IS NULL)");
            table.HasCheckConstraint("ck_audit_event_has_state",
                "before_state IS NOT NULL OR after_state IS NOT NULL");
            table.HasCheckConstraint("ck_audit_event_before_state_object",
                "before_state IS NULL OR jsonb_typeof(before_state) = 'object'");
            table.HasCheckConstraint("ck_audit_event_after_state_object",
                "after_state IS NULL OR jsonb_typeof(after_state) = 'object'");
        });
        builder.HasKey(audit => audit.AuditId);
        builder.Property(audit => audit.AuditId).UseIdentityAlwaysColumn();
        builder.Property(audit => audit.OccurredAt).HasDefaultValueSql("now()");
        builder.Property(audit => audit.ActorUserId).HasColumnType("text");
        builder.Property(audit => audit.EntityId).HasColumnType("text").IsRequired();
        builder.Property(audit => audit.CorrelationId).HasColumnType("text").IsRequired();
        builder.Property(audit => audit.Note).HasColumnType("text");
        builder.Property(audit => audit.BeforeState).HasColumnType("jsonb");
        builder.Property(audit => audit.AfterState).HasColumnType("jsonb");
        builder.HasCommuneReference(audit => audit.CommuneId);
        builder.HasCommuneScope();
        builder.HasContractEnum(audit => audit.ActorKind);
        builder.HasContractEnum(audit => audit.ActorRole);
        builder.HasContractEnum(audit => audit.EntityType);
        builder.HasContractEnum(audit => audit.Action);
        builder.HasIndex(audit => new { audit.EntityType, audit.EntityId, audit.AuditId })
            .HasDatabaseName("ix_audit_event_entity");
        builder.HasIndex(audit => audit.CommuneId);
        builder.HasIndex(audit => audit.ActorUserId);
    }
}
