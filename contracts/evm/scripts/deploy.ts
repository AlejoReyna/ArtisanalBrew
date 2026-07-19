import { mkdir, writeFile } from "node:fs/promises";
import { network } from "hardhat";
import { parseEther } from "viem";

const { viem } = await network.connect();
const [deployer] = await viem.getWalletClients();
const admin = deployer.account.address;
const networkName = process.env.HARDHAT_NETWORK ?? "hardhat";
if (!['hardhat', 'localhost'].includes(networkName) && process.env.CONFIRM_PUBLIC_DEPLOYMENT !== "I_UNDERSTAND_THIS_BROADCASTS") {
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
const publicClient = await viem.getPublicClient();
const vaultDeployBlock = await publicClient.getBlockNumber();
const faucet = await viem.deployContract("CafeFaucet", [admin, cafe.address, parseEther("100"), 3600n]);

if (!configuredCafe && !configuredCoffee) {
  await deployer.writeContract({ address: cafe.address, abi: cafe.abi, functionName: "mint", args: [admin, parseEther("1000000")] });
  await deployer.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "mint", args: [admin, parseEther("1000000")] });
  await deployer.writeContract({ address: cafe.address, abi: cafe.abi, functionName: "mint", args: [faucet.address, parseEther("100000")] });
}
await deployer.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "transfer", args: [vault.address, parseEther("100000")] });
await deployer.writeContract({ address: vault.address, abi: vault.abi, functionName: "notifyRewardAmount", args: [parseEther("100000"), 30n * 24n * 60n * 60n] });

const chain = await publicClient.getChainId();
const manifest = {
  schemaVersion: 1,
  chainKey: chain === 31337 ? "evm-local" : "unknown",
  chainId: chain,
  deployBlock: vaultDeployBlock.toString(),
  compiler: { solc: "0.8.24", optimizerRuns: 200, viaIR: true },
  deployedAtUtc: new Date().toISOString(),
  addresses: { cafe: cafe.address, coffee: coffee.address, liquidVault: vault.address, faucet: faucet.address },
  capabilities: { walletLogin: true, liquidStaking: true, legacyExit: false, faucet: true, marketplacePayment: false, rewardMinting: true }
};
await mkdir("deployments", { recursive: true });
await writeFile("deployments/evm-local.json", JSON.stringify(manifest, null, 2) + "\n", "utf8");
console.log(JSON.stringify({ ...manifest, admin: "redacted" }, null, 2));
