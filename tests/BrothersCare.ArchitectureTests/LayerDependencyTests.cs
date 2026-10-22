using System.Reflection;
using BrothersCare.Domain;
using ApplicationDependencyInjection = BrothersCare.Application.DependencyInjection;
using InfrastructureDependencyInjection = BrothersCare.Infrastructure.DependencyInjection;

namespace BrothersCare.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly string[] SolutionAssemblies =
    [
        "BrothersCare.Domain",
        "BrothersCare.Application",
        "BrothersCare.Infrastructure",
        "BrothersCare.Api",
    ];

    [Fact]
    public void DomainDoesNotReferenceOuterLayers() =>
        AssertDoesNotReference(
            typeof(DomainAssembly).Assembly,
            "BrothersCare.Application",
            "BrothersCare.Infrastructure",
            "BrothersCare.Api");

    [Fact]
    public void ApplicationDoesNotReferenceOuterLayers() =>
        AssertDoesNotReference(
            typeof(ApplicationDependencyInjection).Assembly,
            "BrothersCare.Infrastructure",
            "BrothersCare.Api");

    [Fact]
    public void InfrastructureDoesNotReferenceApi() =>
        AssertDoesNotReference(typeof(InfrastructureDependencyInjection).Assembly, "BrothersCare.Api");

    [Fact]
    public void ApiDoesNotReferenceDomainDirectly() =>
        AssertDoesNotReference(typeof(Program).Assembly, "BrothersCare.Domain");

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbidden)
    {
        string[] actual = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && SolutionAssemblies.Contains(name, StringComparer.Ordinal))
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(actual, forbidden.Contains);
    }
}
