using FluentAssertions;

namespace ThisCafeteria.ArchitectureTests;

/// <summary>
/// Invariant 4 of <c>docs/clean-architecture-plan.md</c>: components render, they do not query.
/// </summary>
public sealed class PresentationPurityTests
{
    /// <summary>
    /// Entity Framework's async query operators. A component calling one of these is querying the
    /// database from the render tree instead of asking an injected service for a result.
    /// </summary>
    private static readonly string[] DataAccessTokens =
    [
        "AppDbContext",
        "IDbContextFactory",
        "ToListAsync",
        "SaveChangesAsync",
        "FirstOrDefaultAsync",
        "SingleOrDefaultAsync",
        "AnyAsync",
    ];

    [Fact]
    public void RazorComponentsDoNotPerformDataAccess()
    {
        var offenders = SolutionFiles
            .SourceFilesUnder("src/ThisCafeteria.Web/Components")
            .Where(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(path => SolutionFiles
                .ReadCodeLines(path)
                .Any(line => DataAccessTokens.Any(token => line.Contains(token, StringComparison.Ordinal))))
            .ToHashSet(StringComparer.Ordinal);

        offenders.Should().BeEquivalentTo(
            KnownViolations.DataAccessInRazorComponents,
            "a Razor component should receive data from an injected Application service - "
            + "querying from the render tree ties the view to the schema and, under Blazor Server, "
            + "contends on the scoped DbContext");
    }
}
