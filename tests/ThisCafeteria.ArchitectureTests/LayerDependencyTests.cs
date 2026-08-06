using FluentAssertions;

namespace ThisCafeteria.ArchitectureTests;

/// <summary>
/// Invariant 1 of <c>docs/clean-architecture-plan.md</c>: persistence stays behind Infrastructure.
/// </summary>
public sealed class LayerDependencyTests
{
    /// <summary>Tokens that only appear when a file is talking to Entity Framework directly.</summary>
    private static readonly string[] EntityFrameworkTokens =
    [
        "Microsoft.EntityFrameworkCore",
        "AppDbContext",
        "IDbContextFactory",
        "DbContextOptions",
        "DbSet<",
    ];

    private static readonly string[] LayersThatMustNotTouchEntityFramework =
    [
        "src/ThisCafeteria.Domain",
        "src/ThisCafeteria.Application",
        "src/ThisCafeteria.Web",
        "src/ThisCafeteria.Worker",
    ];

    [Fact]
    public void OnlyInfrastructureTouchesEntityFramework()
    {
        var offenders = LayersThatMustNotTouchEntityFramework
            .SelectMany(SolutionFiles.SourceFilesUnder)
            .Where(path => !KnownViolations.CompositionRoots.Contains(path))
            .Where(HasEntityFrameworkUsage)
            .ToHashSet(StringComparer.Ordinal);

        offenders.Should().BeEquivalentTo(
            KnownViolations.DataAccessOutsideInfrastructure.Concat(KnownViolations.TemporaryWorkerDataAccess),
            "the set of files reaching past Infrastructure to Entity Framework must match "
            + "KnownViolations.DataAccessOutsideInfrastructure exactly - add an entry when you "
            + "must, and delete one in the same commit that fixes the file");
    }

    /// <summary>
    /// Domain is the innermost layer and stays free of every external dependency. This one is
    /// asserted with no allowlist at all, because it currently holds and must never regress.
    /// </summary>
    [Fact]
    public void DomainDependsOnNothingOutsideItself()
    {
        var forbidden = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
            "Nethereum",
            "Newtonsoft",
            "FluentValidation",
            "ThisCafeteria.Application",
            "ThisCafeteria.Infrastructure",
            "ThisCafeteria.Web",
        };

        var offenders = SolutionFiles
            .SourceFilesUnder("src/ThisCafeteria.Domain")
            .SelectMany(path => Violations(path, forbidden))
            .ToList();

        offenders.Should().BeEmpty(
            "ThisCafeteria.Domain must stay dependency-free - it has no PackageReference entries "
            + "and nothing should introduce one through a using directive");
    }

    private static bool HasEntityFrameworkUsage(string repoRelativePath) =>
        Violations(repoRelativePath, EntityFrameworkTokens).Count > 0;

    /// <summary>Returns <c>path:line token</c> descriptions for each forbidden token found in code.</summary>
    private static IReadOnlyList<string> Violations(string repoRelativePath, IReadOnlyList<string> forbiddenTokens)
    {
        var lines = SolutionFiles.ReadCodeLines(repoRelativePath);
        var found = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            foreach (var token in forbiddenTokens)
            {
                if (lines[index].Contains(token, StringComparison.Ordinal))
                {
                    found.Add($"{repoRelativePath}:{index + 1} references {token}");
                }
            }
        }

        return found;
    }
}
