import { mkdir, writeFile } from "node:fs/promises";
import { network } from "hardhat";
import { parseEther } from "viem";
import { deployDeleGatorEnvironment } from "@metamask/delegation-toolkit/utils";

const { viem } = await network.connect();
const [deployer] = await viem.getWalletClients();
if (!deployer?.account) throw new Error("No deployer account configured for this network. Set the network-specific private-key environment variable in the shell; never put it in the repository or chat.");
const admin = deployer.account.address;
const publicClient = await viem.getPublicClient();
const chainId = await publicClient.getChainId();
const chainKey = chainId === 31337 ? "evm-local" : chainId === 97 ? "bsc-testnet" : chainId === 11155111 ? "ethereum-sepolia" : "";
if (!chainKey) throw new Error(`Unsupported deployment chain ID ${chainId}. Only local chain 31337, BSC Testnet 97, and Ethereum Sepolia 11155111 are supported.`);
if (chainId !== 31337 && process.env.CONFIRM_PUBLIC_DEPLOYMENT !== "I_UNDERSTAND_THIS_BROADCASTS") {
  throw new Error("Refusing public deployment. Set CONFIRM_PUBLIC_DEPLOYMENT=I_UNDERSTAND_THIS_BROADCASTS explicitly.");
}
// Existing token addresses are a Sepolia-only migration input. Never reuse
// them on BSC, where the deployment must create chain-local token contracts.
const configuredCafe = chainId === 11155111 ? process.env.CAFE_ADDRESS : undefined;
const configuredCoffee = chainId === 11155111 ? process.env.COFFEE_ADDRESS : undefined;
const configuredFaucet = process.env.FAUCET_ADDRESS ?? (chainId === 11155111 ? "0xBD1517529BB0BA20c43b4E39323C70058FADe86D" : undefined);
const rewardAmount = parseEther(process.env.REWARD_AMOUNT ?? "100000");

if (chainId === 11155111 && (!configuredCafe || !configuredCoffee)) {
  throw new Error("Ethereum Sepolia deployment requires both CAFE_ADDRESS and COFFEE_ADDRESS; the existing legacy tokens must never be replaced.");
}

const cafe = configuredCafe
  ? await viem.getContractAt("TestCafeToken", configuredCafe as `0x${string}`)
  : await viem.deployContract("TestCafeToken", [admin, parseEther("1000000000")]);
const coffee = configuredCoffee
  ? await viem.getContractAt("TestCoffeeToken", configuredCoffee as `0x${string}`)
  : await viem.deployContract("TestCoffeeToken", [admin, parseEther("1000000000")]);
const vault = await viem.deployContract("CafeLiquidStakingVault", [admin, cafe.address, coffee.address]);
const vaultDeployBlock = await publicClient.getBlockNumber();
const faucet = configuredFaucet
  ? await viem.getContractAt("CafeFaucet", configuredFaucet as `0x${string}`)
  : await viem.deployContract("CafeFaucet", [admin, cafe.address, parseEther("100"), 3600n]);

if (!configuredCafe && !configuredCoffee) {
  const t1 = await deployer.writeContract({ address: cafe.address, abi: cafe.abi, functionName: "mint", args: [admin, parseEther("1000000")] });
  await publicClient.waitForTransactionReceipt({ hash: t1 });
  const t2 = await deployer.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "mint", args: [admin, parseEther("1000000")] });
  await publicClient.waitForTransactionReceipt({ hash: t2 });
  const t3 = await deployer.writeContract({ address: cafe.address, abi: cafe.abi, functionName: "mint", args: [faucet.address, parseEther("100000")] });
  await publicClient.waitForTransactionReceipt({ hash: t3 });
}

// The Sepolia legacy COFFEE contract is owner-mintable, and the deployment
// wallet is checked against owner() before this script is run. Minting here
// funds only the new vault schedule; it does not replace the legacy token.
if (chainId === 11155111 && process.env.MINT_SEPOLIA_REWARDS !== "false") {
  const mintRewards = await deployer.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "mint", args: [admin, rewardAmount] });
  await publicClient.waitForTransactionReceipt({ hash: mintRewards });
}

const t4 = await deployer.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "transfer", args: [vault.address, rewardAmount] });
await publicClient.waitForTransactionReceipt({ hash: t4 });
const t5 = await deployer.writeContract({ address: vault.address, abi: vault.abi, functionName: "notifyRewardAmount", args: [rewardAmount, 30n * 24n * 60n * 60n] });
await publicClient.waitForTransactionReceipt({ hash: t5 });

