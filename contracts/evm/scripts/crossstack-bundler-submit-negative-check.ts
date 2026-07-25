/**
 * Negative half of the plan's Phase 4 gate: "over-budget, wrong-target, wrong-selector, expired, and
 * revoked operations fail" - proven through the REAL production code path.
 *
 * These five cases were already unit-tested at the policy layer (SponsorshipPolicyServiceTests) and
 * one of them (an unapproved sponsorship) was already proven through the real submitter by
 * crossstack-bundler-submit-denied-check.ts. Neither of those is quite the gate:
 *
 *   - the policy unit tests never touch UserOperationSponsor, a real chain, or the submitter, so
 *     they prove the rule, not that the rule is actually reached and honoured in the real path;
 *   - the denied-check FABRICATES the denial (SponsorshipSignature.Deny(...)) rather than provoking
 *     it, so it proves the submitter refuses an unapproved signature but proves nothing about which
 *     conditions actually produce one.
 *
 * This script closes that gap. For each case it rigs exactly one real input (an allowlist entry, a
 * grant budget, a per-operation cap, a validity window, a revocation), runs the REAL
 * UserOperationSponsor against a live chain with real gas simulation, and hands whatever
 * SponsorshipSignature that really produces to the REAL UserOperationSubmitter. It then asserts
 * three things per case:
 *
 *   1. sponsorship was denied,
 *   2. for the SPECIFIC expected reason - not merely "some denial happened", so a case that starts
 *      failing for an unrelated reason (a broken simulation, a missing grant) is caught rather than
 *      silently counted as a pass,
 *   3. the submitter returned `Denied` without ever contacting the bundler.
 *
 * (3) is enforced structurally, the same way crossstack-bundler-submit-denied-check.ts does it: the
 * bundler URL points at 127.0.0.1:1, where nothing listens. Any submission attempt surfaces as a
 * loud connection failure instead of a passing test.
 *
 * A live node IS required (unlike the denied-check) because UserOperationSponsor simulates gas
 * before it evaluates policy - the denials here are only meaningful if simulation genuinely
 * succeeded first, which the script also asserts by rejecting a SimulationFailed reason.
 * A running Rundler is NOT required: nothing should ever reach a bundler.
 *
 * Setup:
 *   npx hardhat node --network hardhat --port 8546 &
 *   npx hardhat run scripts/deploy.ts --network arbitrumLocal
 *   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/crossstack-bundler-submit-negative-check.ts
 */
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { network } from "hardhat";
import { concat, encodeFunctionData, parseEther, type Address, type Hex } from "viem";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const SPONSOR_PROJECT = "../../tools/ThisCafeteria.CrossStackHarness";
const RPC_URL = "http://127.0.0.1:8546";
const UNREACHABLE_BUNDLER_URL = "http://127.0.0.1:1"; // port 1 is reserved (tcpmux); nothing binds it.

/** Each case rigs exactly one input; `reason` is the SponsorshipDenialReason it must produce. */
const CASES = [
  { name: "wrongtarget", reason: "DisallowedTarget", rigs: "an allowlist that omits the operation's target" },
  { name: "wrongselector", reason: "DisallowedSelector", rigs: "an allowlist that omits the operation's selector" },
  { name: "overbudget", reason: "OverBudget", rigs: "a grant budget below the measured cost" },
  { name: "overcap", reason: "OperationTooExpensive", rigs: "a per-operation cap below the measured cost" },
  { name: "expired", reason: "Expired", rigs: "a grant validity window that has already closed" },
  { name: "revoked", reason: "Revoked", rigs: "a grant revoked through the real RevokeAsync" }
] as const;

const { viem } = await network.connect();
const publicClient = await viem.getPublicClient();
const [owner, beneficiary, recipient] = await viem.getWalletClients();

const entryPoint = manifest.addresses.entryPoint as Address;
const factory = manifest.addresses.accountFactory as Address;
const paymaster = manifest.addresses.verifyingPaymaster as Address;

