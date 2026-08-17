using FluentAssertions;

namespace ThisCafeteria.UnitTests;

public sealed class DemoSurfaceHygieneTests
{
    [Fact]
    public void TemplateLeftoverPagesAreGoneAndTermsExist()
    {
        var root = FindRepositoryRoot();
        var pages = Path.Combine(root.FullName, "src/ThisCafeteria.Web/Components/Pages");

        File.Exists(Path.Combine(pages, "Weather.razor")).Should().BeFalse();
        File.Exists(Path.Combine(pages, "Counter.razor")).Should().BeFalse();

        var terms = File.ReadAllText(Path.Combine(pages, "Terms.razor"));
        terms.Should().Contain("@page \"/terms\"");
        terms.Should().Contain("testnet demonstration");
    }

    [Fact]
    public void AzureDeployJobIsGatedOffTheLiveHost()
    {
        var workflow = File.ReadAllText(
            Path.Combine(FindRepositoryRoot().FullName, ".github/workflows/ci.yml"));

        workflow.Should().Contain("vars.ENABLE_AZURE_DEPLOY == 'true'");
        workflow.Should().Contain("Atlantic.Net");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ThisCafeteria.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate ThisCafeteria.sln above {AppContext.BaseDirectory}.");
    }
}
