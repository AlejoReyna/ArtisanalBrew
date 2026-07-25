/**
 * Sponsored delegation: the MetaMask Delegation Framework v1.3.0 session-key path and the
 * VerifyingPaymaster sponsorship path, proven working TOGETHER through real .NET code.
 *
 * Why this script exists: `metamask-session-key-e2e.ts` proves the delegation path completely, but
 * contains no paymaster reference at all - every operation there is user-paid out of the agent
 * account's own prefund. `crossstack-bundler-submit-check.ts` proves sponsorship, but only for a
 * plain SimpleAccount transfer. Until now nothing proved an agent could spend under a constrained
 * delegation *without holding gas money*, which is the actual product story.
 *
 * The security question this has to answer is not "does it work" but:
 *
 *     Does paying an agent's gas let it make a payment its delegation does not authorise?
 *
 * It must not, and the two mechanisms are deliberately orthogonal (see
 * docs/erc4337-session-key-provenance.md: "Gas sponsorship is deliberately outside this authority
 * ... Neither path grants asset-payment rights"). This script proves that orthogonality the only
 * way worth proving it - by showing the sponsorship layer genuinely CANNOT tell the two apart:
 *
 *   - Case 1 (in-scope): the agent redeems the exact approve+fund delegations it was granted. The
 *     paymaster pays. The escrow is funded. The agent's own EntryPoint deposit is untouched.
 *   - Case 2 (out-of-scope): the agent tries to redeem a payment for the WRONG AMOUNT. From the
 *     sponsorship policy's point of view this is the same operation - same sender, same target
 *     (DelegationManager), same selector (redeemDelegations) - so the policy approves the gas and
 *     signs for it, exactly as in case 1. The payment still fails, because the only thing that can
 *     stop it is the on-chain ExactCalldata caveat, and that is not part of the sponsorship layer.
 *
 * Case 2 passing is what makes case 1 safe. If the sponsorship policy had been the thing rejecting
 * case 2, that would be an accident of configuration rather than a security boundary, and a change
 * to the allowlist would silently widen the agent's spending authority.
 *
 * Both cases run through the REAL production classes (UserOperationSponsor, SponsorshipPolicyService,
 * UserOperationSubmitter, RundlerBundlerClient, EntryPointConfirmationReader) via
 * ThisCafeteria.CrossStackHarness - not stubs, and not viem's bundler client.
 *
 * Setup:
 *   npx hardhat node --network hardhat --port 8546 &
 *   npx hardhat run scripts/deploy.ts --network arbitrumLocal
 *   <rundler> node --chain_spec scripts/rundler-chain-spec-local.toml \
 *     --node_http http://127.0.0.1:8546 --signer.private_keys <funded key> --rpc.port 4338 --unsafe
 *   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/crossstack-sponsored-delegation-check.ts
 */
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { network } from "hardhat";
import {
  createPublicClient, encodeFunctionData, http, parseEther, toHex, zeroAddress,
  type Address, type Hex
} from "viem";
import {
  ExecutionMode, Implementation, contracts as delegationContracts,
  createDelegation, createExecution, toMetaMaskSmartAccount,
  type DeleGatorEnvironment, type Delegation
} from "@metamask/delegation-toolkit";
import { deployDeleGatorEnvironment } from "@metamask/delegation-toolkit/utils";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const SPONSOR_PROJECT = "../../tools/ThisCafeteria.CrossStackHarness";
const RPC_URL = process.env.RPC_URL ?? "http://127.0.0.1:8546";
const BUNDLER_URL = process.env.BUNDLER_URL ?? "http://127.0.0.1:4338";
const PAYMENT = parseEther("10");

const entryPoint = manifest.addresses.entryPoint as Address;
const paymaster = manifest.addresses.verifyingPaymaster as Address;

const { viem } = await network.connect();
const hardhatPublicClient = await viem.getPublicClient();
const [deployer, owner, agent, provider, evaluator, treasury, outsider] = await viem.getWalletClients();
const chain = hardhatPublicClient.chain!;

const entryPointAbi = (await viem.deployContract("EntryPointFixture")).abi;

