/**
 * Phase 5 gate: "the two-node smoke test moves the configured test asset from the Arbitrum-like
 * node to the Base-like smart account, then funds the job. Failure leaves the job Open and
 * recoverable."
 *
 * This runs against TWO SEPARATE Hardhat node processes (not one node simulating two chains):
 *   - arbitrumLocal — the source chain. Holds the test asset and the resolver's submit/refund side.
 *   - baseLocal — the destination chain. Holds the ERC-4337 stack, the ERC-8183 escrow, and the
 *     resolver's fill side.
 *
 * Both networks are configured with chainId 31337 in hardhat.config.ts, matching what these node
 * processes actually report — Hardhat 3's `node --chain-id` CLI flag does not change what an
 * EDR-simulated node reports (confirmed during this work), so declaring a different chainId in the
 * network config would make Hardhat's own connection validator (HHE708) refuse to connect. This
 * doesn't undermine the test: neither resolver contract reads block.chainid, so "cross-chain" here
 * is enforced by running on two genuinely independent node processes, not a chain-ID label.
 *
 * The "solver" step is performed inline by this script rather than a separate service — it reads
 * the source chain's IntentSubmitted event and acts on it, exactly as an off-chain solver process
 * would, just without the network hop of a separate process. That is an honest simplification for
 * a smoke test, not a claim that a solver service exists.
 *
 * Requires two Hardhat nodes already running:
 *   npx hardhat node --port 8546 &
 *   npx hardhat node --port 8547 &
 *   npx tsx scripts/two-node-crosschain-smoke.ts
 * (this script connects to "arbitrumLocal" and "baseLocal" explicitly by name via
 * network.connect() — no --network flag or HARDHAT_NETWORK env var affects which networks it uses.)
 */
import { network } from "hardhat";
import { encodeFunctionData, parseEther, zeroAddress, type Address, type Hex } from "viem";

const source = await network.connect("arbitrumLocal");
const destination = await network.connect("baseLocal");

const sourcePublic = await source.viem.getPublicClient();
const destPublic = await destination.viem.getPublicClient();

const [sourceUser] = await source.viem.getWalletClients();
const [destAdmin, destSolver, destProvider, destEvaluator] = await destination.viem.getWalletClients();

// --- Source chain setup: the test asset being moved, and the resolver's submit/refund side ---
// TestCafeToken's constructor takes (admin, cap) and mints nothing — mint explicitly.
const sourceToken = await source.viem.deployContract("TestCafeToken", [sourceUser.account.address, parseEther("1000")]);
await sourceUser.writeContract({
  address: sourceToken.address, abi: sourceToken.abi, functionName: "mint",
  args: [sourceUser.account.address, parseEther("1000")]
});
const sourceResolver = await source.viem.deployContract("ERC7683ResolverFixture");
await sourceUser.writeContract({
  address: sourceToken.address, abi: sourceToken.abi, functionName: "approve",
  args: [sourceResolver.address, parseEther("1000")]
});

// --- Destination chain setup: the ERC-4337 stack, the ERC-8183 escrow, and the resolver's fill side ---
const destToken = await destination.viem.deployContract("TestCafeToken", [destAdmin.account.address, parseEther("1000")]);
await destAdmin.writeContract({
  address: destToken.address, abi: destToken.abi, functionName: "mint",
  args: [destAdmin.account.address, parseEther("1000")]
});
// Destination-side fill only — see ERC7683DestinationResolverFixture.sol for why the destination
// chain cannot share ERC7683ResolverFixture's isSubmitted-gated single-instance design.
const destResolver = await destination.viem.deployContract("ERC7683DestinationResolverFixture");
const entryPoint = await destination.viem.deployContract("EntryPointFixture");
const factory = await destination.viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint.address]);
const escrow = await destination.viem.deployContract("AgenticCommerceEscrow", [
  destToken.address, destAdmin.account.address, 100n, zeroAddress
]);

await destAdmin.writeContract({
  address: destToken.address, abi: destToken.abi, functionName: "transfer",
  args: [destSolver.account.address, parseEther("500")]
});
await destSolver.writeContract({
  address: destToken.address, abi: destToken.abi, functionName: "approve",
  args: [destResolver.address, parseEther("500")]
});

// The buyer's smart account on the destination chain: deployed for real (not counterfactual) so
// that execute() calls below actually run account code rather than silently no-opping against an
// empty address. Account creation via initCode is already proven in ERC4337UserOperation.test.ts;
// this smoke test is about the cross-chain funding path, not re-proving that.
const SALT = 0n;
const accountOwner = destAdmin; // the buyer, in this smoke test's cast of characters
const accountAddress = (await destPublic.readContract({
  address: factory.address, abi: factory.abi, functionName: "getAddress", args: [accountOwner.account.address, SALT]
})) as Address;
await accountOwner.writeContract({
  address: factory.address, abi: factory.abi, functionName: "createAccount", args: [accountOwner.account.address, SALT]
});
const accountCode = await destPublic.getCode({ address: accountAddress });
if (!accountCode || accountCode === "0x") throw new Error("FAIL: smart account was not deployed");