const entryPoint = await viem.deployContract("EntryPointFixture");
// Canonical ERC-4337 v0.7.0 reference account factory, bound to the EntryPoint above.
const accountFactory = await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint.address]);
// Canonical VerifyingPaymaster. `admin` is the sponsorship-authorising signer for local dev;
// a real deployment must use a dedicated, secured signer and fund the paymaster's deposit.
const verifyingPaymaster = await viem.deployContract("CanonicalVerifyingPaymaster", [entryPoint.address, admin]);
const registry = await viem.deployContract("ERC8004RegistryFixture");
const resolver = await viem.deployContract("ERC7683ResolverFixture");
const escrow = await viem.deployContract("AgenticCommerceEscrow", [
  cafe.address,
  admin,
  100n, // 1% fee
  "0x0000000000000000000000000000000000000000" // Zero address for trusted forwarder as per user preference
]);

// Additive modular-account stack. The official SDK deploys the published,
// unmodified Delegation Framework v1.3.0 bytecode and reuses this deployment's
// canonical EntryPoint v0.7. Existing SimpleAccount addresses and behavior are
// deliberately left intact. Public deployments require the same explicit
// confirmation as the rest of this script.
const modularAccountsEnabled = chainId === 31337 || process.env.ENABLE_METAMASK_MODULAR_ACCOUNTS === "true";
const modularEnvironment = modularAccountsEnabled
  ? await deployDeleGatorEnvironment(
      deployer as never,
      publicClient as never,
      publicClient.chain!,
      { EntryPoint: entryPoint.address }
    )
  : null;

const rpcUrl = process.env.PUBLIC_RPC_URL ?? (
  chainId === 97
    ? process.env.BSC_TESTNET_RPC_URL ?? "https://97.rpc.thirdweb.com"
    : chainId === 11155111
      ? process.env.ETHEREUM_SEPOLIA_RPC_URL ?? "https://ethereum-sepolia-rpc.publicnode.com"
      : "http://127.0.0.1:8545"
);
const explorerBase = chainId === 97 ? "https://testnet.bscscan.com" : chainId === 11155111 ? "https://sepolia.etherscan.io" : "http://127.0.0.1:8545";
const displayName = chainId === 97 ? "BSC Testnet" : chainId === 11155111 ? "Ethereum Sepolia" : "Local EVM";
const manifest = {
  schemaVersion: 1,
  chainKey,
  chainId,
  rpcUrl,
  displayName,
  explorerAddressTemplate: `${explorerBase}/address/{0}`,
  explorerTransactionTemplate: `${explorerBase}/tx/{0}`,
  nativeCurrency: chainId === 97 ? { name: "BNB", symbol: "tBNB", decimals: 18 } : { name: "Ether", symbol: "ETH", decimals: 18 },
  deployBlock: vaultDeployBlock.toString(),
  compiler: { solc: "0.8.24", optimizerRuns: 200, viaIR: true },
  deployedAtUtc: new Date().toISOString(),
  addresses: { 
    cafe: cafe.address, 
    coffee: coffee.address, 
    liquidVault: vault.address, 
    faucet: faucet.address,
    entryPoint: entryPoint.address,
    accountFactory: accountFactory.address,
    verifyingPaymaster: verifyingPaymaster.address,
    erc8004Registry: registry.address,
    erc7683Resolver: resolver.address,
    erc8183Escrow: escrow.address,
    ...(modularEnvironment ? {
      modularSimpleFactory: modularEnvironment.SimpleFactory,
      delegationManager: modularEnvironment.DelegationManager,
      hybridDeleGatorImplementation: modularEnvironment.implementations.HybridDeleGatorImpl,
      allowedTargetsEnforcer: modularEnvironment.caveatEnforcers.AllowedTargetsEnforcer,
      allowedMethodsEnforcer: modularEnvironment.caveatEnforcers.AllowedMethodsEnforcer,
      exactCalldataEnforcer: modularEnvironment.caveatEnforcers.ExactCalldataEnforcer,
      limitedCallsEnforcer: modularEnvironment.caveatEnforcers.LimitedCallsEnforcer,
      nonceEnforcer: modularEnvironment.caveatEnforcers.NonceEnforcer,
      timestampEnforcer: modularEnvironment.caveatEnforcers.TimestampEnforcer
    } : {})
  },
  accountAbstraction: {
    entryPointVersion: "0.7",
    legacyAccountType: "simple-v0.7",
    modularAccountType: modularEnvironment ? "metamask-hybrid-delegator-v1.3.0" : null,
    modularFrameworkRevision: modularEnvironment ? "bfbdf9795a976833ed2fa000baf42fbb83958b03" : null,
    modularCompiler: modularEnvironment ? { solc: "0.8.23", optimizerRuns: 200, evmVersion: "london" } : null
  },
  capabilities: { walletLogin: true, liquidStaking: true, legacyExit: false, faucet: true, marketplacePayment: false, rewardMinting: true, agenticCommerce: true, agenticSessionPayments: modularEnvironment !== null },
  admin
};
const manifestPath = process.env.DEPLOYMENT_MANIFEST ?? `deployments/${chainKey}.json`;
await mkdir("deployments", { recursive: true });
await writeFile(manifestPath, JSON.stringify(manifest, null, 2) + "\n", "utf8");
console.log(JSON.stringify({ ...manifest, admin: "redacted" }, null, 2));