// --- Delegation environment, bound to this repo's own pinned v0.7 EntryPoint ---
const environment = await deployDeleGatorEnvironment(
  deployer as never, hardhatPublicClient as never, chain, { EntryPoint: entryPoint } as Record<string, Address>
) as DeleGatorEnvironment;
assert.equal(environment.EntryPoint.toLowerCase(), entryPoint.toLowerCase());

const publicClient = createPublicClient({ chain, transport: http(RPC_URL) });
const ownerAccount = await toMetaMaskSmartAccount({
  client: publicClient, implementation: Implementation.Hybrid,
  deployParams: [owner.account.address, [], [], []], deploySalt: toHex(3003n),
  signer: { walletClient: owner }, environment
});
const agentAccount = await toMetaMaskSmartAccount({
  client: publicClient, implementation: Implementation.Hybrid,
  deployParams: [agent.account.address, [], [], []], deploySalt: toHex(4004n),
  signer: { walletClient: agent }, environment
});
const ownerAddress = await ownerAccount.getAddress();
const agentAddress = await agentAccount.getAddress();

// The owner is funded because the owner still pays for its OWN operations (job creation, epoch
// activation) - those are not the thing under test. The AGENT is deliberately given only a token
// amount: it must not be able to pay for its own operation, so a successful redemption can only
// mean the paymaster really paid.
await deployer.sendTransaction({ to: ownerAddress, value: parseEther("5") });
await deployer.sendTransaction({ to: agentAddress, value: parseEther("0.001") });

// Fund the paymaster's own EntryPoint deposit. This script must not depend on another proof having
// run first and left a deposit behind - it did, once, and passed for the wrong reason on a dirty
// node before failing on a fresh one.
await deployer.writeContract({
  address: entryPoint, abi: entryPointAbi, functionName: "depositTo",
  args: [paymaster], value: parseEther("1")
});

// --- Escrow, token, and a funded job, mirroring metamask-session-key-e2e.ts ---
const token = await viem.deployContract("TestCafeToken", [deployer.account.address, parseEther("1000000")]);
const escrow = await viem.deployContract("AgenticCommerceEscrow", [token.address, treasury.account.address, 100n, zeroAddress]);
await deployer.writeContract({ address: token.address, abi: token.abi, functionName: "mint", args: [ownerAddress, parseEther("100")] });

const escrowAbi = escrow.abi;
const tokenAbi = token.abi;
const block = await publicClient.getBlock();
const expiry = block.timestamp + 3600n;

// The owner's own operations still go through viem's bundler client - they are setup, not the
// subject of this proof, and reusing the e2e's proven path here keeps the new surface small.
const { createBundlerClient } = await import("viem/account-abstraction");
const viemBundler = createBundlerClient({ client: publicClient, transport: http(BUNDLER_URL) });
async function ownerOp(calls: readonly { to: Address; data?: Hex; value?: bigint }[]) {
  const hash = await viemBundler.sendUserOperation({ account: ownerAccount, calls });
  const receipt = await viemBundler.waitForUserOperationReceipt({ hash, timeout: 60_000 });
  assert.equal(receipt.success, true, `owner setup operation ${hash} must succeed`);
  return receipt;
}

const createJobData = encodeFunctionData({
  abi: escrowAbi, functionName: "createJob",
  args: [provider.account.address, evaluator.account.address, expiry, "sponsored-session-payment"]
});
const created = await ownerOp([{ to: escrow.address, data: createJobData }]);
const createdLogs = await publicClient.getContractEvents({
  address: escrow.address, abi: escrowAbi, eventName: "JobCreated",
  fromBlock: created.receipt.blockNumber, toBlock: created.receipt.blockNumber
}) as any[];
const jobId = createdLogs[0]?.args?.jobId as bigint;
assert.ok(jobId, "setup must create the escrow job");
await provider.writeContract({ address: escrow.address, abi: escrowAbi, functionName: "setBudget", args: [jobId, PAYMENT, "0x"] });

// --- Two exact, epoch-bound, one-use delegations: approve + fund ---
const nonceEnforcerAbi = (await import("@metamask/delegation-abis")).NonceEnforcer.abi;
const previousEpoch = await publicClient.readContract({
  address: environment.caveatEnforcers.NonceEnforcer as Address, abi: nonceEnforcerAbi,
  functionName: "currentNonce", args: [environment.DelegationManager as Address, ownerAddress]
}) as bigint;
const permissionEpoch = previousEpoch + 1n;

