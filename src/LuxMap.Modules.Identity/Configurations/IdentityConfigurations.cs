using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Identity.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_user");
        builder.HasKey(user => user.UserId);

        builder.Property(user => user.UserId).HasPrefixedId(PrefixedIds.AppUser);
        builder.Property(user => user.Username).HasColumnType("text").IsRequired();
        builder.Property(user => user.Email).HasColumnType("text").IsRequired();
        builder.Property(user => user.FullName).HasColumnType("text").IsRequired();
        builder.Property(user => user.PasswordHash).HasColumnType("text").IsRequired();
        builder.Property(user => user.PasswordAlgorithm).HasColumnType("text").IsRequired();
        builder.Property(user => user.IsLocked).HasDefaultValue(false);
        builder.Property(user => user.HasSystemWideScope).HasDefaultValue(false);
        builder.Property(user => user.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(user => user.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(user => user.Role);

        builder.HasIndex(user => user.Username).IsUnique();
        builder.HasIndex(user => user.Email).IsUnique();
    }
}

public sealed class AppUserCommuneConfiguration : IEntityTypeConfiguration<AppUserCommune>
{
    public void Configure(EntityTypeBuilder<AppUserCommune> builder)
    {
        builder.ToTable("app_user_commune");
        builder.HasKey(assignment => new { assignment.UserId, assignment.CommuneId });

        builder.Property(assignment => assignment.UserId).HasColumnType("text");
        builder.HasCommuneReference(assignment => assignment.CommuneId);
        builder.Property(assignment => assignment.AssignedAt).HasDefaultValueSql("now()");

        builder.HasOne(assignment => assignment.User)
            .WithMany(user => user.CommuneAssignments)
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a commune that still has assignees must be blocked: losing the assignment loses
        // the authorization trail.
        builder.HasOne(assignment => assignment.Commune)
            .WithMany()
            .HasForeignKey(assignment => assignment.CommuneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(assignment => assignment.CommuneId);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_token");
        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id).ValueGeneratedOnAdd();
        builder.Property(token => token.UserId).HasColumnType("text").IsRequired();
        builder.Property(token => token.TokenHash).HasColumnType("text").IsRequired();
        builder.Property(token => token.CreatedAt).HasDefaultValueSql("now()");
        builder.HasContractEnum(token => token.RevokedReason);

        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(token => token.ReplacedByToken)
            .WithMany()
            .HasForeignKey(token => token.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        // Refresh lookups go straight through this index.
        builder.HasIndex(token => token.TokenHash).IsUnique();

        // Expired-token cleanup (BE-07) scans on this column.
        builder.HasIndex(token => token.ExpiresAt);

        // Revoking an entire chain on replay detection is one UPDATE over this index.
        builder.HasIndex(token => token.ChainId);
    }
}
