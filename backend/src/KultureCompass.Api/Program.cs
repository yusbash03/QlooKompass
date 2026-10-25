using System.Text.Json.Serialization;
using KultureCompass.Api.Auth;
using KultureCompass.Api.DevTools;
using KultureCompass.Api.Endpoints;
using KultureCompass.Api.ErrorHandling;
using KultureCompass.Api.Http;
using KultureCompass.Api.Middleware;
using KultureCompass.Application;
using KultureCompass.Infrastructure;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Generation;
using KultureCompass.Infrastructure.Identity;
using KultureCompass.Infrastructure.Llm;
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

    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    {
        // Every problem response — handler, framework (JWT challenge, binding, rate limiter) or exception — carries
        // the contract's code + traceId members.
        context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
        if (ResultHttpExtensions.DefaultCodeFor(context.ProblemDetails.Status) is { } code)
            context.ProblemDetails.Extensions.TryAdd("code", code);
    });
    builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

    // Minimal APIs throw on bad request bodies in Development by default, which would surface as 500. Always
    // return the 400 Problem Details instead.
    builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);

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
    var isProduction = builder.Environment.IsProduction();
    builder.Services.AddOptions<LlmOptions>()
        .Bind(builder.Configuration.GetSection(LlmOptions.SectionName))
        .ValidateDataAnnotations()
        .Validate(o => o.IsMock || !string.IsNullOrWhiteSpace(o.ApiKey),
            "Llm:ApiKey is required when Llm:Provider is not Mock (set it via user-secrets or env, never appsettings).")
        .Validate(o => !isProduction || !o.LogPrompts, "Llm:LogPrompts must be false in Production (02-security §7).")
        .ValidateOnStart();
    builder.Services.AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
        .ValidateDataAnnotations()
        .Validate(o => o.HasValidSigningKey(),
            $"Jwt:SigningKey must be base64 encoding at least {JwtOptions.MinSigningKeyBytes} random bytes (openssl rand -base64 48).")
        .Validate(o => !isProduction || !JwtOptions.KnownPlaceholderKeys.Contains(o.SigningKey),
            "Jwt:SigningKey is a committed placeholder; set a real key via env/secret store in Production.")
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

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddQlooClient(builder.Configuration);
    builder.Services.AddLlmClient(builder.Configuration);
    builder.Services.AddGenerationPipeline();
    builder.Services.AddKultureCompassAuth(builder.Configuration);

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

    if (!app.Environment.IsProduction())
    {
        await SeedDemoUserAsync(app);
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSecurityHeaders();
    app.UseCors(corsPolicyName);
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapOpenApi().AllowAnonymous();
    if (app.Environment.IsDevelopment())
    {
        app.MapScalarApiReference().AllowAnonymous();
    }

    app.MapSystemEndpoints();
    app.MapAuthEndpoints();
    app.MapBusinessProfileEndpoints();
    app.MapGrowthPlanEndpoints();

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

static async Task SeedDemoUserAsync(WebApplication app)
{
    var email = app.Configuration["Demo:UserEmail"];
    var password = app.Configuration["Demo:UserPassword"];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return;

    using var scope = app.Services.CreateScope();
    var created = await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().SeedAsync(email, password, CancellationToken.None);
    Log.Information("Demo user seeding: {Outcome}", created ? "created" : "already exists");
}

namespace KultureCompass.Api
{
    /// <summary>Marker partial Program class so WebApplicationFactory&lt;Program&gt; can target this entry point in tests.</summary>
    public partial class Program;
}