const approveData = encodeFunctionData({ abi: tokenAbi, functionName: "approve", args: [escrow.address, PAYMENT] });
const fundData = encodeFunctionData({ abi: escrowAbi, functionName: "fund", args: [jobId, PAYMENT, "0x"] });

function unsignedPermission(target: Address, callData: Hex, salt: bigint) {
  return createDelegation({
    environment, to: agentAddress, from: ownerAddress, salt: toHex(salt),
    scope: { type: "functionCall", targets: [target], selectors: [callData.slice(0, 10) as Hex], exactCalldata: { calldata: callData } },
    caveats: [
      { type: "nonce", nonce: toHex(permissionEpoch, { size: 32 }) },
      { type: "timestamp", afterThreshold: Number(block.timestamp - 1n), beforeThreshold: Number(expiry) },
      { type: "limitedCalls", limit: 1 }
    ]
  });
}
async function signPermission(permission: Delegation): Promise<Delegation> {
  const { signature: _, ...signable } = permission;
  return { ...permission, signature: await ownerAccount.signDelegation({ delegation: signable }) };
}
const approvePermission = await signPermission(unsignedPermission(token.address, approveData, 21n));
const fundPermission = await signPermission(unsignedPermission(escrow.address, fundData, 22n));

// Owner activates the epoch on-chain; before this the permissions are unusable.
const incrementNonceData = delegationContracts.NonceEnforcer.encode.incrementNonce(environment.DelegationManager as Address);
await ownerOp([{ to: environment.caveatEnforcers.NonceEnforcer as Address, data: incrementNonceData }]);
const activeNonce = await publicClient.readContract({
  address: environment.caveatEnforcers.NonceEnforcer as Address, abi: nonceEnforcerAbi,
  functionName: "currentNonce", args: [environment.DelegationManager as Address, ownerAddress]
});
assert.equal(activeNonce, permissionEpoch, "owner must activate the signed nonce epoch on-chain");

function redemptionCall(permissionChains: Delegation[][], executions: ReturnType<typeof createExecution>[][]) {
  return {
    to: environment.DelegationManager as Address,
    data: delegationContracts.DelegationManager.encode.redeemDelegations({
      delegations: permissionChains, modes: permissionChains.map(() => ExecutionMode.SingleDefault), executions
    })
  };
}

// --- The .NET harness, driving the real sponsor and the real submitter ---
const packUint = (hi: bigint, lo: bigint): Hex => `0x${hi.toString(16).padStart(32, "0")}${lo.toString(16).padStart(32, "0")}` as Hex;
const VERIFICATION_GAS = 2_000_000n;
const CALL_GAS = 2_000_000n;
const PRE_VERIFICATION_GAS = 500_000n;
const MAX_FEE = 2_000_000_000n;
const accountGasLimits = packUint(VERIFICATION_GAS, CALL_GAS);
const gasFees = packUint(MAX_FEE, MAX_FEE);
const opPath = "/tmp/crossstack-sponsored-delegation-op.json";

// A file-backed harness database, so the grant's reverted-operation count survives across the
// separate `dotnet run` processes below. Case 3 depends on exactly that accumulation.
const HARNESS_DB = `/tmp/crossstack-sponsored-delegation-${Date.now()}.sqlite`;
const MAX_REVERTED = "2";

function runHarness(mode: string, body: Record<string, unknown>, extraEnv: Record<string, string> = {}) {
  writeFileSync(opPath, JSON.stringify(body, null, 2));
  const out = execFileSync("dotnet", ["run", "--project", SPONSOR_PROJECT, "--", opPath, mode], {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    env: {
      ...process.env, CROSSSTACK_RPC_URL: RPC_URL, CROSSSTACK_OWNER_ADDRESS: agentAddress.toLowerCase(),
      CROSSSTACK_DB_PATH: HARNESS_DB, CROSSSTACK_MAX_REVERTED_OPERATIONS: MAX_REVERTED, ...extraEnv
    }
  });
  return JSON.parse(out.trim().split("\n").filter(l => l.startsWith("{")).pop()!);
}

