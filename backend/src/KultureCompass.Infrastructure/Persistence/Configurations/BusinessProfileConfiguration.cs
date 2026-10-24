using KultureCompass.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KultureCompass.Infrastructure.Persistence.Configurations;

public sealed class BusinessProfileConfiguration : IEntityTypeConfiguration<BusinessProfile>
{
    public void Configure(EntityTypeBuilder<BusinessProfile> builder)
    {
        builder.ToTable("business_profiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.OwnerId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.BrandName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.CategoryOther).HasMaxLength(60);
        builder.Property(p => p.ProductsDescription).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.TargetMarket).HasMaxLength(200);
        builder.Property(p => p.PrimaryLocation).HasMaxLength(100);
        builder.Property(p => p.MarketingObjective).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.ObjectiveNotes).HasMaxLength(300);
        builder.Property(p => p.BrandIdentity).HasMaxLength(500);
        builder.Property(p => p.BudgetRange).HasConversion<string>().HasMaxLength(32);

        builder.Property(p => p.ReferenceEntities)
            .HasConversion(JsonValueConverters.For<IReadOnlyList<ReferenceEntity>>())
            .HasColumnType("jsonb")
            .HasColumnName("reference_entities")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<ReferenceEntity>());

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();
        builder.Property(p => p.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion().ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(p => new { p.OwnerId, p.CreatedAt }).HasDatabaseName("ix_business_profiles_owner_created");
    }
}
