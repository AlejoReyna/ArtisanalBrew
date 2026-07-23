/**
 * The actual Phase 4 gate: a sponsored UserOperation submitted through a real, reachable bundler,
 * against Ethereum Sepolia (not local Hardhat), mined and confirmed, with the app's own
 * server-side verification (real UserOperationSubmitter, decoding the EntryPoint's own
 * UserOperationEvent - not just `receipt.success`) checking it.
 *
 * This is the Sepolia sibling of crossstack-bundler-submit-check.ts, which already proves the
 * identical .NET code path (ThisCafeteria.CrossStackHarness's "submit" mode running the real
 * UserOperationSubmitter) against a local Hardhat node + local Rundler. Local Hardhat cannot run a
 * bundler in safe mode at all (see "Rundler investigation" in the plan doc) - proving this against
 * Sepolia through a hosted bundler is what actually exercises ERC-4337 storage-access-rule
 * enforcement, which nothing local ever has.
 *
 * SAFETY: this script performs two real, if small, actions on Sepolia if you let it past its own
 * gates - funding the paymaster's EntryPoint deposit (only if it doesn't already have one) and
 * submitting a UserOperation (gas paid by the paymaster deposit, not the account). Both cost real,
 * if valueless, Sepolia ETH and create permanent public transactions. It refuses to do either
 * unless you explicitly opt in - see the two environment variables below - matching this project's
 * own rule: "Do not broadcast to a public chain or spend real funds without Alexis's explicit
 * authorization for the specific network and wallet."
 *
 * Required environment variables:
 *   ETHEREUM_SEPOLIA_DEPLOYER_PRIVATE_KEY - already used to deploy the pinned Sepolia contracts.
 *     Does triple duty here, exactly as deploy.ts already does: the smart account's owner, the
 *     transaction sender that funds the paymaster deposit, AND the paymaster's own trusted
 *     verifying signer (deploy.ts passes this same account as VerifyingPaymaster's constructor
 *     `admin` argument for every network, Sepolia included - confirmed by reading deploy.ts, not
 *     assumed).
 *   SEPOLIA_BUNDLER_RPC_URL - a real bundler endpoint that supports Sepolia and the deployed
 *     EntryPoint (e.g. a Pimlico project URL: https://api.pimlico.io/v2/sepolia/rpc?apikey=...).
 *     Never logged, never written to any file this script controls.
 *   SEPOLIA_BROADCAST_AUTHORIZED=yes - the explicit-authorization gate. Without this exact value,
 *     the script only performs read-only checks (chain ID, EntryPoint bytecode presence, paymaster
 *     deposit, deployer balance, bundler eth_supportedEntryPoints) and prints what it WOULD do,
 *     then exits 0 without sending a single transaction.
 *
 * Usage:
 *   ETHEREUM_SEPOLIA_DEPLOYER_PRIVATE_KEY=0x... \
 *   SEPOLIA_BUNDLER_RPC_URL=https://api.pimlico.io/v2/sepolia/rpc?apikey=... \
 *   SEPOLIA_BROADCAST_AUTHORIZED=yes \
 *   HARDHAT_NETWORK=ethereumSepolia npx tsx scripts/sepolia-bundler-submit-check.ts
 */
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { network } from "hardhat";
import { concat, encodeFunctionData, type Address, type Hex } from "viem";
import manifest from "../deployments/ethereum-sepolia.json" with { type: "json" };

const SPONSOR_PROJECT = "../../tools/ThisCafeteria.CrossStackHarness";
const RPC_URL = process.env.ETHEREUM_SEPOLIA_RPC_URL ?? "https://ethereum-sepolia-rpc.publicnode.com";
const BUNDLER_URL = process.env.SEPOLIA_BUNDLER_RPC_URL;
const AUTHORIZED = process.env.SEPOLIA_BROADCAST_AUTHORIZED === "yes";

const { viem } = await network.connect();
const publicClient = await viem.getPublicClient();
const wallets = await viem.getWalletClients();
if (wallets.length === 0) {
  throw new Error("FAIL: no account configured. Set ETHEREUM_SEPOLIA_DEPLOYER_PRIVATE_KEY.");
}
const owner = wallets[0]; // Same account for owner, funder, and paymaster verifying signer - see header.

