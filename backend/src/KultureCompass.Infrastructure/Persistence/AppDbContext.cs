using KultureCompass.Domain.Evaluations;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using KultureCompass.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KultureCompass.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<GrowthPlan> GrowthPlans => Set<GrowthPlan>();
    public DbSet<EvaluationRun> EvaluationRuns => Set<EvaluationRun>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
