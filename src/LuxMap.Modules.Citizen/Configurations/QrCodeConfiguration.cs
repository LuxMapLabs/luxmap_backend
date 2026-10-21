using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Citizen.Configurations;

public sealed class QrCodeConfiguration : IEntityTypeConfiguration<QrCode>
{
    public const string CodeIndex = "ux_qr_code_code";
    public const string ActivePerPoleIndex = "ux_qr_code_pole_id_active";

    public void Configure(EntityTypeBuilder<QrCode> builder)
    {
        builder.ToTable("qr_code", table =>
        {
            table.HasCheckConstraint("ck_qr_code_code_format", "code ~ '^[0-9A-Za-z]{16}$'");
            table.HasCheckConstraint("ck_qr_code_revoked_together", "(revoked_at IS NULL) = (revoked_by IS NULL)");
            table.HasCheckConstraint("ck_qr_code_revoked_after_created", "revoked_at IS NULL OR revoked_at >= created_at");
        });

        builder.HasKey(qr => qr.QrId);
        builder.Property(qr => qr.QrId).HasPrefixedId(PrefixedIds.QrCode);
        builder.Property(qr => qr.Code).HasColumnType("text").IsRequired();
        builder.Property(qr => qr.PoleId).HasColumnType("text").IsRequired();
        builder.Property(qr => qr.CreatedBy).HasColumnType("text").IsRequired();
        builder.Property(qr => qr.RevokedBy).HasColumnType("text");

        builder.HasIndex(qr => qr.Code).IsUnique().HasDatabaseName(CodeIndex);

        // CQ-10: at most one code in use per pole. The service serialises issuing on the pole row; this is the last line.
        builder.HasIndex(qr => qr.PoleId, ActivePerPoleIndex).IsUnique().HasFilter("revoked_at IS NULL").HasDatabaseName(ActivePerPoleIndex);

        // 🔴 The partial index above leads with pole_id, so EF's convention would skip the FK index on it — and the partial
        // one does not serve revoked rows (partial-index rule in docs/agents/traps/migrations.md). Declared explicitly, under a distinct name —
        // both indexes on one column need a MODEL name too, or EF treats the second HasIndex as the first and overwrites it.
        builder.HasIndex(qr => qr.PoleId, "ix_qr_code_pole_id").HasDatabaseName("ix_qr_code_pole_id");
        builder.HasIndex(qr => qr.CreatedBy);
        builder.HasIndex(qr => qr.RevokedBy);

        // A code is a fact about a pole and a person: never cascade, never orphan.
        builder.HasOne<Pole>().WithMany().HasForeignKey(qr => qr.PoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(qr => qr.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(qr => qr.RevokedBy).OnDelete(DeleteBehavior.Restrict);

        // 🔴 Looks redundant and is load-bearing (the ak_feeder_feeder_id_commune_id pattern, O-7): the target of the composite key
        // from citizen_submission that keeps a send on the SAME pole and commune as the code it used (Codex review P2b #8). A code
        // never changes pole or commune, so making them key properties costs nothing.
        builder.HasAlternateKey(qr => new { qr.QrId, qr.PoleId, qr.CommuneId });

        builder.HasCommuneScope();
        builder.HasIndex(qr => qr.CommuneId);
        builder.HasCommuneReference(qr => qr.CommuneId);
    }
}
