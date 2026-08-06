using FluentAssertions;

namespace ThisCafeteria.ArchitectureTests;

/// <summary>
/// Invariant 5 of <c>docs/clean-architecture-plan.md</c>: a package that implies an architectural
/// pattern has to be a package we actually use. An unused one advertises a structure the code does
/// not have, and sends the next reader looking for handlers that were never written.
/// </summary>
public sealed class DependencyHygieneTests
{
    [Theory]
    [InlineData("MediatR", "MediatR")]
    [InlineData("FluentValidation", "FluentValidation")]
    public void ArchitecturalPackagesAreEitherUsedOrRemoved(string packageId, string rootNamespace)
    {
        var isDeclared = IsPackageDeclared(packageId);
        var isUsed = IsNamespaceReferenced(rootNamespace);
        var isKnownUnused = KnownViolations.UnusedArchitecturalPackages.Contains(packageId);

        if (isKnownUnused)
        {
            isDeclared.Should().BeTrue(
                $"{packageId} is recorded in KnownViolations as declared-but-unused; once the "
                + "PackageReference is deleted, delete the KnownViolations entry too");
            isUsed.Should().BeFalse(
                $"{packageId} is recorded as unused - if it has since been adopted, remove it from "
                + "KnownViolations.UnusedArchitecturalPackages");
            return;
        }

        if (isDeclared)
        {
            isUsed.Should().BeTrue(
                $"{packageId} is declared as a dependency but nothing under src/ references it - "
                + "either adopt it or drop the PackageReference");
        }
    }

    private static bool IsPackageDeclared(string packageId)
    {
        var needle = $"""PackageReference Include="{packageId}" """.TrimEnd();

        return Directory
            .EnumerateFiles(Path.Combine(SolutionFiles.RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Any(path => File.ReadAllText(path).Contains(needle, StringComparison.Ordinal));
    }

    private static bool IsNamespaceReferenced(string rootNamespace) =>
        SolutionFiles
            .SourceFilesUnder("src")
            .Any(path => SolutionFiles
                .ReadCodeLines(path)
                .Any(line => line.Contains($"using {rootNamespace};", StringComparison.Ordinal)
                    || line.Contains($"{rootNamespace}.", StringComparison.Ordinal)));
}