/** Splits the C# VerifyingPaymaster layout into viem's unpacked v0.7 paymaster fields. */
function unpackPaymasterAndData(paymasterAndData: Hex) {
  const raw = paymasterAndData.slice(2);
  return {
    paymaster: `0x${raw.slice(0, 40)}` as Address,
    paymasterVerificationGasLimit: BigInt(`0x${raw.slice(40, 72)}`),
    paymasterPostOpGasLimit: BigInt(`0x${raw.slice(72, 104)}`),
    paymasterData: `0x${raw.slice(104)}` as Hex
  };
}

const agentFactoryArgs = await agentAccount.getFactoryArgs();

/**
 * One sponsored agent operation, end to end: build -> real C# sponsor -> agent signs the
 * HybridDeleGator's own typed UserOperation -> real C# submitter -> Rundler.
 *
 * `label` distinguishes the in-scope and out-of-scope cases; both take the identical path on
 * purpose, which is the whole point of the proof.
 */
async function sponsoredAgentOperation(label: string, call: { to: Address; data: Hex }) {
  const callData = await agentAccount.encodeCalls([{ to: call.to, data: call.data }]);
  const nonce = await publicClient.readContract({
    address: entryPoint, abi: entryPointAbi, functionName: "getNonce", args: [agentAddress, 0n]
  }) as bigint;
  const deployed = await publicClient.getCode({ address: agentAddress });
  const needsInitCode = !deployed || deployed === "0x";
  const initCode: Hex = needsInitCode && agentFactoryArgs.factory
    ? `0x${agentFactoryArgs.factory.slice(2)}${agentFactoryArgs.factoryData!.slice(2)}` as Hex
    : "0x";

  const base = {
    sender: agentAddress, nonce: nonce.toString(), initCode, callData,
    accountGasLimits, preVerificationGas: PRE_VERIFICATION_GAS.toString(), gasFees,
    // What the sponsorship policy sees. Note it is IDENTICAL for the in-scope and out-of-scope
    // cases - the agent calls the same DelegationManager entry point either way.
    target: environment.DelegationManager as Address,
    selector: call.data.slice(0, 10) as Hex,
    entryPoint, accountFactory: agentFactoryArgs.factory ?? zeroAddress, paymaster
  };

  console.log(`  [${label}] sponsoring (real UserOperationSponsor, real gas simulation)...`);
  const sponsored = runHarness("approve", base);
  if (!sponsored.approved) {
    return { sponsored, submission: null as any };
  }
  console.log(`  [${label}] sponsorship APPROVED, costUsd=${sponsored.costUsd}`);

  // HybridDeleGator does not verify a plain personal_sign over the userOpHash the way SimpleAccount
  // does - it has its own typed UserOperation domain binding chain ID, account, and EntryPoint. So
  // the toolkit account signs, not a raw signMessage.
  const signature = await agentAccount.signUserOperation({
    sender: agentAddress, nonce, callData,
    callGasLimit: CALL_GAS, verificationGasLimit: VERIFICATION_GAS, preVerificationGas: PRE_VERIFICATION_GAS,
    maxFeePerGas: MAX_FEE, maxPriorityFeePerGas: MAX_FEE,
    ...(needsInitCode && agentFactoryArgs.factory ? { factory: agentFactoryArgs.factory, factoryData: agentFactoryArgs.factoryData } : {}),
    ...unpackPaymasterAndData(sponsored.paymasterAndData as Hex),
    signature: "0x" as Hex
  } as never);

  console.log(`  [${label}] submitting through Rundler (real UserOperationSubmitter)...`);
  const submission = runHarness("submit",
    { ...base, paymasterAndData: sponsored.paymasterAndData, costUsd: sponsored.costUsd, signature },
    { CROSSSTACK_BUNDLER_RPC_URL: BUNDLER_URL });
  return { sponsored, submission };
}

// ===== Case 1: in-scope redemption, fully sponsored =====
console.log("\n=== case 1: the agent redeems exactly what it was delegated, on sponsored gas ===");
const agentNativeBefore = await publicClient.getBalance({ address: agentAddress });
const paymasterDepositBefore = await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [paymaster]
}) as bigint;

