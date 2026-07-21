import { mkdir, writeFile } from "node:fs/promises";
import { network } from "hardhat";
import { parseEther } from "viem";
import { deployDeleGatorEnvironment } from "@metamask/delegation-toolkit/utils";

const { viem } = await network.connect();
const [deployer] = await viem.getWalletClients();
const admin = deployer.account.address;
const publicClient = await viem.getPublicClient();
const chainId = await publicClient.getChainId();
const chainKey = chainId === 31337 ? "evm-local" : chainId === 97 ? "bsc-testnet" : "";
if (!chainKey) throw new Error(`Unsupported deployment chain ID ${chainId}. Only local chain 31337 and BSC Testnet 97 are supported.`);
if (chainId !== 31337 && process.env.CONFIRM_PUBLIC_DEPLOYMENT !== "I_UNDERSTAND_THIS_BROADCASTS") {
  throw new Error("Refusing public deployment. Set CONFIRM_PUBLIC_DEPLOYMENT=I_UNDERSTAND_THIS_BROADCASTS explicitly.");
}
const configuredCafe = process.env.CAFE_ADDRESS;
const configuredCoffee = process.env.COFFEE_ADDRESS;

const cafe = configuredCafe
  ? await viem.getContractAt("TestCafeToken", configuredCafe as `0x${string}`)
  : await viem.deployContract("TestCafeToken", [admin, parseEther("1000000000")]);
const coffee = configuredCoffee
  ? await viem.getContractAt("TestCoffeeToken", configuredCoffee as `0x${string}`)
  : await viem.deployContract("TestCoffeeToken", [admin, parseEther("1000000000")]);
const vault = await viem.deployContract("CafeLiquidStakingVault", [admin, cafe.address, coffee.address]);
const vaultDeployBlock = await publicClient.getBlockNumber();
const faucet = await viem.deployContract("CafeFaucet", [admin, cafe.address, parseEther("100"), 3600n]);

if (!configuredCafe && !configuredCoffee) {
  const t1 = await deployer.writeContract({ address: cafe.address, abi: cafe.abi, functionName: "mint", args: [admin, parseEther("1000000")] });
  await publicClient.waitForTransactionReceipt({ hash: t1 });
  const t2 = await deployer.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "mint", args: [admin, parseEther("1000000")] });
  await publicClient.waitForTransactionReceipt({ hash: t2 });
  const t3 = await deployer.writeContract({ address: cafe.address, abi: cafe.abi, functionName: "mint", args: [faucet.address, parseEther("100000")] });
  await publicClient.waitForTransactionReceipt({ hash: t3 });
}
const t4 = await deployer.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "transfer", args: [vault.address, parseEther("100000")] });
await publicClient.waitForTransactionReceipt({ hash: t4 });
const t5 = await deployer.writeContract({ address: vault.address, abi: vault.abi, functionName: "notifyRewardAmount", args: [parseEther("100000"), 30n * 24n * 60n * 60n] });
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

const rpcUrl = process.env.PUBLIC_RPC_URL ?? (chainId === 97 ? process.env.BSC_TESTNET_RPC_URL ?? "https://97.rpc.thirdweb.com" : "http://127.0.0.1:8545");
const explorerBase = chainId === 97 ? "https://testnet.bscscan.com" : "http://127.0.0.1:8545";
const displayName = chainId === 97 ? "BSC Testnet" : "Local EVM";
const manifest = {
  schemaVersion: 1,
  chainKey,
  chainId,
  rpcUrl,
  displayName,
  explorerAddressTemplate: `${explorerBase}/address/{0}`,
  explorerTransactionTemplate: `${explorerBase}/tx/{0}`,
  nativeCurrency: chainId === 97 ? { name: "BNB", symbol: "tBNB", decimals: 18 } : { name: "Local Ether", symbol: "ETH", decimals: 18 },
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
