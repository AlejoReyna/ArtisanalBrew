using System.Text.Json;
using FluentAssertions;

namespace ThisCafeteria.IntegrationTests;

public sealed class ChainVisibilityTests(ThisCafeteriaWebApplicationFactory factory) : IClassFixture<ThisCafeteriaWebApplicationFactory>
{
    [Fact]
    public async Task PublicChainApiExposesOnlyWorkingConnections()
    {
        var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/chains"));
        var keys = document.RootElement.GetProperty("chains").EnumerateArray()
            .Select(chain => chain.GetProperty("key").GetString()).ToArray();

        keys.Should().BeEquivalentTo("ethereum-sepolia", "solana-localnet");
        keys.Should().NotContain(new[]
        {
            "hedera-testnet", "avalanche-fuji", "linea-sepolia", "base-sepolia",
            "bsc-testnet", "monad-testnet", "arbitrum-sepolia", "solana-testnet"
        });
    }
}
