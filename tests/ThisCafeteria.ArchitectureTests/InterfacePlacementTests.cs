using System.Reflection;
using FluentAssertions;

namespace ThisCafeteria.ArchitectureTests;

/// <summary>
/// Invariants 2 and 3 of <c>docs/clean-architecture-plan.md</c>: an abstraction declared in
/// Application is implemented in Application or Infrastructure - never in a host.
///
/// These rules reflect over compiled assemblies rather than source text, because "implements
/// interface I" is a type-system fact that only the loaded metadata knows reliably.
/// </summary>
public sealed class InterfacePlacementTests
{
    [Fact]
    public void ApplicationInterfacesAreImplementedInApplicationOrInfrastructure()
    {
        var applicationInterfaces = LoadTypes("ThisCafeteria.Application")
            .Where(type => type.IsInterface)
            .ToHashSet();

        applicationInterfaces.Should().NotBeEmpty("the Application assembly should declare abstractions");

        var offenders = new[] { "ThisCafeteria.Web", "ThisCafeteria.Worker" }
            .SelectMany(LoadTypes)
            .Where(IsConcreteClass)
            .Where(type => type.GetInterfaces().Any(applicationInterfaces.Contains))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        offenders.Should().BeEquivalentTo(
            KnownViolations.MisplacedApplicationImplementations,
            "a host project may consume Application abstractions but must not supply them - "
            + "implementations belong in Infrastructure, where the Worker can reuse them too");
    }

    /// <summary>
    /// Infrastructure exists to implement Application's abstractions, so it must never be the
    /// place where a new abstraction is invented for the layers above to depend on. Asserted with
    /// no allowlist: today Infrastructure declares only infrastructure-internal interfaces
    /// (IEmailSender, IS3StorageService, ISqsMessagePublisher), and none of them leak upward.
    /// </summary>
    [Fact]
    public void HostsDoNotDependOnInfrastructureOnlyAbstractions()
    {
        var infrastructureInterfaces = LoadTypes("ThisCafeteria.Infrastructure")
            .Where(type => type.IsInterface)
            .ToHashSet();

        var applicationTypes = LoadTypes("ThisCafeteria.Application");

        var offenders = applicationTypes
            .Where(IsConcreteClass)
            .Where(type => type.GetInterfaces().Any(infrastructureInterfaces.Contains))
            .Select(type => type.FullName!)
            .ToList();

        offenders.Should().BeEmpty(
            "Application must not implement interfaces declared in Infrastructure - that inverts "
            + "the dependency rule");
    }

    private static bool IsConcreteClass(Type type) =>
        type is { IsClass: true, IsAbstract: false };

    /// <summary>
    /// Loads an assembly by simple name and returns the types it could surface. Types that fail to
    /// load (a missing optional dependency, for instance) are skipped rather than failing the run,
    /// since a partial view is still enough to catch a misplaced implementation.
    /// </summary>
    private static Type[] LoadTypes(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>().ToArray();
        }
    }
}
