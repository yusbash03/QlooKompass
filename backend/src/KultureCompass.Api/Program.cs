using System.Text.Json.Serialization;
using KultureCompass.Api.DevTools;
using KultureCompass.Api.Endpoints;
using KultureCompass.Api.ErrorHandling;
using KultureCompass.Api.Middleware;
using KultureCompass.Infrastructure;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Persistence;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter()));

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

    builder.Services.AddOptions<AppOptions>()
        .Bind(builder.Configuration.GetSection(AppOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<FeatureOptions>()
        .Bind(builder.Configuration.GetSection(FeatureOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<GenerationSettings>()
        .Bind(builder.Configuration.GetSection(GenerationSettings.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<LlmOptions>()
        .Bind(builder.Configuration.GetSection(LlmOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    var appOptions = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();
    const string corsPolicyName = "SiblingSubdomain";
    builder.Services.AddCors(options => options.AddPolicy(corsPolicyName, policy =>
    {
        var origins = new[] { appOptions.BaseUrl }.Concat(appOptions.AdditionalOrigins)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToArray();
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins);
        }
        policy.WithMethods("GET", "POST", "PUT", "PATCH")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "If-Match", "X-Requested-With")
            .WithExposedHeaders("ETag", "Location", "Retry-After")
            .AllowCredentials();
    }));

    builder.Services.AddOpenApi();

    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
    builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres");

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddQlooClient(builder.Configuration);

    var app = builder.Build();

    if (args.Contains("--migrate"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        return;
    }

    if (args.Contains("research"))
    {
        var profileJsonIndex = Array.IndexOf(args, "--profile-json");
        if (profileJsonIndex < 0 || profileJsonIndex + 1 >= args.Length)
        {
            Console.Error.WriteLine("Usage: dotnet run -- research --profile-json <file>");
            return;
        }

        using var scope = app.Services.CreateScope();
        await ResearchConsoleCommand.RunAsync(scope.ServiceProvider, args[profileJsonIndex + 1], CancellationToken.None);
        return;
    }

    app.UseExceptionHandler();
    app.UseSecurityHeaders();
    app.UseCors(corsPolicyName);

    app.MapOpenApi();
    if (app.Environment.IsDevelopment())
    {
        app.MapScalarApiReference();
    }

    app.MapSystemEndpoints();

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

namespace KultureCompass.Api
{
    /// <summary>Marker partial Program class so WebApplicationFactory&lt;Program&gt; can target this entry point in tests.</summary>
    public partial class Program;
}
