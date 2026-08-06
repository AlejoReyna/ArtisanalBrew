using FluentAssertions;

namespace ThisCafeteria.ArchitectureTests;

/// <summary>
/// Strict-migration ratchets for host-only concerns. The temporary sets document exactly what
/// remains to move; a new host adapter or Identity leak fails loudly.
/// </summary>
public sealed class HostBoundaryTests
{
    private static readonly string[] IdentityTokens =
    [
        "ThisCafeteria.Infrastructure.Identity",
        "UserManager<",
        "SignInManager<",
    ];

    private static readonly string[] ExternalAdapterTokens =
    [
        "Nethereum",
        "Azure.Messaging.ServiceBus",
        "Microsoft.EntityFrameworkCore",
    ];

    [Fact]
    public void WebDoesNotReferenceInfrastructureIdentityTypes()
    {
        var offenders = SourceFilesWithAnyToken("src/ThisCafeteria.Web", IdentityTokens)
            .Where(path => !KnownViolations.CompositionRoots.Contains(path))
            .ToHashSet(StringComparer.Ordinal);

        offenders.Should().BeEquivalentTo(
            KnownViolations.TemporaryWebIdentityReferences,
            "Web must consume application-facing identity contracts; the listed files are the "
            + "strict-migration work queue");
    }

    [Fact]
    public void WebDoesNotReferenceInfrastructureServicesOutsideCompositionRoot()
    {
        var offenders = SourceFilesWithAnyToken(
                "src/ThisCafeteria.Web",
                ["ThisCafeteria.Infrastructure.Services"])
            .Where(path => !KnownViolations.CompositionRoots.Contains(path))
            .ToHashSet(StringComparer.Ordinal);

        offenders.Should().BeEmpty(
            "Web consumes Application ports; Infrastructure services are known only to Program.cs");
    }

    [Fact]
    public void WorkerDoesNotOwnHostedServiceImplementations()
    {
        var offenders = SourceFilesWithAnyToken("src/ThisCafeteria.Worker", ["BackgroundService", "IHostedService"])
            .Where(path => !KnownViolations.CompositionRoots.Contains(path))
            .ToHashSet(StringComparer.Ordinal);

        offenders.Should().BeEquivalentTo(
            KnownViolations.TemporaryWorkerHostedServices,
            "a strict Worker host only composes services registered by Infrastructure");
    }

    [Fact]
    public void WorkerDoesNotOwnExternalServiceAdapters()
    {
        var offenders = SourceFilesWithAnyToken("src/ThisCafeteria.Worker", ExternalAdapterTokens)
            .Where(path => !KnownViolations.CompositionRoots.Contains(path))
            .ToHashSet(StringComparer.Ordinal);

        offenders.Should().BeEquivalentTo(
            KnownViolations.TemporaryWorkerExternalAdapters,
            "Nethereum, Service Bus, and EF integrations belong to Infrastructure, not the host");
    }

    private static IEnumerable<string> SourceFilesWithAnyToken(string directory, IReadOnlyList<string> tokens) =>
        SolutionFiles.SourceFilesUnder(directory)
            .Where(path => SolutionFiles.ReadCodeLines(path)
                .Any(line => tokens.Any(token => line.Contains(token, StringComparison.Ordinal))));
}