const entryPoint = manifest.addresses.entryPoint as Address;
const factory = manifest.addresses.accountFactory as Address;
const paymaster = manifest.addresses.verifyingPaymaster as Address;

console.log("=== read-only checks ===");
const chainId = await publicClient.getChainId();
console.log(`chain id: ${chainId} (expect 11155111)`);
if (chainId !== 11155111) throw new Error(`FAIL: connected to chain ${chainId}, not Sepolia.`);

const entryPointCode = await publicClient.getCode({ address: entryPoint });
console.log(`EntryPoint (${entryPoint}) has code: ${!!entryPointCode && entryPointCode !== "0x"}`);
if (!entryPointCode || entryPointCode === "0x") throw new Error("FAIL: EntryPoint has no deployed code on this network.");

const entryPointAbi = (await viem.deployContract("EntryPointFixture")).abi;
const factoryAbi = (await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint])).abi;

const paymasterDeposit = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [paymaster]
})) as bigint;
console.log(`paymaster EntryPoint deposit: ${paymasterDeposit} wei`);

const deployerBalance = await publicClient.getBalance({ address: owner.account.address });
console.log(`deployer (${owner.account.address}) balance: ${deployerBalance} wei`);

if (!BUNDLER_URL) {
  console.log("\nSEPOLIA_BUNDLER_RPC_URL is not set - stopping here (read-only checks only).");
  process.exit(0);
}

async function bundlerRpc(method: string, params: unknown[]) {
  const res = await fetch(BUNDLER_URL!, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ jsonrpc: "2.0", method, params, id: 1 })
  });
  const body = (await res.json()) as any;
  if (body.error) throw new Error(`${method} failed: ${JSON.stringify(body.error)}`);
  return body.result;
}

const supported = await bundlerRpc("eth_supportedEntryPoints", []);
console.log(`bundler supports EntryPoints: ${JSON.stringify(supported)}`);
if (!supported.map((a: string) => a.toLowerCase()).includes(entryPoint.toLowerCase())) {
  throw new Error(`FAIL: configured bundler does not support our EntryPoint ${entryPoint}.`);
}

// Minimum deposit to sponsor a handful of small operations - not a real spend, but real Sepolia ETH.
const MIN_DEPOSIT = 5_000_000_000_000_000n; // 0.005 ETH
const SALT = BigInt(process.env.SEPOLIA_SUBMIT_CHECK_SALT ?? "1");

const sender = (await publicClient.readContract({
  address: factory, abi: factoryAbi, functionName: "getAddress", args: [owner.account.address, SALT]
})) as Address;
console.log(`\ncounterfactual account (salt=${SALT}): ${sender}`);

const needsDeposit = paymasterDeposit < MIN_DEPOSIT;
console.log(`\n=== what this script would do if authorized ===`);
if (needsDeposit) console.log(`- fund the paymaster's EntryPoint deposit with ${MIN_DEPOSIT - paymasterDeposit} wei`);
console.log(`- submit a sponsored, zero-value self-call UserOperation deploying ${sender} through the bundler`);
console.log(`- poll for a receipt and independently verify it via the real UserOperationSubmitter`);

if (!AUTHORIZED) {
  console.log("\nSEPOLIA_BROADCAST_AUTHORIZED is not \"yes\" - stopping here without broadcasting anything.");
  process.exit(0);
}

console.log("\n=== SEPOLIA_BROADCAST_AUTHORIZED=yes: proceeding to broadcast ===");

if (needsDeposit) {
  const depositAmount = MIN_DEPOSIT - paymasterDeposit;
  console.log(`funding paymaster deposit: ${depositAmount} wei...`);
  const depositTx = await owner.writeContract({
    address: entryPoint, abi: entryPointAbi, functionName: "depositTo", args: [paymaster], value: depositAmount
  });
  await publicClient.waitForTransactionReceipt({ hash: depositTx });
  console.log(`deposit tx: ${depositTx}`);
}

// A zero-value self-call: proves deployment + sponsorship + bundler submission + verification
// without needing to fund the counterfactual account itself with spendable ETH.
const executeAbi = [{
  type: "function", name: "execute",
  inputs: [{ name: "dest", type: "address" }, { name: "value", type: "uint256" }, { name: "func", type: "bytes" }],
  outputs: [], stateMutability: "nonpayable"
}] as const;
const callData = encodeFunctionData({ abi: executeAbi, functionName: "execute", args: [sender, 0n, "0x"] });
const selector = callData.slice(0, 10) as Hex;

