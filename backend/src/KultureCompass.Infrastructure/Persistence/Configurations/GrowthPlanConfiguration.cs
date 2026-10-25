using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Experiments;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KultureCompass.Infrastructure.Persistence.Configurations;

public sealed class GrowthPlanConfiguration : IEntityTypeConfiguration<GrowthPlan>
{
    public void Configure(EntityTypeBuilder<GrowthPlan> builder)
    {
        builder.ToTable("growth_plans");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.OwnerId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.Stage).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(p => p.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(p => p.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(p => p.SchemaVersion).HasMaxLength(10).IsRequired();
        builder.Property(p => p.PromptVersion).HasMaxLength(20).IsRequired();
        builder.Property(p => p.QlooMode).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(p => p.LlmProvider).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.LlmModel).HasMaxLength(80);
        builder.Property(p => p.IsMock).IsRequired();
        builder.Property(p => p.QlooCallCount).HasDefaultValue(0).IsRequired();
        builder.Property(p => p.LlmCallCount).HasDefaultValue(0).IsRequired();
        builder.Property(p => p.InterpretationJson).HasColumnType("jsonb").HasColumnName("interpretation");
        builder.Property(p => p.ResearchPlanJson).HasColumnType("jsonb").HasColumnName("research_plan");
        builder.Property(p => p.LlmUsageJson).HasColumnType("jsonb").HasColumnName("llm_usage");
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.StartedAt);
        builder.Property(p => p.CompletedAt);
        builder.Property(p => p.UpdatedAt).IsRequired();

        builder.Property(p => p.BriefSnapshot)
            .HasConversion(JsonValueConverters.For<BriefSnapshot>())
            .HasColumnType("jsonb")
            .HasColumnName("brief_snapshot")
            .IsRequired();

        builder.Property(p => p.Options)
            .HasConversion(JsonValueConverters.For<GenerationOptions>())
            .HasColumnType("jsonb")
            .HasColumnName("options")
            .IsRequired();

        builder.Property(p => p.Summary)
            .HasConversion(JsonValueConverters.ForNullableClass<GrowthPlanSummary>())
            .HasColumnType("jsonb")
            .HasColumnName("content_summary");

