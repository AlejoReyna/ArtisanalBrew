using FluentAssertions;
using ThisCafeteria.Application.Configuration;

namespace ThisCafeteria.UnitTests;

public sealed class ChainRegistryTests
{
    [Fact]
    public void DefaultsRegisterNineNetworksButExposeOnlyTheFinishedConnection()
    {
        var registry = new ChainRegistry(BlockchainOptions.CreateDefaults());

        registry.All.Should().HaveCount(9);
        registry.All.Select(chain => chain.Key).Should().ContainInOrder(
            "ethereum-sepolia", "hedera-testnet", "avalanche-fuji", "linea-sepolia", "base-sepolia",
            "bsc-testnet", "monad-testnet", "arbitrum-sepolia", "solana-testnet");
        registry.All.Where(chain => chain.Enabled).Should().ContainSingle(chain => chain.Key == "ethereum-sepolia");
        registry.All.Single(chain => chain.Key == "solana-testnet").EvmChainId.Should().BeNull();
    }

    [Fact]
    public void RejectsLiquidStakingWithoutDeployment()
    {
        var options = new BlockchainOptions
        {
            DefaultChainKey = "test",
            Chains = [new ChainDefinition
            {
                Key = "test", DisplayName = "Test", Family = ChainFamily.Evm, EvmChainId = 31337, EvmChainIdHex = "0x7a69",
                PublicRpcUrl = "http://127.0.0.1:8545", Capabilities = new ChainCapabilities { LiquidStaking = true }
            }]
        };

        var action = () => new ChainRegistry(options);
        action.Should().Throw<InvalidOperationException>().WithMessage("*liquid staking*");
    }

    [Fact]
    public void DoesNotExposeServerRpcThroughThePublicDefinitionContract()
    {
        var definition = new ChainDefinition { Key = "test", PublicRpcUrl = "https://public.example", ServerRpcUrl = "https://user:secret@private.example" };

        definition.PublicRpcUrl.Should().NotContain("secret");
        definition.EffectiveServerRpcUrl.Should().Contain("secret");
    }
}
