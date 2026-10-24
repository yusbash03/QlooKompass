using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KultureCompass.Infrastructure.Persistence;

/// Used only by `dotnet ef` at design time; the running app configures the context via DependencyInjection.cs.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=kulture_compass;Username=kc;Password=design-time-only";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

        return new AppDbContext(optionsBuilder.Options);
    }
}