const entryPointAbi = (await viem.deployContract("EntryPointFixture")).abi;
const factoryAbi = (await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint])).abi;

// A distinct salt from the other cross-stack proofs so this one derives its own counterfactual
// account on a shared deterministic deployment. Nothing here ever deploys it - no operation is
// submitted - but the address must not collide with an account another proof already deployed,
// which would change what gas simulation measures.
const SALT = BigInt(process.env.NEGATIVE_CHECK_SALT ?? "525252");
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

// Same funding as crossstack-bundler-submit-check.ts, and for the same reason: gas simulation runs
// with an empty paymasterAndData (the paymaster signature does not exist yet at that point), so the
// account itself must look able to pay for simulation to succeed. A denial that came from a failed
// simulation would prove nothing about the policy rule under test.
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
const opPath = "/tmp/crossstack-submit-negative-op.json";
writeFileSync(opPath, JSON.stringify(opDescription, null, 2));

// --- Baseline: the very same operation IS approvable. Without this, every "denied" below could be
// explained by the operation being broken rather than by the rule under test. ---
console.log("--- baseline: the unrigged operation is approved (so denials below mean the rule fired) ---");
const baselineOut = execFileSync("dotnet", ["run", "--project", SPONSOR_PROJECT, "--", opPath, "approve"], {
  encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
  env: { ...process.env, CROSSSTACK_RPC_URL: RPC_URL }
});
const baseline = JSON.parse(baselineOut.trim().split("\n").filter(l => l.startsWith("{")).pop()!);
if (!baseline.approved) {
  throw new Error(`FAIL: baseline sponsorship was denied (${baseline.reason}: ${baseline.detail}) - fix the setup before trusting any negative case below.`);
}
console.log(`baseline approved, measured costUsd=${baseline.costUsd}`);

let failures = 0;
for (const testCase of CASES) {
  console.log(`\n--- case "${testCase.name}": ${testCase.rigs} ---`);
  const out = execFileSync("dotnet", ["run", "--project", SPONSOR_PROJECT, "--", opPath, "negative"], {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    env: {
      ...process.env,
      CROSSSTACK_RPC_URL: RPC_URL,
      CROSSSTACK_BUNDLER_RPC_URL: UNREACHABLE_BUNDLER_URL,
      CROSSSTACK_NEGATIVE_CASE: testCase.name
    }
  });
  const result = JSON.parse(out.trim().split("\n").filter(l => l.startsWith("{")).pop()!);
  console.log(`approved=${result.approved} reason=${result.reason} submissionStatus=${result.submissionStatus}`);

  const problems: string[] = [];
  if (result.approved) problems.push("sponsorship was APPROVED; this case must be denied");
  if (result.reason === "SimulationFailed") problems.push(`denied by a failed simulation, not by policy: ${result.detail}`);
  if (result.reason !== testCase.reason) problems.push(`expected reason "${testCase.reason}", got "${result.reason}"`);
  if (result.submissionStatus !== "Denied") problems.push(`expected submissionStatus "Denied", got "${result.submissionStatus}" (${result.submissionDetail})`);

  if (problems.length > 0) {
    failures++;
    for (const problem of problems) console.error(`  FAIL: ${problem}`);
  } else {
    console.log(`  PASS: denied as ${result.reason}, and the submitter refused it without contacting the bundler`);
  }
}

if (failures > 0) {
  throw new Error(`FAIL: ${failures} of ${CASES.length} negative cases did not behave as the Phase 4 gate requires.`);
}

console.log(`\nPASS: all ${CASES.length} negative cases denied for their own specific reason through the`);
console.log("      real UserOperationSponsor + SponsorshipPolicyService, and the real");
console.log("      UserOperationSubmitter refused every one of them without ever contacting a");
console.log("      bundler - confirmed by each run completing cleanly against an address nothing");
console.log("      listens on, rather than failing with a connection error.");
console.log("CROSSSTACK_BUNDLER_SUBMIT_NEGATIVE_RESULT=PASS");
