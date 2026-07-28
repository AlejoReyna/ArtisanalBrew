using FluentAssertions;

namespace ThisCafeteria.IntegrationTests;

public sealed class PixelHomeRenderTests
{
    private static readonly string[] RequiredFiles =
    [
        "src/ThisCafeteria.Web/wwwroot/js/pixelCrewSim.js",
        "src/ThisCafeteria.Web/wwwroot/js/pixelCrewPolicy.js",
        "src/ThisCafeteria.Web/wwwroot/js/pixelCrewRuntime.js",
        "src/ThisCafeteria.Web/wwwroot/images/coffee-coin-pixel.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-andromeda.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-chain-bnb.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-chain-ethereum.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-chain-solana.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-planet.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-planet-ringed.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-satellite.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-plus-one.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-robot-coincrew.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-robot-coincrew-flip.png",
        "src/ThisCafeteria.Web/wwwroot/images/pl-robot-courier.png",
    ];

    [Fact]
    public void Homepage_RouteUsesPixelCrewReleaseContract()
    {
        var root = FindRepositoryRoot();
        var route = File.ReadAllText(
            Path.Combine(root.FullName, "src/ThisCafeteria.Web/Components/Pages/Home.razor"));
        var hero = File.ReadAllText(
            Path.Combine(root.FullName, "src/ThisCafeteria.Web/Components/Home/PixelHome.razor"));
        var scene = File.ReadAllText(
            Path.Combine(root.FullName, "src/ThisCafeteria.Web/Components/Layout/GlobalScene.razor"));

        route.Should().Contain("@page \"/\"");
        route.Should().Contain("<PixelHome />", "the pixel homepage must be the production root");
        hero.Should().Contain("class=\"ph-hero", "the release health check uses the hero marker");
        hero.Should().Contain(
            "\"ph-scene-root\"",
            "the trained runtime mounts on this stable id");
        scene.Should().Contain("id=\"ph-scene-root\"", "the background scenario keeps the stable mount id");
        scene.Should().Contain(
            "data-permanent",
            "the scenario containers must survive enhanced navigation so the sky never restarts");
        hero.Should().Contain(
            "aria-label=\"Your next coffee, on-chain\"",
            "the visual pixel title must retain an accessible heading");
        hero.Should().Contain("href=\"/staking\"", "the primary conversion path must remain available");
        hero.Should().Contain(
            "private string _checkpoint = \"trained\"",
            "generation 300 must remain the initial browser checkpoint");
    }

    [Fact]
    public void Homepage_TrainedRuntimeAssets_ArePresentAndNonEmpty()
    {
        var root = FindRepositoryRoot();

        foreach (var relativePath in RequiredFiles)
        {
            var file = new FileInfo(Path.Combine(root.FullName, relativePath));
            file.Exists.Should().BeTrue($"required homepage asset {relativePath} must be committed");
            file.Length.Should().BeGreaterThan(0, $"{relativePath} must not be empty");
        }
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

        throw new DirectoryNotFoundException(
            $"Could not locate ThisCafeteria.sln above {AppContext.BaseDirectory}.");
    }
}
