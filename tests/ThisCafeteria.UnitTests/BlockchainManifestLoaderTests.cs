using FluentAssertions;
using System.Numerics;
using System.Text;
using ThisCafeteria.Application.Configuration;

namespace ThisCafeteria.UnitTests;

public sealed class BlockchainManifestLoaderTests
{
    [Fact]
    public void LoadsTheSameNineDecimalSolanaManifestForRuntimeRegistration()
    {
        var path = WriteManifest(9, 9, 9);
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), null, path);
            var registry = new ChainRegistry(options);
            var chain = registry.GetRequired("solana-localnet");

            chain.Capabilities.LiquidStaking.Should().BeTrue();
            chain.Deployment.Program.Should().Be(Key('P'));
            chain.Deployment.VaultPda.Should().Be(Key('V'));
            chain.Deployment.AuthorityPda.Should().Be(Key('V'));
            chain.Deployment.CafeDecimals.Should().Be(9);
            chain.Deployment.StCafeDecimals.Should().Be(9);
            chain.Deployment.CoffeeDecimals.Should().Be(9);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsEconomicallyUnsafeOrMismatchedSolanaDecimals()
    {
        var path = WriteManifest(18, 18, 18);
        try
        {
            var action = () => BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), null, path);
            action.Should().Throw<InvalidDataException>().WithMessage("*decimals*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnablesSolanaTestnetOnlyWhenAValidatedDeploymentManifestIsPresent()
    {
        var defaults = new ChainRegistry(BlockchainOptions.CreateDefaults());
        defaults.All.Single(chain => chain.Key == "solana-testnet").Enabled.Should().BeFalse();
        Action unresolvedLookup = () => defaults.GetRequired("solana-testnet");
        unresolvedLookup.Should().Throw<KeyNotFoundException>();

        var path = WriteManifest(9, 9, 9, "testnet", "solana-testnet", "https://api.testnet.solana.com");
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), null, path);
            var deployed = new ChainRegistry(options).GetRequired("solana-testnet");

            deployed.Enabled.Should().BeTrue();
            deployed.Capabilities.WalletLogin.Should().BeTrue();
            deployed.Capabilities.LiquidStaking.Should().BeTrue();
            deployed.SolanaCluster.Should().Be("testnet");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnablesBscTestnetFromAValidatedEvmManifest()
    {
        var defaults = new ChainRegistry(BlockchainOptions.CreateDefaults());
        defaults.All.Single(chain => chain.Key == "bsc-testnet").Enabled.Should().BeFalse();

        var path = WriteEvmManifest();
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), path, null);
            var deployed = new ChainRegistry(options).GetRequired("bsc-testnet");

            deployed.Enabled.Should().BeTrue();
            deployed.EvmChainId.Should().Be(97);
            deployed.EvmChainIdHex.Should().Be("0x61");
            deployed.PublicRpcUrl.Should().Be("https://97.rpc.thirdweb.com");
            deployed.Deployment.LiquidVault.Should().Be(Address('A'));
            deployed.Deployment.AgenticEscrow.Should().Be(Address('E'));
            deployed.Deployment.EntryPoint.Should().Be(Address('P'));
            deployed.Deployment.ERC8004Registry.Should().Be(Address('R'));
            deployed.Deployment.ERC7683Resolver.Should().Be(Address('S'));
            deployed.Deployment.PaymentToken.Should().Be(Address('C'));
            deployed.Capabilities.LiquidStaking.Should().BeTrue();
            deployed.Capabilities.AgenticCommerce.Should().BeTrue();
            deployed.Capabilities.AgenticSessionPayments.Should().BeTrue();
            deployed.Deployment.ModularAccountFactory.Should().Be(Address('M'));
            deployed.Deployment.DelegationManager.Should().Be(Address('D'));
            deployed.Deployment.HybridDeleGatorImplementation.Should().Be(Address('H'));
            deployed.Deployment.AllowedTargetsEnforcer.Should().Be(Address('1'));
            deployed.Deployment.AllowedMethodsEnforcer.Should().Be(Address('2'));
            deployed.Deployment.ExactCalldataEnforcer.Should().Be(Address('3'));
            deployed.Deployment.LimitedCallsEnforcer.Should().Be(Address('4'));
            deployed.Deployment.NonceEnforcer.Should().Be(Address('5'));
            deployed.Deployment.TimestampEnforcer.Should().Be(Address('6'));
            deployed.Deployment.ModularAccountType.Should().Be("metamask-hybrid-delegator-v1.3.0");
            deployed.Deployment.ModularFrameworkRevision.Should().Be("bfbdf9795a976833ed2fa000baf42fbb83958b03");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteManifest(int cafeDecimals, int stCafeDecimals, int coffeeDecimals, string cluster = "localnet", string chainKey = "solana-localnet", string rpcUrl = "http://127.0.0.1:8899")
    {
        var path = Path.Combine(Path.GetTempPath(), $"artisanalbrew-solana-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""
        {
          "schemaVersion": "1",
          "chainKey": "{{chainKey}}",
          "rpcUrl": "{{rpcUrl}}",
          "cluster": "{{cluster}}",
          "programId": "{{Key('P')}}",
          "deploymentSlot": 10,
          "statePda": "{{Key('V')}}",
          "authorityPda": "{{Key('V')}}",
          "cafeMint": "{{Key('C')}}",
          "stCafeMint": "{{Key('S')}}",
          "coffeeMint": "{{Key('R')}}",
          "cafeCustody": "{{Key('A')}}",
          "coffeeCustody": "{{Key('B')}}",
          "administrator": "{{Key('D')}}",
          "tokenProgram": "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
          "token2022Program": "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb",
          "cafeDecimals": {{cafeDecimals}},
          "stCafeDecimals": {{stCafeDecimals}},
          "coffeeDecimals": {{coffeeDecimals}}
        }
        """);
        return path;
    }

    private static string WriteEvmManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"artisanalbrew-evm-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""
        {
          "schemaVersion": 1,
          "chainKey": "bsc-testnet",
          "chainId": 97,
          "rpcUrl": "https://97.rpc.thirdweb.com",
          "displayName": "BSC Testnet",
          "nativeCurrency": { "name": "BNB", "symbol": "tBNB", "decimals": 18 },
          "addresses": {
            "cafe": "{{Address('C')}}",
            "coffee": "{{Address('B')}}",
            "liquidVault": "{{Address('A')}}",
            "faucet": "{{Address('F')}}",
            "erc8183Escrow": "{{Address('E')}}",
            "entryPoint": "{{Address('P')}}",
            "erc8004Registry": "{{Address('R')}}",
            "erc7683Resolver": "{{Address('S')}}",
            "modularSimpleFactory": "{{Address('M')}}",
            "delegationManager": "{{Address('D')}}",
            "hybridDeleGatorImplementation": "{{Address('H')}}",
            "allowedTargetsEnforcer": "{{Address('1')}}",
            "allowedMethodsEnforcer": "{{Address('2')}}",
            "exactCalldataEnforcer": "{{Address('3')}}",
            "limitedCallsEnforcer": "{{Address('4')}}",
            "nonceEnforcer": "{{Address('5')}}",
            "timestampEnforcer": "{{Address('6')}}"
          },
          "deployBlock": 123,
          "accountAbstraction": {
            "modularAccountType": "metamask-hybrid-delegator-v1.3.0",
            "modularFrameworkRevision": "bfbdf9795a976833ed2fa000baf42fbb83958b03"
          },
          "capabilities": { "walletLogin": true, "liquidStaking": true, "faucet": true, "rewardMinting": true, "agenticCommerce": true, "agenticSessionPayments": true }
        }
        """);
        return path;
    }

    private static string Key(char value) => Base58Encode(Enumerable.Repeat((byte)value, 32).ToArray());

    private static string Address(char value) => $"0x{new string(value, 40)}";

    private static string Base58Encode(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var result = new StringBuilder();
        while (value > 0) { value = BigInteger.DivRem(value, 58, out var remainder); result.Insert(0, alphabet[(int)remainder]); }
        foreach (var item in bytes) { if (item != 0) break; result.Insert(0, '1'); }
        return result.Length == 0 ? "1" : result.ToString();
    }
}