        builder.Property(p => p.Limitations)
            .HasConversion(JsonValueConverters.For<IReadOnlyList<string>>())
            .HasColumnType("jsonb")
            .HasColumnName("content_limitations")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<string>());

        builder.Property(p => p.UnlinkedMentions)
            .HasConversion(JsonValueConverters.For<IReadOnlyList<UnlinkedMention>>())
            .HasColumnType("jsonb")
            .HasColumnName("content_unlinked_mentions")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<UnlinkedMention>());

        builder.Property(p => p.StageLog)
            .HasConversion(JsonValueConverters.For<IReadOnlyList<StageLogEntry>>())
            .HasColumnType("jsonb")
            .HasColumnName("stage_log")
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<StageLogEntry>());

        builder.OwnsOne(p => p.Error, error =>
        {
            error.Property(e => e.Code).HasConversion<string>().HasColumnName("error_code").HasMaxLength(40);
            error.Property(e => e.Message).HasColumnName("error_message").HasMaxLength(300);
        });

        ConfigureEvidence(builder);
        ConfigureRecommendations(builder);
        ConfigureExperiments(builder);

        builder.HasIndex(p => new { p.OwnerId, p.IdempotencyKey }).IsUnique().HasDatabaseName("ux_growth_plans_owner_idem");
        builder.HasIndex(p => new { p.OwnerId, p.CreatedAt }).HasDatabaseName("ix_growth_plans_owner_created");
        builder.HasIndex(p => new { p.BusinessProfileId, p.CreatedAt }).HasDatabaseName("ix_growth_plans_profile_created");
        builder.HasIndex(p => p.Status)
            .HasDatabaseName("ix_growth_plans_status_partial")
            .HasFilter("status in ('Queued','Running')");
    }

    private static void ConfigureEvidence(EntityTypeBuilder<GrowthPlan> builder)
    {
        builder.OwnsMany(p => p.Evidence, evidence =>
        {
            evidence.ToTable("growth_plan_evidence");
            evidence.WithOwner().HasForeignKey("growth_plan_id");
            evidence.Property<Guid>("id").ValueGeneratedOnAdd();
            evidence.HasKey("id");

            evidence.Property(e => e.Key).HasColumnName("evidence_key").HasMaxLength(10).IsRequired();
            evidence.Property(e => e.QlooEntityId).HasColumnName("qloo_entity_id").HasMaxLength(100).IsRequired();
            evidence.Property(e => e.Name).HasMaxLength(200).IsRequired();
            evidence.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(60).IsRequired();
            evidence.Property(e => e.Subtype).HasMaxLength(80);
            evidence.Property(e => e.SourceOperation).HasColumnName("source_operation").HasConversion<string>().HasMaxLength(20).IsRequired();
            evidence.Property(e => e.Affinity).HasColumnType("numeric(6,5)");
            evidence.Property(e => e.Popularity).HasColumnType("numeric(6,5)");
            evidence.Property(e => e.AppearedInOperations).HasColumnName("appeared_in_operations").HasDefaultValue(1).IsRequired();
            evidence.Property(e => e.RetrievedAt).HasColumnName("retrieved_at").IsRequired();
            evidence.Property(e => e.Quality).HasConversion<string>().HasMaxLength(10).IsRequired();

            evidence.Property(e => e.SourceSignals)
                .HasConversion(JsonValueConverters.For<IReadOnlyList<string>>())
                .HasColumnType("jsonb").HasColumnName("source_signals").IsRequired()
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<string>());

            evidence.Property(e => e.Tags)
                .HasConversion(JsonValueConverters.For<IReadOnlyList<string>>())
                .HasColumnType("jsonb").HasColumnName("tags").IsRequired()
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<string>());

            evidence.Property(e => e.QualityReasons)
                .HasConversion(JsonValueConverters.For<IReadOnlyList<string>>())
                .HasColumnType("jsonb").HasColumnName("quality_reasons").IsRequired()
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<string>());

            evidence.Property(e => e.Metadata)
                .HasConversion(JsonValueConverters.For<EvidenceMetadata>())
                .HasColumnType("jsonb").HasColumnName("metadata").IsRequired();

            evidence.HasIndex("growth_plan_id", nameof(EvidenceItem.Key)).IsUnique();
            evidence.HasIndex("growth_plan_id", nameof(EvidenceItem.QlooEntityId)).IsUnique();
        });

        builder.Navigation(p => p.Evidence).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureRecommendations(EntityTypeBuilder<GrowthPlan> builder)
    {
        builder.OwnsMany(p => p.Recommendations, rec =>
        {
            rec.ToTable("growth_plan_recommendations");
            rec.WithOwner().HasForeignKey("growth_plan_id");
            rec.Property<Guid>("id").ValueGeneratedOnAdd();
            rec.HasKey("id");

            rec.Property(r => r.Id).HasColumnName("rec_id").HasMaxLength(10).IsRequired();
            rec.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
            rec.Property(r => r.Ordinal).IsRequired();
            rec.Property(r => r.Title).HasMaxLength(200).IsRequired();
            rec.Property(r => r.EvidenceStrength).HasColumnName("evidence_strength").HasConversion<string>().HasMaxLength(10).IsRequired();

            rec.Property(r => r.EvidenceKeys)
                .HasColumnName("evidence_keys")
                .HasConversion(new ValueConverter<IReadOnlyList<string>, string[]>(
                    v => v.ToArray(),
                    v => v.ToList()))
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .Metadata.SetValueComparer(JsonValueConverters.ReadOnlyListComparer<string>());

            rec.Property(r => r.Payload)
                .HasConversion(JsonValueConverters.For<RecommendationPayload>())
                .HasColumnType("jsonb").HasColumnName("payload").IsRequired();

            rec.HasIndex("growth_plan_id", nameof(Recommendation.Id)).IsUnique();
            rec.HasIndex("growth_plan_id", nameof(Recommendation.Kind), nameof(Recommendation.Ordinal));
        });

        builder.Navigation(p => p.Recommendations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureExperiments(EntityTypeBuilder<GrowthPlan> builder)
    {
        builder.OwnsMany(p => p.Experiments, exp =>
        {
            exp.ToTable("marketing_experiments");
            exp.WithOwner().HasForeignKey("growth_plan_id");
            exp.Property<Guid>("id").ValueGeneratedOnAdd();
            exp.HasKey("id");

            exp.Property(e => e.Id).HasColumnName("exp_id").HasMaxLength(10).IsRequired();
            exp.Property(e => e.LinkedRecommendationId).HasColumnName("recommendation_rec_id").HasMaxLength(10).IsRequired();
            exp.Property(e => e.Hypothesis).HasMaxLength(500).IsRequired();
            exp.Property(e => e.Segment).HasMaxLength(300).IsRequired();
            exp.Property(e => e.ProposedCampaign).HasColumnName("proposed_campaign").HasMaxLength(500).IsRequired();
            exp.Property(e => e.PrimaryMetric).HasColumnName("primary_metric").HasMaxLength(200).IsRequired();
            exp.Property(e => e.MeasurementPeriodDays).HasColumnName("measurement_period_days").IsRequired();
            exp.Property(e => e.SuccessThreshold).HasColumnName("success_threshold").HasMaxLength(200).IsRequired();
            exp.Property(e => e.ThresholdIsProposedDefault).HasColumnName("threshold_is_proposed_default").IsRequired();
            exp.Property(e => e.IfSuccess).HasColumnName("if_success").HasMaxLength(300).IsRequired();
            exp.Property(e => e.IfFailure).HasColumnName("if_failure").HasMaxLength(300).IsRequired();
            exp.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(ExperimentStatus.Proposed).IsRequired();
            exp.Property(e => e.ObservedResult).HasColumnName("observed_result").HasMaxLength(200);
            exp.Property(e => e.ResultNotes).HasColumnName("result_notes").HasMaxLength(1000);
            exp.Property(e => e.StartedAt).HasColumnName("started_at");
            exp.Property(e => e.ConcludedAt).HasColumnName("concluded_at");
            exp.Property(e => e.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion().ValueGeneratedOnAddOrUpdate();

            exp.ToTable(t => t.HasCheckConstraint(
                "ck_marketing_experiments_period",
                "measurement_period_days between 1 and 90"));

            exp.HasIndex("growth_plan_id", nameof(MarketingExperiment.Id)).IsUnique();
        });

        builder.Navigation(p => p.Experiments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
