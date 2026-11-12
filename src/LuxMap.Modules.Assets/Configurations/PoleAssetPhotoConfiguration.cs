using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Assets.Configurations;

public sealed class PoleAssetPhotoConfiguration : IEntityTypeConfiguration<PoleAssetPhoto>
{
    public void Configure(EntityTypeBuilder<PoleAssetPhoto> b)
    {
        b.ToTable("pole_asset_photo", t =>
        {
            t.HasCheckConstraint("ck_pole_asset_photo_sizes", "byte_count BETWEEN 1 AND 16777216 AND thumbnail_bytes > 0 AND width > 0 AND height > 0 AND width::bigint * height <= 40000000");
            t.HasCheckConstraint("ck_pole_asset_photo_keys", "object_key = 'original/' || photo_id || '.jpg' AND thumbnail_key = 'thumb/' || photo_id || '.jpg'");
            t.HasCheckConstraint("ck_pole_asset_photo_hash", "request_hash ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_pole_asset_photo_operation", "client_op_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            t.HasCheckConstraint("ck_pole_asset_photo_deleted", "(deleted_at IS NULL) = (deleted_by IS NULL)");
        });
        b.HasKey(x => x.PhotoId);
        b.Property(x => x.PhotoId).HasPrefixedId(PrefixedIds.PoleAssetPhoto);
        b.HasContractEnum(x => x.DataSource);
        b.HasCommuneScope();
        b.HasCommuneReference(x => x.CommuneId);
        b.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.DeletedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex("CommuneId");
        b.HasIndex(x => new { x.PoleId, x.CreatedAt, x.PhotoId }).HasDatabaseName("ix_pole_asset_photo_parent_order");
        b.HasIndex(x => new { x.UploadedBy, x.ClientOpId }).IsUnique().HasDatabaseName("ux_pole_asset_photo_user_operation");
    }
}