const executeAbi = [{
  type: "function", name: "execute",
  inputs: [{ name: "dest", type: "address" }, { name: "value", type: "uint256" }, { name: "func", type: "bytes" }],
  outputs: [], stateMutability: "nonpayable"
}] as const;

async function accountExecute(target: Address, data: Hex) {
  const hash = await accountOwner.writeContract({
    address: accountAddress, abi: executeAbi, functionName: "execute", args: [target, 0n, data]
  });
  const receipt = await destPublic.waitForTransactionReceipt({ hash });
  if (receipt.status !== "success") throw new Error(`FAIL: account.execute to ${target} reverted`);
}

const escrowAbi = escrow.abi;
async function readJob(jobId: bigint) {
  return destPublic.readContract({ address: escrow.address, abi: escrowAbi, functionName: "jobs", args: [jobId] }) as Promise<readonly [bigint, Address, Address, Address, string, bigint, bigint, number]>;
}

async function createOpenJobFundedByAccount(budget: bigint, description: string): Promise<bigint> {
  const future = (await destPublic.getBlock()).timestamp + 3600n;
  // createJob's own `provider` argument sets job.provider immediately if non-zero — pass the
  // zero address here so provider assignment is a genuinely separate setProvider() step below,
  // matching the two-step lifecycle the Phase 3 acceptance harness proves.
  await accountExecute(escrow.address, encodeFunctionData({
    abi: escrowAbi, functionName: "createJob", args: [zeroAddress, destEvaluator.account.address, future, description]
  }));
  const logs = await destPublic.getContractEvents({
    address: escrow.address, abi: escrowAbi, eventName: "JobCreated", fromBlock: 0n
  });
  const jobId = logs[logs.length - 1].args.jobId!;

  await accountExecute(escrow.address, encodeFunctionData({
    abi: escrowAbi, functionName: "setProvider", args: [jobId, destProvider.account.address]
  }));
  await accountExecute(escrow.address, encodeFunctionData({
    abi: escrowAbi, functionName: "setBudget", args: [jobId, budget, "0x"]
  }));

  const job = await readJob(jobId);
  if (job[7] !== 0) throw new Error(`FAIL: job ${jobId} should be Open (0), was ${job[7]}`);
  return jobId;
}

// =============================================================================================
// Case 1: happy path — asset moves from the Arbitrum-like chain to the smart account on the
// Base-like chain, and only THEN is the job funded.
// =============================================================================================
console.log("--- case 1: cross-chain fill then job funding ---");

const budget = parseEther("10");
const jobId1 = await createOpenJobFundedByAccount(budget, "cross-chain-funded-job");
console.log(`job ${jobId1} created and Open on destination chain, client = smart account ${accountAddress}`);

const order1 = {
  user: sourceUser.account.address,
  sourceToken: sourceToken.address,
  amountIn: parseEther("10"),
  destinationChainId: 84532n,
  destinationToken: destToken.address,
  destinationReceiver: accountAddress,
  minAmountOut: budget,
  deadline: (await sourcePublic.getBlock()).timestamp + 3600n,
  nonce: 1n,
  allowedSolver: destSolver.account.address
};

const submitTx = await sourceUser.writeContract({
  address: sourceResolver.address, abi: sourceResolver.abi, functionName: "submitIntent", args: [order1]
});
const submitReceipt = await sourcePublic.waitForTransactionReceipt({ hash: submitTx });
const submittedLogs = await sourcePublic.getContractEvents({
  address: sourceResolver.address, abi: sourceResolver.abi, eventName: "IntentSubmitted",
  fromBlock: submitReceipt.blockNumber, toBlock: submitReceipt.blockNumber
});
if (submittedLogs.length !== 1) throw new Error("FAIL: expected exactly one IntentSubmitted event");
console.log(`intent submitted on SOURCE chain (Arbitrum-like), orderId ${submittedLogs[0].args.orderId}`);

const lockedBalance = await sourcePublic.readContract({
  address: sourceToken.address, abi: sourceToken.abi, functionName: "balanceOf", args: [sourceResolver.address]
}) as bigint;
if (lockedBalance !== order1.amountIn) throw new Error("FAIL: source resolver did not lock the intent's amountIn");

