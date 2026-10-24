using KultureCompass.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KultureCompass.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(t => t.RevokedReason).HasMaxLength(30);
        builder.Property(t => t.CreatedByIpHash).HasMaxLength(64).IsFixedLength();
        builder.Property(t => t.UserAgent).HasMaxLength(200);
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.AbsoluteExpiresAt).IsRequired();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        builder.HasIndex(t => new { t.UserId, t.FamilyId }).HasDatabaseName("ix_refresh_tokens_user_family");
        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("ix_refresh_tokens_expires");
    }
}
