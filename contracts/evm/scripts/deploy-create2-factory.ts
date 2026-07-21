/**
 * Deploys the canonical Nick's-method deterministic CREATE2 factory
 * (0x4e59b44847b379578588920ca78fbf26c0b4956c) to the local Hardhat node, using the well-known
 * presigned raw transaction. A fresh Hardhat node does not have this factory pre-deployed.
 *
 * This exists because @pimlico/alto (the ERC-4337 bundler) tries to auto-deploy this same factory
 * on startup if it's missing, using its own hardcoded copy of the raw transaction — but in
 * @pimlico/alto@0.0.20 that copy is corrupted (every "00" byte pair in the hex payload has been
 * replaced with the literal character "V"), so Alto's self-deploy attempt fails with
 * "invalid value: string ..., expected a valid hex string". Pre-deploying the factory here lets
 * Alto skip that broken step entirely. See docs/agentic-commerce-stack-plan.md's Phase 4 handoff
 * notes for the full bundler investigation this came out of.
 */
import { network } from "hardhat";
import { parseEther } from "viem";

const DEPLOYER_SENDER = "0x3fab184622dc19b6109349b94811493bf2a45362";
const RAW_TX =
  "0xf8a58085174876e800830186a08080b853604580600e600039806000f350fe7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffe03601600081602082378035828234f58015156039578182fd5b8082525050506014600cf31ba02222222222222222222222222222222222222222222222222222222222222222a02222222222222222222222222222222222222222222222222222222222222222";
const FACTORY_ADDRESS = "0x4e59b44847b379578588920ca78fbf26c0b4956c";

const { viem } = await network.connect();
const publicClient = await viem.getPublicClient();
const [funder] = await viem.getWalletClients();

const existingCode = await publicClient.getCode({ address: FACTORY_ADDRESS });
if (existingCode && existingCode !== "0x") {
  console.log(`Deterministic deployer already present at ${FACTORY_ADDRESS}.`);
  process.exit(0);
}

const fundHash = await funder.sendTransaction({ to: DEPLOYER_SENDER, value: parseEther("1") });
await publicClient.waitForTransactionReceipt({ hash: fundHash });

const txHash = await publicClient.request({ method: "eth_sendRawTransaction", params: [RAW_TX as `0x${string}`] });
const receipt = await publicClient.waitForTransactionReceipt({ hash: txHash });
if (receipt.status !== "success") {
  throw new Error("FAIL: deterministic deployer factory transaction did not succeed");
}

const code = await publicClient.getCode({ address: FACTORY_ADDRESS });
if (!code || code === "0x") {
  throw new Error(`FAIL: no code at ${FACTORY_ADDRESS} after deployment`);
}

console.log(`Deployed deterministic deployer factory at ${FACTORY_ADDRESS} (${code.length} bytes of code).`);
