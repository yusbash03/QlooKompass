using System.Reflection;
using KultureCompass.Domain.Common;
using NetArchTest.Rules;
using Shouldly;

namespace KultureCompass.ArchitectureTests;

public class DependencyDirectionTests
{
    private static readonly Assembly DomainAssembly = typeof(DomainException).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(KultureCompass.Application.Abstractions.IBusinessProfileStore).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_other_layers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "KultureCompass.Application",
                "KultureCompass.Infrastructure",
                "KultureCompass.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureSummary(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "KultureCompass.Infrastructure",
                "KultureCompass.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureSummary(result));
    }

    [Fact]
    public void Application_should_not_depend_on_ef_core_or_aspnetcore_http_types()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore.Http",
                "System.Net.Http")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureSummary(result));
    }

    private static string FailureSummary(TestResult result) =>
        result.FailingTypes is null
            ? "Unknown architecture violation."
            : "Violating types: " + string.Join(", ", result.FailingTypes.Select(t => t.FullName));
}
