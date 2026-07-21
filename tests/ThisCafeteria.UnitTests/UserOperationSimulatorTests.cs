using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// Fail-closed configuration gating for UserOperationSimulator. The actual eth_call
/// state-override recipe — substituting canonical EntryPointSimulations bytecode for the real
/// EntryPoint's code to measure real gas without deploying anything or touching chain state — is
/// proven against a live Hardhat node by contracts/evm/scripts/simulation-recipe-check.ts and
/// contracts/evm/scripts/crossstack-sponsor-check.ts; it is not re-verified here, since a chain-free
/// unit test cannot tell a correct simulation from a coincidentally-matching one.
/// </summary>
public class UserOperationSimulatorTests
{
    private sealed class StubChainRegistry(params ChainDefinition[] chains) : IChainRegistry
    {
        public string DefaultChainKey => chains.Length > 0 ? chains[0].Key : string.Empty;
        public IReadOnlyList<ChainDefinition> All { get; } = chains;
        public bool TryGet(string key, out ChainDefinition definition)
        {
            definition = All.FirstOrDefault(c => c.Key == key)!;
            return definition is not null;
        }
        public ChainDefinition GetRequired(string key) => TryGet(key, out var d) ? d : throw new KeyNotFoundException(key);
    }

    private static UserOperationSimulationRequest Request(string chainKey = "evm-local") => new()
    {
        ChainKey = chainKey,
        Sender = "0x93e957812b6ce6e7100b0b743f39376838be9920",
        Nonce = 0,
        InitCode = "0x",
        CallData = "0x",
        AccountGasLimits = "0x000000000000000000000000000f4240000000000000000000000000000f4240",
        PreVerificationGas = 100_000,
        GasFees = "0x0000000000000000000000003b9aca00000000000000000000000002540be400"
    };

    [Fact]
    public async Task Simulate_UnknownChain_FailsWithoutAttemptingAnRpcCall()
    {
        var simulator = new UserOperationSimulator(new StubChainRegistry(), NullLogger<UserOperationSimulator>.Instance);

        var result = await simulator.SimulateAsync(Request("ethereum-sepolia"));

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("no configured EntryPoint");
    }

    [Fact]
    public async Task Simulate_ChainWithoutEntryPoint_Fails()
    {
        var chain = new ChainDefinition
        {
            Key = "evm-local",
            Family = ChainFamily.Evm,
            EvmChainId = 31337,
            EvmChainIdHex = "0x7a69",
            PublicRpcUrl = "http://127.0.0.1:8545",
            Deployment = new ChainDeployment { EntryPoint = string.Empty }
        };
        var simulator = new UserOperationSimulator(new StubChainRegistry(chain), NullLogger<UserOperationSimulator>.Instance);

        var result = await simulator.SimulateAsync(Request());

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("no configured EntryPoint");
    }

    [Fact]
    public void Failure_FactoryProducesAnUnsuccessfulResult()
    {
        var result = UserOperationSimulationResult.Failure("AA21 didn't pay prefund");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("AA21 didn't pay prefund");
        result.PaidWei.Should().Be(0, "a failed simulation must not report a usable cost");
    }
}