const allowedRedemption = redemptionCall(
  [[approvePermission], [fundPermission]],
  [[createExecution({ target: token.address, callData: approveData })], [createExecution({ target: escrow.address, callData: fundData })]]
);
const inScope = await sponsoredAgentOperation("in-scope", allowedRedemption);
assert.ok(inScope.sponsored.approved, `sponsorship must approve the in-scope operation: ${inScope.sponsored.reason} ${inScope.sponsored.detail}`);
if (inScope.submission.status !== "Confirmed") {
  throw new Error(`FAIL: sponsored in-scope redemption did not confirm: ${inScope.submission.status} - ${inScope.submission.detail}`);
}
console.log(`  confirmed: userOpHash=${inScope.submission.userOpHash} tx=${inScope.submission.transactionHash}`);

// --- Independent on-chain verification ---
const fundedLogs = await publicClient.getContractEvents({
  address: escrow.address, abi: escrowAbi, eventName: "JobFunded",
  fromBlock: (await publicClient.getTransactionReceipt({ hash: inScope.submission.transactionHash as Hex })).blockNumber,
  toBlock: (await publicClient.getTransactionReceipt({ hash: inScope.submission.transactionHash as Hex })).blockNumber
}) as any[];
assert.equal(fundedLogs.length, 1, "exactly one JobFunded event must be emitted");
assert.equal(fundedLogs[0].args.jobId, jobId);
assert.equal(fundedLogs[0].args.client?.toLowerCase(), ownerAddress.toLowerCase(), "the OWNER's tokens are what got escrowed, not the agent's");
assert.equal(fundedLogs[0].args.amount, PAYMENT);

const agentDeposit = await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [agentAddress]
}) as bigint;
const agentNativeAfter = await publicClient.getBalance({ address: agentAddress });
const paymasterDepositAfter = await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [paymaster]
}) as bigint;

assert.equal(agentDeposit, 0n, "the agent must not have deposited or spent anything at the EntryPoint");
assert.equal(agentNativeAfter, agentNativeBefore, "the agent's own native balance must be untouched - the paymaster paid");
assert.ok(paymasterDepositAfter < paymasterDepositBefore, "the paymaster's EntryPoint deposit must have decreased");
console.log(`  agent EntryPoint deposit: ${agentDeposit} | agent native balance unchanged: ${agentNativeAfter === agentNativeBefore}`);
console.log(`  paymaster deposit paid: ${paymasterDepositBefore - paymasterDepositAfter} wei`);
console.log(`  JobFunded(jobId=${jobId}, client=owner, amount=${PAYMENT}) verified on-chain`);
console.log("  PASS: an agent holding no gas money made a delegated payment.");

// ===== Case 2: out-of-scope redemption, identically sponsored, still fails =====
console.log("\n=== case 2: same agent, same sponsorship, WRONG AMOUNT - must still fail ===");
const paymasterDepositBeforeAttack = await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [paymaster]
}) as bigint;
const wrongAmountData = encodeFunctionData({ abi: tokenAbi, functionName: "approve", args: [escrow.address, PAYMENT * 1000n] });
const outOfScope = await sponsoredAgentOperation("out-of-scope",
  redemptionCall([[approvePermission]], [[createExecution({ target: token.address, callData: wrongAmountData })]]));

// The crux. The sponsorship layer MUST NOT be what stops this - if it were, the boundary would be
// a configuration accident rather than an on-chain guarantee.
if (!outOfScope.sponsored.approved) {
  throw new Error(
    `FAIL: sponsorship denied the out-of-scope operation (${outOfScope.sponsored.reason}). That is the WRONG layer to ` +
    `catch it: it would mean this test proves nothing about the delegation caveats, and that widening the sponsorship ` +
    `allowlist would silently widen the agent's spending authority.`);
}
console.log("  sponsorship approved the gas (as it must - it cannot see the inner execution)");

if (outOfScope.submission.status === "Confirmed") {
  throw new Error("FAIL: an out-of-scope payment was CONFIRMED on sponsored gas. Delegation caveats are not being enforced.");
}
console.log(`  submission correctly failed: status=${outOfScope.submission.status}`);
console.log(`  detail: ${String(outOfScope.submission.detail).split("\n")[0].slice(0, 160)}`);

