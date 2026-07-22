/**
 * KNOWN FAILING — bundler integration attempt, kept for whoever continues this work.
 *
 * Goal: submit a genuine UserOperation to a locally-running Alto bundler via
 * eth_sendUserOperation and confirm it gets bundled and mined via eth_getUserOperationReceipt —
 * WITHOUT this script (or any funded EOA under this script's control) ever calling
 * EntryPoint.handleOps directly. That's the one thing none of the other cross-stack scripts in
 * this directory prove: an operation reaching the chain through an actual bundler mempool rather
 * than a hand-assembled batch-of-one.
 *
 * CURRENT STATUS: fails at eth_estimateUserOperationGas with "AA23 reverted (or OOG)".
 *
 * Root cause, confirmed by direct investigation (not assumed): @pimlico/alto does not call the
 * canonical `simulateHandleOp` from the pinned @account-abstraction/contracts@0.7.0 package (whose
 * real selector is 0x97b2dcb9, confirmed against this repo's own CanonicalEntryPointSimulations
 * artifact). Instead it substitutes its OWN proprietary simulation contract at the EntryPoint
 * address via an eth_call state override, calling selector 0xd6383f94 — a Pimlico-internal
 * interface that is not part of the ERC-4337 spec and is not documented. That contract is
 * evidently calibrated for the canonical, officially-deployed EntryPoint and does not tolerate our
 * locally-redeployed instance (same source, different address/deployment history), for reasons
 * that would require reverse-engineering Pimlico's closed-source-in-practice bundler internals to
 * pin down further.
 *
 * This is a different (and more specific) friction point than the "Hardhat's debug_traceCall
 * support is patchy" friction predicted before this was attempted — that prediction was about
 * safe-mode's storage-access tracer. This failure happens even with --safe-mode false, i.e. with
 * that entire code path skipped. The real obstacle is Alto's own gas-estimation contract, which
 * this codebase's rule against deploying anything but pinned, unmodified canonical contracts
 * cannot route around (adopting Alto's contract would mean depending on unpinned, undocumented,
 * proprietary bytecode).
 *
 * WHAT DID WORK, and is real progress worth keeping:
 * - Alto starts, listens, and correctly reports our deployed EntryPoint via
 *   eth_supportedEntryPoints — confirmed live.
 * - Alto's --safe-mode false genuinely runs real EntryPoint validation (not a no-op): a malformed
 *   signature produces the same "AA23 reverted" this session saw earlier via raw eth_call, proving
 *   it isn't skipping validation, only the storage-access tracer rules.
 * - Alto's built-in deterministic-CREATE2-factory self-deploy is separately broken in
 *   @pimlico/alto@0.0.20 (its hardcoded raw transaction's hex is corrupted — every "00" byte pair
 *   is the literal string "V"). Confirmed by manually deploying the real well-known transaction
 *   successfully. Worked around by contracts/evm/scripts/deploy-create2-factory.ts, which is a
 *   genuinely reusable fix independent of the deeper issue below.
 *
 * NEXT STEPS FOR WHOEVER PICKS THIS UP:
 * - Try Rundler (Alchemy's Rust bundler) instead of Alto — it isn't distributed via npm, so it
 *   wasn't attempted in this session, but it may use the standard EntryPointSimulations rather
 *   than a proprietary one.
 * - Or: skip local Hardhat bundler testing entirely and defer bundler integration to Base Sepolia,
 *   where a hosted bundler (Pimlico/Alchemy/Biconomy) simulates against the REAL canonical
 *   EntryPoint at its real address, which is exactly the case Alto's internals are built for.
 *
 * Setup this script assumes, if you want to keep debugging locally:
 *   npx hardhat node --chain-id 31337 &
 *   npm run deploy:local
 *   HARDHAT_NETWORK=localhost npx tsx scripts/deploy-create2-factory.ts
 *   npx alto --entrypoints <EntryPoint> --rpc-url http://127.0.0.1:8545 \
 *     --executor-private-keys <funded key> --utility-private-key <funded key> \
 *     --safe-mode false --port 4337
 *   HARDHAT_NETWORK=localhost npx tsx scripts/bundler-e2e-check.ts
 */
import { network } from "hardhat";
import { concat, encodeFunctionData, encodePacked, parseEther, type Address, type Hex } from "viem";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const BUNDLER_URL = "http://127.0.0.1:4337";

const { viem } = await network.connect();
const publicClient = await viem.getPublicClient();
const [owner, beneficiary, recipient] = await viem.getWalletClients();

const entryPoint = manifest.addresses.entryPoint as Address;
const factory = manifest.addresses.accountFactory as Address;

const entryPointAbi = (await viem.deployContract("EntryPointFixture")).abi;
const factoryAbi = (await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint])).abi;

const SALT = 999999n; // distinct from other scripts sharing this node
const packUint = (hi: bigint, lo: bigint): Hex => encodePacked(["uint128", "uint128"], [hi, lo]);

async function bundlerRpc(method: string, params: unknown[]) {
  const res = await fetch(BUNDLER_URL, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ jsonrpc: "2.0", method, params, id: 1 })
  });
  const body = (await res.json()) as any;
  if (body.error) throw new Error(`${method} failed: ${JSON.stringify(body.error)}`);
  return body.result;
}