// --- Off-chain solver step: watches the source chain, fills on the destination chain ---
console.log("solver observed IntentSubmitted on source chain; filling on destination chain...");
const destAccountBalanceBefore = await destPublic.readContract({
  address: destToken.address, abi: destToken.abi, functionName: "balanceOf", args: [accountAddress]
}) as bigint;

const fillTx = await destSolver.writeContract({
  address: destResolver.address, abi: destResolver.abi, functionName: "fillIntent", args: [order1, budget]
});
const fillReceipt = await destPublic.waitForTransactionReceipt({ hash: fillTx });
if (fillReceipt.status !== "success") throw new Error("FAIL: fillIntent reverted on destination chain");

const destAccountBalanceAfter = await destPublic.readContract({
  address: destToken.address, abi: destToken.abi, functionName: "balanceOf", args: [accountAddress]
}) as bigint;
if (destAccountBalanceAfter - destAccountBalanceBefore !== budget) {
  throw new Error("FAIL: smart account did not receive the filled amount on the destination chain");
}
console.log(`smart account balance on DESTINATION chain (Base-like) increased by ${budget} wei — asset has moved chains`);

// --- Only now, with destination funds verified, fund the job ---
console.log("destination funds verified; funding the job from the smart account...");
await accountExecute(destToken.address, encodeFunctionData({
  abi: destToken.abi, functionName: "approve", args: [escrow.address, budget]
}));
await accountExecute(escrow.address, encodeFunctionData({
  abi: escrowAbi, functionName: "fund", args: [jobId1, budget, "0x"]
}));

const fundedJob = await readJob(jobId1);
if (fundedJob[7] !== 1) throw new Error(`FAIL: job ${jobId1} should be Funded (1), was ${fundedJob[7]}`);
const escrowBalance = await destPublic.readContract({
  address: destToken.address, abi: destToken.abi, functionName: "balanceOf", args: [escrow.address]
}) as bigint;
if (escrowBalance !== budget) throw new Error("FAIL: escrow did not receive the funded amount");

console.log(`PASS: job ${jobId1} is Funded using an asset that moved from the Arbitrum-like chain`);
console.log(`      to the Base-like smart account, and was only spent after that move was verified.`);

// =============================================================================================
// Case 2: failure path — the solver never fills. The job must stay Open, and the source-side
// intent must remain refundable (recoverable) rather than the source asset being stuck.
// =============================================================================================
console.log("--- case 2: unfilled intent leaves the job Open and the source asset recoverable ---");

const jobId2 = await createOpenJobFundedByAccount(parseEther("5"), "cross-chain-unfilled-job");

const shortDeadline = (await sourcePublic.getBlock()).timestamp + 10n;
const order2 = {
  user: sourceUser.account.address,
  sourceToken: sourceToken.address,
  amountIn: parseEther("5"),
  destinationChainId: 84532n,
  destinationToken: destToken.address,
  destinationReceiver: accountAddress,
  minAmountOut: parseEther("5"),
  deadline: shortDeadline,
  nonce: 2n,
  allowedSolver: destSolver.account.address
};

await sourceUser.writeContract({
  address: sourceResolver.address, abi: sourceResolver.abi, functionName: "submitIntent", args: [order2]
});
console.log("intent submitted on source chain; solver deliberately does not fill it");

// Captured AFTER submission (i.e. after the amountIn is locked into the resolver) so the
// post-refund delta below measures exactly what refundIntent returns, not the submission lock.
const beforeRefundBalance = await sourcePublic.readContract({
  address: sourceToken.address, abi: sourceToken.abi, functionName: "balanceOf", args: [sourceUser.account.address]
}) as bigint;

// Never call fillIntent on the destination chain — simulate the solver failing to act.

await (sourcePublic as any).request({ method: "evm_increaseTime", params: [20] });
await (sourcePublic as any).request({ method: "evm_mine", params: [] });

await sourceUser.writeContract({
  address: sourceResolver.address, abi: sourceResolver.abi, functionName: "refundIntent", args: [order2]
});

const afterRefundBalance = await sourcePublic.readContract({
  address: sourceToken.address, abi: sourceToken.abi, functionName: "balanceOf", args: [sourceUser.account.address]
}) as bigint;
if (afterRefundBalance - beforeRefundBalance !== order2.amountIn) {
  throw new Error("FAIL: user did not recover their source-chain asset after the unfilled intent expired");
}
console.log("source-chain asset refunded to the user — recoverable, not stuck");

const unfundedJob = await readJob(jobId2);
if (unfundedJob[7] !== 0) throw new Error(`FAIL: job ${jobId2} should remain Open (0) since it was never funded, was ${unfundedJob[7]}`);
console.log(`PASS: job ${jobId2} remains Open — no attempt was made to fund it without verified destination funds.`);

console.log("TWO_NODE_SMOKE_RESULT=PASS");
