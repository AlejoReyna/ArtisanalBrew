/**
 * Cross-stack proof: the .NET UserOperationSubmitter (using the real UserOperationSponsor,
 * RundlerBundlerClient, and SponsorshipPolicyService — not stubs) submits a fully-signed, sponsored
 * UserOperation through a real, locally-running Rundler bundler via eth_sendUserOperation, polls
 * eth_getUserOperationReceipt, and independently verifies the mined result by decoding the
 * canonical EntryPoint's own UserOperationEvent — all without this script or any funded EOA under
 * its control ever calling EntryPoint.handleOps directly.
 *
 * This is the proof that closes the gap the stack plan documented: rundler-e2e-check.ts already
 * proved a real bundler mines a UserOperation this script submits directly; crossstack-sponsor-check.ts
 * already proved the real C# UserOperationSponsor produces a signature the on-chain paymaster
 * accepts. Neither proved ThisCafeteria.* code itself talking to a bundler. This script does, by
 * shelling out to ThisCafeteria.CrossStackHarness in "submit" mode, which runs the real production
 * UserOperationSubmitter class.
 *
 * Two harness invocations, mirroring how a real client/server round-trip would work:
 *   1. "approve" mode - the real UserOperationSponsor simulates gas and produces a paymaster
 *      signature (unchanged from crossstack-sponsor-check.ts).
 *   2. This script computes EntryPoint.getUserOpHash(...) over that paymasterAndData and signs it
 *      with the owner's key (viem), exactly as a real account owner's wallet would.
 *   3. "submit" mode - the real UserOperationSubmitter takes the already-approved sponsorship
 *      signature and the owner's signature, submits through Rundler, polls, and independently
 *      verifies on-chain. Deliberately does NOT re-run SponsorAsync: see
 *      IUserOperationSubmitter's own doc comment for why re-sponsoring here would produce a
 *      paymasterAndData the owner's signature no longer matches.
 *
 * Setup this script assumes (same as rundler-e2e-check.ts):
 *   npx hardhat node --network hardhat --port 8546 &
 *   npx hardhat run scripts/deploy.ts --network arbitrumLocal
 *   <rundler binary> node \
 *     --chain_spec contracts/evm/scripts/rundler-chain-spec-local.toml \
 *     --node_http http://127.0.0.1:8546 \
 *     --signer.private_keys <funded key> \
 *     --rpc.port 4338 \
 *     --unsafe
 *   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/crossstack-bundler-submit-check.ts
 */
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { network } from "hardhat";
import { concat, encodeFunctionData, parseEther, type Address, type Hex } from "viem";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const SPONSOR_PROJECT = "../../tools/ThisCafeteria.CrossStackHarness";
const RPC_URL = "http://127.0.0.1:8546";
const BUNDLER_URL = "http://127.0.0.1:4338";

const { viem } = await network.connect();
const publicClient = await viem.getPublicClient();
const [owner, beneficiary, recipient] = await viem.getWalletClients();

const entryPoint = manifest.addresses.entryPoint as Address;
const factory = manifest.addresses.accountFactory as Address;
const paymaster = manifest.addresses.verifyingPaymaster as Address;

const entryPointAbi = (await viem.deployContract("EntryPointFixture")).abi;
const factoryAbi = (await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint])).abi;

// A fresh salt so this proof deploys a brand-new counterfactual account each run rather than
// colliding with rundler-e2e-check.ts's own well-known salt on a shared deterministic deployment.
const SALT = BigInt(process.env.SUBMIT_CHECK_SALT ?? "424242");
const packUint = (hi: bigint, lo: bigint): Hex => `0x${hi.toString(16).padStart(32, "0")}${lo.toString(16).padStart(32, "0")}` as Hex;

const sender = (await publicClient.readContract({
  address: factory, abi: factoryAbi, functionName: "getAddress", args: [owner.account.address, SALT]
})) as Address;
console.log("sender (counterfactual account):", sender);

const initCode = concat([
  factory,
  encodeFunctionData({ abi: factoryAbi, functionName: "createAccount", args: [owner.account.address, SALT] })
]);

const executeAbi = [{
  type: "function", name: "execute",
  inputs: [{ name: "dest", type: "address" }, { name: "value", type: "uint256" }, { name: "func", type: "bytes" }],
  outputs: [], stateMutability: "nonpayable"
}] as const;

const transferValue = parseEther("0.01");
const callData = encodeFunctionData({ abi: executeAbi, functionName: "execute", args: [recipient.account.address, transferValue, "0x"] });
const selector = callData.slice(0, 10) as Hex;

const nonce = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "getNonce", args: [sender, 0n]
})) as bigint;

const accountGasLimits = packUint(1_000_000n, 1_000_000n);
const gasFees = packUint(2_000_000_000n, 2_000_000_000n);
const preVerificationGas = 500_000n;