const supported = await bundlerRpc("eth_supportedEntryPoints", []);
console.log("bundler supports EntryPoints:", supported);
if (!supported.map((a: string) => a.toLowerCase()).includes(entryPoint.toLowerCase())) {
  throw new Error(`FAIL: bundler does not support our EntryPoint ${entryPoint}`);
}

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

await beneficiary.sendTransaction({ to: sender, value: parseEther("0.5") });
await beneficiary.writeContract({
  address: entryPoint, abi: entryPointAbi, functionName: "depositTo", args: [sender], value: parseEther("0.5")
});

const nonce = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "getNonce", args: [sender, 0n]
})) as bigint;

const unsignedOp = {
  sender, nonce: `0x${nonce.toString(16)}`, initCode, callData,
  callGasLimit: `0x${(3_000_000n).toString(16)}`,
  verificationGasLimit: `0x${(3_000_000n).toString(16)}`,
  preVerificationGas: `0x${(500_000n).toString(16)}`,
  maxFeePerGas: `0x${(10_000_000_000n).toString(16)}`,
  maxPriorityFeePerGas: `0x${(1_000_000_000n).toString(16)}`,
  paymasterAndData: "0x",
  signature: "0x" as Hex
};

console.log("estimating gas via the bundler (eth_estimateUserOperationGas)...");
console.log("(expected to fail here — see the header comment for the diagnosed root cause)");

let gasEstimate: any;
try {
  gasEstimate = await bundlerRpc("eth_estimateUserOperationGas", [
    { ...unsignedOp, signature: await owner.signMessage({ message: { raw: `0x${"00".repeat(32)}` as Hex } }) },
    entryPoint
  ]);
} catch (err) {
  console.error("FAILED AS EXPECTED at eth_estimateUserOperationGas:");
  console.error((err as Error).message);
  console.error("");
  console.error("This is the known Alto-proprietary-simulation-contract incompatibility documented");
  console.error("in this file's header, not a bug in this script or in the UserOperation it built.");
  console.error("BUNDLER_E2E_RESULT=KNOWN_FAILURE");
  process.exit(1);
}

console.log("gas estimate from bundler:", gasEstimate);
console.log("(if you see this, the known failure above has been resolved — please update this");
console.log(" script's header comment and remove the try/catch, then finish the flow below.)");

const op = {
  sender, nonce, initCode, callData,
  accountGasLimits: packUint(BigInt(gasEstimate.verificationGasLimit), BigInt(gasEstimate.callGasLimit)),
  preVerificationGas: BigInt(gasEstimate.preVerificationGas),
  gasFees: packUint(1_000_000_000n, 10_000_000_000n),
  paymasterAndData: "0x" as Hex,
  signature: "0x" as Hex
};

const userOpHash = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "getUserOpHash", args: [op]
})) as Hex;
const signature = await owner.signMessage({ message: { raw: userOpHash } });

const sendResult = await bundlerRpc("eth_sendUserOperation", [
  {
    ...unsignedOp,
    callGasLimit: `0x${BigInt(gasEstimate.callGasLimit).toString(16)}`,
    verificationGasLimit: `0x${BigInt(gasEstimate.verificationGasLimit).toString(16)}`,
    preVerificationGas: `0x${BigInt(gasEstimate.preVerificationGas).toString(16)}`,
    signature
  },
  entryPoint
]);
console.log("eth_sendUserOperation returned userOpHash:", sendResult);
if (sendResult.toLowerCase() !== userOpHash.toLowerCase()) {
  throw new Error(`FAIL: bundler-returned hash ${sendResult} does not match locally computed hash ${userOpHash}`);
}

console.log("polling eth_getUserOperationReceipt (bundler must mine it on its own)...");
const recipientBalanceBefore = await publicClient.getBalance({ address: recipient.account.address });

let receipt: any = null;
for (let attempt = 0; attempt < 30 && !receipt; attempt++) {
  await new Promise((r) => setTimeout(r, 1000));
  receipt = await bundlerRpc("eth_getUserOperationReceipt", [userOpHash]);
}
if (!receipt) throw new Error("FAIL: bundler never produced a receipt within 30s");

console.log("receipt.success:", receipt.success);
console.log("receipt.receipt.transactionHash:", receipt.receipt.transactionHash);
if (!receipt.success) throw new Error("FAIL: UserOperation execution was not successful");

const code = await publicClient.getCode({ address: sender });
if (!code || code === "0x") throw new Error("FAIL: account was not deployed by the bundler-submitted operation");

const recipientBalanceAfter = await publicClient.getBalance({ address: recipient.account.address });
if (recipientBalanceAfter - recipientBalanceBefore !== transferValue) {
  throw new Error("FAIL: recipient did not receive the transferred value");
}

console.log("PASS: a real bundler (Alto) accepted, bundled, and got mined a UserOperation this");
console.log("      script never submitted directly to the EntryPoint.");
console.log("BUNDLER_E2E_RESULT=PASS");
