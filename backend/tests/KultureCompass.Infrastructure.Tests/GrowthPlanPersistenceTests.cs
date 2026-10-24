using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public class GrowthPlanPersistenceTests(PostgresFixture fixture)
{
    private static BriefSnapshot Brief() => new(
        "Eko Thread Co.", "Lagos streetwear label", BusinessCategory.Streetwear, null, "Graphic tees",
        "Lagos 18-30", "Lagos, Nigeria", MarketingObjective.Sales, null, null,
        [ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist)], BudgetRange.From100kTo500k);

    [Fact]
    public async Task Unique_owner_idempotency_key_constraint_is_enforced()
    {
        await using var db = fixture.CreateContext();
        var ownerId = Guid.NewGuid().ToString();
        var profileId = Guid.CreateVersion7();
        var idempotencyKey = Guid.NewGuid().ToString();

        var first = GrowthPlan.CreateQueued(ownerId, profileId, idempotencyKey, "hash-1", Brief(), GenerationOptions.Default, DateTimeOffset.UtcNow);
        await db.GrowthPlans.AddAsync(first);
        await db.SaveChangesAsync();

        await using var db2 = fixture.CreateContext();
        var second = GrowthPlan.CreateQueued(ownerId, profileId, idempotencyKey, "hash-2", Brief(), GenerationOptions.Default, DateTimeOffset.UtcNow);
        await db2.GrowthPlans.AddAsync(second);

        await Should.ThrowAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task BriefSnapshot_jsonb_column_round_trips()
    {
        var ownerId = Guid.NewGuid().ToString();
        var profileId = Guid.CreateVersion7();
        var plan = GrowthPlan.CreateQueued(
            ownerId, profileId, Guid.NewGuid().ToString(), "hash-rt", Brief(), GenerationOptions.Default, DateTimeOffset.UtcNow);

        await using (var db = fixture.CreateContext())
        {
            await db.GrowthPlans.AddAsync(plan);
            await db.SaveChangesAsync();
        }

        await using var readDb = fixture.CreateContext();
        var reloaded = await readDb.GrowthPlans.AsNoTracking().FirstAsync(p => p.Id == plan.Id);

        reloaded.BriefSnapshot.BrandName.ShouldBe("Eko Thread Co.");
        reloaded.BriefSnapshot.Category.ShouldBe(BusinessCategory.Streetwear);
        reloaded.BriefSnapshot.ReferenceEntities.Count.ShouldBe(1);
        reloaded.BriefSnapshot.ReferenceEntities[0].Name.ShouldBe("Burna Boy");
        reloaded.BriefSnapshot.ReferenceEntities[0].KindHint.ShouldBe(ReferenceKindHint.Artist);
        reloaded.Options.IncludeCollaborations.ShouldBeTrue();
        reloaded.StageLog.Count.ShouldBe(1);
        reloaded.StageLog[0].Stage.ShouldBe(GenerationStage.Queued);
    }
}