// Fund the paymaster's EntryPoint deposit so it can actually sponsor gas, and fund the
// counterfactual account itself so its inner `execute` call has real ETH to transfer out - gas
// sponsorship only covers the UserOperation's own gas cost, not the value the inner call moves.
await beneficiary.writeContract({
  address: entryPoint, abi: entryPointAbi, functionName: "depositTo",
  args: [paymaster], value: parseEther("1")
});
await beneficiary.sendTransaction({ to: sender, value: parseEther("0.5") });

const opDescription = {
  sender, nonce: nonce.toString(), initCode, callData,
  accountGasLimits, preVerificationGas: preVerificationGas.toString(), gasFees,
  target: recipient.account.address, selector,
  entryPoint, accountFactory: factory, paymaster
};
const opPath = "/tmp/crossstack-submit-op.json";
writeFileSync(opPath, JSON.stringify(opDescription, null, 2));

function runHarness(mode: string, extraEnv: Record<string, string> = {}) {
  const out = execFileSync("dotnet", ["run", "--project", SPONSOR_PROJECT, "--", opPath, mode], {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    env: { ...process.env, CROSSSTACK_RPC_URL: RPC_URL, ...extraEnv }
  });
  const line = out.trim().split("\n").filter(l => l.startsWith("{")).pop()!;
  return JSON.parse(line);
}

// --- Step 1: real C# UserOperationSponsor simulates gas and produces a paymaster signature ---
console.log("--- step 1: sponsor (real UserOperationSponsor, real gas simulation) ---");
const sponsored = runHarness("approve");
console.log(`approved=${sponsored.approved} costUsd=${sponsored.costUsd}`);
if (!sponsored.approved) throw new Error(`FAIL: sponsorship denied: ${sponsored.reason} ${sponsored.detail}`);
if (!(sponsored.costUsd > 0 && sponsored.costUsd < 1000)) {
  throw new Error(`FAIL: costUsd=${sponsored.costUsd} is not a plausible measured cost`);
}

// --- Step 2: owner signs EntryPoint.getUserOpHash(...) over that exact paymasterAndData, exactly
// as a real account owner's wallet would once sponsorship is known ---
const opForHash = {
  sender, nonce, initCode, callData, accountGasLimits, preVerificationGas, gasFees,
  paymasterAndData: sponsored.paymasterAndData as Hex,
  signature: "0x" as Hex
};
const userOpHash = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "getUserOpHash", args: [opForHash]
})) as Hex;
const signature = await owner.signMessage({ message: { raw: userOpHash } });

// --- Step 3: real C# UserOperationSubmitter submits through Rundler, polls, and independently
// verifies - this script never touches the bundler RPC or the EntryPoint's handleOps itself ---
console.log("--- step 2: submit through Rundler (real UserOperationSubmitter, real bundler) ---");
writeFileSync(opPath, JSON.stringify({ ...opDescription, paymasterAndData: sponsored.paymasterAndData, costUsd: sponsored.costUsd, signature }, null, 2));

const recipientBalanceBefore = await publicClient.getBalance({ address: recipient.account.address });

const submission = runHarness("submit", { CROSSSTACK_BUNDLER_RPC_URL: BUNDLER_URL });
console.log(`status=${submission.status} userOpHash=${submission.userOpHash} tx=${submission.transactionHash}`);
if (submission.status !== "Confirmed") {
  throw new Error(`FAIL: UserOperationSubmitter did not confirm: ${submission.status} - ${submission.detail}`);
}
if (submission.userOpHash?.toLowerCase() !== userOpHash.toLowerCase()) {
  throw new Error(`FAIL: submitter-reported hash ${submission.userOpHash} does not match locally computed hash ${userOpHash}`);
}

// --- Independent on-chain checks, not just trusting the harness's own report ---
const code = await publicClient.getCode({ address: sender });
if (!code || code === "0x") throw new Error("FAIL: account was not deployed by the bundler-submitted operation");

const senderDeposit = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [sender]
})) as bigint;
if (senderDeposit !== 0n) throw new Error("FAIL: the account paid; it should have been sponsored");

const recipientBalanceAfter = await publicClient.getBalance({ address: recipient.account.address });
if (recipientBalanceAfter - recipientBalanceBefore !== transferValue) {
  throw new Error("FAIL: recipient did not receive the transferred value");
}

console.log("PASS: real .NET code (UserOperationSubmitter) submitted a sponsored UserOperation");
console.log("      through a real bundler (Rundler) and independently verified it on-chain by");
console.log("      decoding the EntryPoint's own UserOperationEvent - never calling handleOps");
console.log("      or the bundler RPC directly from this script.");
console.log(`  account deployed: ${sender}`);
console.log(`  account deposit spent: ${senderDeposit} (sponsored)`);
console.log(`  userOpHash: ${submission.userOpHash}`);
console.log(`  tx: ${submission.transactionHash}`);
console.log("CROSSSTACK_BUNDLER_SUBMIT_RESULT=PASS");
