using FluentAssertions;
using System.Numerics;
using System.Text;
using ThisCafeteria.Application.Configuration;

namespace ThisCafeteria.UnitTests;

// One test mutates the process working directory, so this class must not run in parallel with others.
[CollectionDefinition("ManifestLoaderCwdSerial", DisableParallelization = true)]
public sealed class ManifestLoaderCwdSerialCollection;

[Collection("ManifestLoaderCwdSerial")]
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
    public void EnablesSolanaDevnetOnlyWhenAValidatedDeploymentManifestIsPresent()
    {
        var defaults = new ChainRegistry(BlockchainOptions.CreateDefaults());
        defaults.All.Single(chain => chain.Key == "solana-devnet").Enabled.Should().BeFalse();
        Action unresolvedLookup = () => defaults.GetRequired("solana-devnet");
        unresolvedLookup.Should().Throw<KeyNotFoundException>();

        var path = WriteManifest(9, 9, 9, "devnet", "solana-devnet", "https://api.devnet.solana.com");
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), null, path);
            var deployed = new ChainRegistry(options).GetRequired("solana-devnet");

            deployed.Enabled.Should().BeTrue();
            deployed.Capabilities.WalletLogin.Should().BeTrue();
            deployed.Capabilities.LiquidStaking.Should().BeTrue();
            deployed.SolanaCluster.Should().Be("devnet");
            deployed.IconAsset.Should().Be("/images/solana_logo.svg");
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
            deployed.IconAsset.Should().Be("/images/bnb_logo.svg");
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

    [Fact]
    public void ReadsMarketplacePaymentFromTheManifestInsteadOfHardcodingIt()
    {
        // Manifest is the single source of truth: with the flag off (the WriteEvmManifest default)
        // the capability is off even though the escrow address is present...
        var offPath = WriteEvmManifest();
        // ...and with the flag on plus the required escrow address, the capability lights up.
        var onPath = Path.Combine(Path.GetTempPath(), $"artisanalbrew-evm-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(onPath, EvmManifestJson("bsc-testnet", 97, "https://97.rpc.thirdweb.com", marketplacePayment: true));
        try
        {
            var off = new ChainRegistry(BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), offPath, null)).GetRequired("bsc-testnet");
            off.Capabilities.MarketplacePayment.Should().BeFalse();

            var on = new ChainRegistry(BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), onPath, null)).GetRequired("bsc-testnet");
            on.Capabilities.MarketplacePayment.Should().BeTrue();
            on.Deployment.AgenticEscrow.Should().Be(Address('E'));
        }
        finally
        {
            File.Delete(offPath);
            File.Delete(onPath);
        }
    }

    [Fact]
    public void RejectsAMarketplacePaymentManifestThatOmitsTheEscrowDeployment()
    {
        // Inconsistent manifest: marketplacePayment on, but no erc8183Escrow address to settle against.
        // Validation must fail closed rather than advertise a capability with no contract behind it.
        var path = Path.Combine(Path.GetTempPath(), $"artisanalbrew-evm-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, EvmManifestJson("bsc-testnet", 97, "https://97.rpc.thirdweb.com", marketplacePayment: true, includeEscrow: false));
        try
        {
            var action = () => new ChainRegistry(BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), path, null));
            action.Should().Throw<InvalidOperationException>().WithMessage("*marketplace payment*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadsLegacyPoolFromTheEvmManifest()
    {
        var path = WriteEvmManifest();
        File.WriteAllText(path, EvmManifestJson("bsc-testnet", 97, "https://97.rpc.thirdweb.com", legacyPool: Address('L')));
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), path, null);
            var deployed = new ChainRegistry(options).GetRequired("bsc-testnet");

            deployed.Deployment.LegacyPool.Should().Be(Address('L'));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AllowsMarketplacePaymentBackedByALegacyPoolAloneWithNoEscrow()
    {
        // The OR branch of the settlement-venue rule: a legacy-pool-only manifest (no escrow)
        // must still validate, matching the native-transfer checkout rail.
        var path = Path.Combine(Path.GetTempPath(), $"artisanalbrew-evm-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, EvmManifestJson("bsc-testnet", 97, "https://97.rpc.thirdweb.com", marketplacePayment: true, includeEscrow: false, legacyPool: Address('L')));
        try
        {
            var deployed = new ChainRegistry(BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), path, null)).GetRequired("bsc-testnet");

            deployed.Capabilities.MarketplacePayment.Should().BeTrue();
            deployed.Deployment.LegacyPool.Should().Be(Address('L'));
            deployed.Deployment.AgenticEscrow.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LeavesBundlerRpcUrlUnsetWhenTheManifestOmitsIt()
    {
        var path = WriteEvmManifest();
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), path, null);
            var deployed = new ChainRegistry(options).GetRequired("bsc-testnet");

            deployed.BundlerRpcUrl.Should().BeNullOrEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadsBundlerRpcUrlFromTheManifestRootWhenPresent()
    {
        const string bundlerUrl = "https://bundler.test/rpc";
        var path = Path.Combine(Path.GetTempPath(), $"artisanalbrew-evm-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, EvmManifestJson("bsc-testnet", 97, "https://97.rpc.thirdweb.com", bundlerUrl));
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), path, null);
            var deployed = new ChainRegistry(options).GetRequired("bsc-testnet");

            deployed.BundlerRpcUrl.Should().Be(bundlerUrl);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BundlerRpcUrlOverride_WinsOverTheManifestAndIsKeyedPerChain()
    {
        var path = WriteEvmManifest();
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), path, null);
            // Only bsc-testnet's override variable is set - other chains must be untouched.
            var overridden = BlockchainManifestLoader.ApplyBundlerRpcUrlOverrides(options, name =>
                name == "ARTISANALBREW_BUNDLER_RPC_URL__BSC_TESTNET" ? "https://api.pimlico.io/v2/bsc-testnet/rpc?apikey=secret" : null);
            var registry = new ChainRegistry(overridden);

            registry.GetRequired("bsc-testnet").BundlerRpcUrl.Should().Be("https://api.pimlico.io/v2/bsc-testnet/rpc?apikey=secret");
            registry.All.Where(c => c.Key != "bsc-testnet").Should().OnlyContain(c => string.IsNullOrEmpty(c.BundlerRpcUrl));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BundlerRpcUrlOverride_LeavesChainsUnsetWhenNoEnvironmentVariableMatches()
    {
        var options = BlockchainOptions.CreateDefaults();

        var overridden = BlockchainManifestLoader.ApplyBundlerRpcUrlOverrides(options, _ => null);

        overridden.Chains.Should().OnlyContain(c => string.IsNullOrEmpty(c.BundlerRpcUrl));
    }

    [Fact]
    public void LoadsMultipleEvmDeploymentManifestsForOneApplication()
    {
        var bscPath = WriteEvmManifest();
        var sepoliaPath = WriteEvmManifest("ethereum-sepolia", 11155111, "https://ethereum-sepolia-rpc.publicnode.com");
        try
        {
            var options = BlockchainManifestLoader.LoadDeploymentManifests(
                BlockchainOptions.CreateDefaults(), $"{bscPath};{sepoliaPath}", null);
            var registry = new ChainRegistry(options);

            registry.GetRequired("bsc-testnet").EvmChainId.Should().Be(97);
            registry.GetRequired("bsc-testnet").IconAsset.Should().Be("/images/bnb_logo.svg");
            registry.GetRequired("ethereum-sepolia").EvmChainId.Should().Be(11155111);
            registry.GetRequired("ethereum-sepolia").PublicRpcUrl.Should().Be("https://ethereum-sepolia-rpc.publicnode.com");
            registry.GetRequired("ethereum-sepolia").IconAsset.Should().Be("/images/eth_logo.png");
        }
        finally
        {
            File.Delete(bscPath);
            File.Delete(sepoliaPath);
        }
    }

    private static string WriteManifest(int cafeDecimals, int stCafeDecimals, int coffeeDecimals, string cluster = "localnet", string chainKey = "solana-localnet", string rpcUrl = "http://127.0.0.1:8899", bool faucet = false)
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
          "coffeeDecimals": {{coffeeDecimals}},
          "capabilities": { "walletLogin": true, "liquidStaking": true, "faucet": {{(faucet ? "true" : "false")}} }
        }
        """);
        return path;
    }

    [Fact]
    public void ReadsTheFaucetCapabilityFromTheSolanaManifestInsteadOfHardcodingItOff()
    {
        // Off by default (no flag / flag false), on when the manifest declares it. The CAFE mint and
        // administrator authority a devnet mint needs are always present in a valid Solana manifest.
        var offPath = WriteManifest(9, 9, 9, "devnet", "solana-devnet", "https://api.devnet.solana.com");
        var onPath = WriteManifest(9, 9, 9, "devnet", "solana-devnet", "https://api.devnet.solana.com", faucet: true);
        try
        {
            var off = new ChainRegistry(BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), null, offPath)).GetRequired("solana-devnet");
            off.Capabilities.Faucet.Should().BeFalse();

            var on = new ChainRegistry(BlockchainManifestLoader.LoadDeploymentManifests(BlockchainOptions.CreateDefaults(), null, onPath)).GetRequired("solana-devnet");
            on.Capabilities.Faucet.Should().BeTrue();
        }
        finally
        {
            File.Delete(offPath);
            File.Delete(onPath);
        }
    }

    [Fact]
    public void ResolvesRelativeManifestPathByWalkingUpFromTheWorkingDirectory()
    {
        // Reproduces the "liquid staking is not deployed on this chain yet" report: appsettings ships
        // a relative "deployments/ethereum-sepolia.json" path, but a local `dotnet run --project` (or
        // IDE launch) sets the process CWD to the project directory, where that relative path does not
        // exist. Before the walk-up fix the manifest silently failed to load and the registry fell back
        // to the addressless ethereum-sepolia stub (empty LiquidVault). The loader must find the
        // repo-root copy from a nested working directory.
        var root = Directory.CreateTempSubdirectory("artisanalbrew-manifest-cwd-");
        var originalWorkingDirectory = Directory.GetCurrentDirectory();
        try
        {
            var deployments = Directory.CreateDirectory(Path.Combine(root.FullName, "deployments"));
            File.WriteAllText(
                Path.Combine(deployments.FullName, "ethereum-sepolia.json"),
                EvmManifestJson("ethereum-sepolia", 11155111, "https://ethereum-sepolia-rpc.publicnode.com"));
            var nestedWorkingDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "ThisCafeteria.Web"));
            Directory.SetCurrentDirectory(nestedWorkingDirectory.FullName);

            var options = BlockchainManifestLoader.LoadDeploymentManifests(
                BlockchainOptions.CreateDefaults(), "deployments/ethereum-sepolia.json", null);
            var deployed = new ChainRegistry(options).GetRequired("ethereum-sepolia");

            deployed.Enabled.Should().BeTrue();
            deployed.Capabilities.LiquidStaking.Should().BeTrue();
            deployed.Deployment.LiquidVault.Should().Be(Address('A'));
            deployed.Deployment.StCafe.Should().Be(Address('A'));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static string WriteEvmManifest(string chainKey = "bsc-testnet", int chainId = 97, string rpcUrl = "https://97.rpc.thirdweb.com")
    {
        var path = Path.Combine(Path.GetTempPath(), $"artisanalbrew-evm-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, EvmManifestJson(chainKey, chainId, rpcUrl));
        return path;
    }

    private static string EvmManifestJson(string chainKey, int chainId, string rpcUrl, string? bundlerRpcUrl = null, bool marketplacePayment = false, bool includeEscrow = true, string? legacyPool = null) =>
        $$"""
        {
          "schemaVersion": 1,
          "chainKey": "{{chainKey}}",
          "chainId": {{chainId}},
          "rpcUrl": "{{rpcUrl}}",
          {{(bundlerRpcUrl is null ? "" : $"\"bundlerRpcUrl\": \"{bundlerRpcUrl}\",")}}
          "displayName": "Test Network",
          "nativeCurrency": { "name": "BNB", "symbol": "tBNB", "decimals": 18 },
          "addresses": {
            "cafe": "{{Address('C')}}",
            "coffee": "{{Address('B')}}",
            "liquidVault": "{{Address('A')}}",
            "faucet": "{{Address('F')}}",
            {{(includeEscrow ? $"\"erc8183Escrow\": \"{Address('E')}\"," : "")}}
            {{(legacyPool is null ? "" : $"\"legacyPool\": \"{legacyPool}\",")}}
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
          "capabilities": { "walletLogin": true, "liquidStaking": true, "faucet": true, "rewardMinting": true, "agenticCommerce": true, "agenticSessionPayments": true, "marketplacePayment": {{(marketplacePayment ? "true" : "false")}} }
        }
        """;

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