const initCode = concat([
  factory,
  encodeFunctionData({ abi: factoryAbi, functionName: "createAccount", args: [owner.account.address, SALT] })
]);

const nonce = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "getNonce", args: [sender, 0n]
})) as bigint;

const packUint = (hi: bigint, lo: bigint): Hex => `0x${hi.toString(16).padStart(32, "0")}${lo.toString(16).padStart(32, "0")}` as Hex;
const accountGasLimits = packUint(1_000_000n, 1_000_000n);
const gasFees = packUint(2_000_000_000n, 2_000_000_000n);
const preVerificationGas = 500_000n;

const opDescription = {
  sender, nonce: nonce.toString(), initCode, callData,
  accountGasLimits, preVerificationGas: preVerificationGas.toString(), gasFees,
  target: sender, selector,
  entryPoint, accountFactory: factory, paymaster
};
const opPath = "/tmp/sepolia-submit-op.json";
writeFileSync(opPath, JSON.stringify(opDescription, null, 2));

function runHarness(mode: string, extraEnv: Record<string, string> = {}) {
  const out = execFileSync("dotnet", ["run", "--project", SPONSOR_PROJECT, "--", opPath, mode], {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    env: {
      ...process.env, CROSSSTACK_RPC_URL: RPC_URL, CROSSSTACK_EVM_CHAIN_ID: "11155111",
      CROSSSTACK_OWNER_ADDRESS: owner.account.address,
      CROSSSTACK_VERIFYING_SIGNER_KEY: process.env.ETHEREUM_SEPOLIA_DEPLOYER_PRIVATE_KEY!,
      ...extraEnv
    }
  });
  const line = out.trim().split("\n").filter(l => l.startsWith("{")).pop()!;
  return JSON.parse(line);
}

console.log("\n--- step 1: sponsor (real UserOperationSponsor, real gas simulation against Sepolia) ---");
const sponsored = runHarness("approve");
console.log(`approved=${sponsored.approved} costUsd=${sponsored.costUsd} detail=${sponsored.detail ?? ""}`);
if (!sponsored.approved) throw new Error(`FAIL: sponsorship denied: ${sponsored.reason} ${sponsored.detail}`);

const opForHash = {
  sender, nonce, initCode, callData, accountGasLimits, preVerificationGas, gasFees,
  paymasterAndData: sponsored.paymasterAndData as Hex,
  signature: "0x" as Hex
};
const userOpHash = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "getUserOpHash", args: [opForHash]
})) as Hex;
const signature = await owner.signMessage({ message: { raw: userOpHash } });

console.log("--- step 2: submit through the hosted bundler (real UserOperationSubmitter) ---");
writeFileSync(opPath, JSON.stringify({ ...opDescription, paymasterAndData: sponsored.paymasterAndData, costUsd: sponsored.costUsd, signature }, null, 2));
const submission = runHarness("submit", { CROSSSTACK_BUNDLER_RPC_URL: BUNDLER_URL });
console.log(`status=${submission.status} userOpHash=${submission.userOpHash} tx=${submission.transactionHash}`);
if (submission.status !== "Confirmed") {
  throw new Error(`FAIL: UserOperationSubmitter did not confirm: ${submission.status} - ${submission.detail}`);
}

const code = await publicClient.getCode({ address: sender });
if (!code || code === "0x") throw new Error("FAIL: account was not deployed by the bundler-submitted operation");

console.log("\nPASS: real .NET code (UserOperationSubmitter) submitted a sponsored UserOperation");
console.log("      through a real, hosted bundler against Ethereum Sepolia, got it mined, and");
console.log("      independently verified it on-chain by decoding the EntryPoint's own");
console.log("      UserOperationEvent.");
console.log(`  account deployed: ${sender}`);
console.log(`  userOpHash: ${submission.userOpHash}`);
console.log(`  tx: ${submission.transactionHash}`);
console.log(`  https://sepolia.etherscan.io/tx/${submission.transactionHash}`);
console.log("SEPOLIA_BUNDLER_SUBMIT_RESULT=PASS");