// The wrong-amount approval must not exist on-chain.
const allowance = await publicClient.readContract({
  address: token.address, abi: tokenAbi, functionName: "allowance", args: [ownerAddress, escrow.address]
}) as bigint;
assert.notEqual(allowance, PAYMENT * 1000n, "the out-of-scope allowance must never have been set");
console.log(`  owner->escrow allowance is ${allowance}, not the attempted ${PAYMENT * 1000n}`);

// --- Who paid for the failed attempt? ---
// A `Reverted` result means the operation was MINED and its inner call reverted, so under
// EntryPoint v0.7 the paymaster still pays for the gas. Meanwhile UserOperationSubmitter returns
// before RecordUsageAsync on that path, so nothing is debited from the owner's USD budget. Measure
// it rather than reason about it, and report the gap loudly instead of letting a green PASS imply
// the failed attempt was free.
const paymasterDepositAfterAttack = await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [paymaster]
}) as bigint;
const griefCostWei = paymasterDepositBeforeAttack - paymasterDepositAfterAttack;
assert.ok(griefCostWei > 0n, "a mined-but-reverted operation must still have cost the paymaster gas");
console.log(`  the failed attempt still cost the paymaster ${griefCostWei} wei (mined, inner call reverted)`);
console.log(`  grant state: revertedCount=${outOfScope.submission.grantRevertedCount} spentUsd=${outOfScope.submission.grantSpentUsd} revoked=${outOfScope.submission.grantRevoked}`);
// Compared against case 1's charge, not against zero: the harness database persists across these
// invocations, so the budget legitimately already carries the successful operation's cost.
assert.equal(outOfScope.submission.grantSpentUsd, inScope.submission.grantSpentUsd,
  "a revert has no successful operation to price, so it must not move the USD budget");
assert.equal(outOfScope.submission.grantRevertedCount, 1, "the revert must be metered even though no budget was debited");

// ===== Case 3: reverts are metered, and enough of them revoke the grant =====
//
// Without this, the budget is only a spend control against an HONEST grant-holder: every reverted
// operation costs the paymaster real gas while debiting nothing, so a valid grant could drain the
// deposit indefinitely. MaxRevertedOperations is what closes that, and this proves it end to end
// rather than only in SponsorshipPolicyServiceTests.
console.log(`\n=== case 3: repeated reverts revoke the grant (limit ${MAX_REVERTED}) ===`);
const secondRevert = await sponsoredAgentOperation("second-revert",
  redemptionCall([[approvePermission]], [[createExecution({ target: token.address, callData: wrongAmountData })]]));
assert.equal(secondRevert.submission.status, "Reverted", "the second out-of-scope attempt must also revert");
console.log(`  grant state: revertedCount=${secondRevert.submission.grantRevertedCount} revoked=${secondRevert.submission.grantRevoked}`);
assert.equal(secondRevert.submission.grantRevertedCount, 2);
assert.equal(secondRevert.submission.grantRevoked, true, `reaching ${MAX_REVERTED} reverts must revoke the grant`);
assert.ok(String(secondRevert.submission.detail).includes("revoked"),
  "the caller must be told its grant is gone, not just that one operation failed");

// The revocation has to actually stop the next sponsorship, not merely set a column.
const afterRevocation = await sponsoredAgentOperation("after-revocation", allowedRedemption);
assert.equal(afterRevocation.sponsored.approved, false, "sponsorship must be refused once the grant is revoked");
assert.equal(afterRevocation.sponsored.reason, "Revoked");
console.log(`  next sponsorship request refused: ${afterRevocation.sponsored.reason}`);
console.log("  PASS: reverted sponsored operations are metered and exhaust the grant.");

console.log("\nPASS: gas sponsorship and delegation authority are independent.");
console.log("      An agent with no gas money spent exactly what it was delegated, on the");
console.log("      paymaster's money; and the identical sponsorship, applied to a payment the");
console.log("      delegation did not authorise, bought it nothing - the on-chain caveat stopped");
console.log("      it, not the sponsorship policy. Reverted attempts are not free either: they");
console.log("      are metered against the grant and enough of them revoke it.");
console.log("CROSSSTACK_SPONSORED_DELEGATION_RESULT=PASS");
