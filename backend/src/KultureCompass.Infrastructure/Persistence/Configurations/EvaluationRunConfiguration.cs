using KultureCompass.Domain.Evaluations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KultureCompass.Infrastructure.Persistence.Configurations;

public sealed class EvaluationRunConfiguration : IEntityTypeConfiguration<EvaluationRun>
{
    public void Configure(EntityTypeBuilder<EvaluationRun> builder)
    {
        builder.ToTable("evaluation_runs");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OwnerId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.LlmOnlyOutputJson).HasColumnType("jsonb").HasColumnName("llm_only_output");
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.CompletedAt);

        builder.Property(e => e.GroundedMetrics)
            .HasConversion(JsonValueConverters.ForNullableClass<EvaluationMetrics>())
            .HasColumnType("jsonb")
            .HasColumnName("grounded_metrics");

        builder.Property(e => e.LlmOnlyMetrics)
            .HasConversion(JsonValueConverters.ForNullableClass<EvaluationMetrics>())
            .HasColumnType("jsonb")
            .HasColumnName("llm_only_metrics");

        builder.Property(e => e.Notes)
            .HasConversion(JsonValueConverters.For<IReadOnlyList<string>>())
            .HasColumnType("jsonb")
            .HasColumnName("notes")
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<string>());

        builder.OwnsOne(e => e.Error, error =>
        {
            error.Property(x => x.Code).HasConversion<string>().HasColumnName("error_code").HasMaxLength(40);
            error.Property(x => x.Message).HasColumnName("error_message").HasMaxLength(300);
        });

        builder.HasIndex(e => new { e.OwnerId, e.CreatedAt }).HasDatabaseName("ix_evaluation_runs_owner_created");
    }
}
