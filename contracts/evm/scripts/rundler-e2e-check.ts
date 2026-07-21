/**
 * PASSING — Rundler bundler integration, verified live end-to-end.
 *
 * Goal: submit a genuine UserOperation to a locally-running Rundler bundler via
 * eth_sendUserOperation and confirm it gets bundled and mined via eth_getUserOperationReceipt —
 * WITHOUT this script (or any funded EOA under this script's control) ever calling
 * EntryPoint.handleOps directly. Same goal as bundler-e2e-check.ts (Alto), which is a documented
 * KNOWN FAILURE against this repo's pinned canonical contracts — see that file's header. This
 * script proves the same thing works with Rundler (Alchemy's Rust bundler).
 *
 * WHY RUNDLER SUCCEEDS WHERE ALTO FAILED: Alto's gas estimation substitutes a proprietary,
 * undocumented simulation contract calibrated for the canonical mainnet EntryPoint deployment
 * history, which rejects a locally-redeployed (same source, different address) instance. Rundler's
 * chain-spec system (a TOML file, see rundler-chain-spec-local.toml alongside this script) lets you
 * declare the actual deployed EntryPoint address directly — no proprietary contract involved.
 *
 * THREE REAL, DIAGNOSED ISSUES HAD TO BE FIXED TO GET HERE (not guessed — each confirmed against
 * Rundler's own error messages, in order encountered):
 *
 * 1. ERC-4337 v0.7 JSON-RPC schema split. The v0.7 JSON-RPC UserOperation schema splits `initCode`
 *    into separate `factory`/`factoryData` fields (the on-chain PackedUserOperation struct still
 *    concatenates them into one `initCode` bytes value — only the RPC layer changed). Alto's RPC
 *    layer tolerates the old combined-initCode shape; Rundler enforces the split-field schema
 *    strictly and rejects combined initCode with "-32602 Invalid user operation for entry point".
 *
 * 2. Hardhat's default EIP-7825 (Osaka hardfork) transaction gas cap of 16,777,216 rejects Rundler's
 *    simulation eth_call, which deliberately requests ~550,000,000 gas (standard bundler practice:
 *    a high ceiling that lets simulation distinguish a real revert from an artificial out-of-gas).
 *    Fixed in hardhat.config.ts by setting `transactionGasCap: 1_000_000_000n` on the `hardhat`
 *    network entry (raising, not disabling, the cap — `false` would only fall back to the block
 *    gas limit, itself well under 550M). Note the `hardhat node` CLI task defaults to a network
 *    named "node", NOT "hardhat" — you must pass `--network hardhat` explicitly for this network
 *    config entry to apply.
 *
 * 3. Rundler's default (safe-mode, i.e. without --unsafe) validation calls debug_traceCall with a custom JS tracer (the
 *    standard ERC-4337 storage-access-rule tracer) to enforce ERC-7562 validation rules. Hardhat's
 *    EDR engine (Rust-based) recognizes the tracer/tracerConfig RPC fields but does not implement
 *    JS-tracer execution — confirmed by the literal strings "JS Tracer is not enabled" and
 *    "unsupported tracer" present in the compiled EDR native binary itself (the platform-specific
 *    edr.node file under node_modules for the "@nomicfoundation/edr" family of packages). This is
 *    architecturally the same class of limitation that blocks Alto's safe-mode locally
 *    (see that file's header) — not fixable via
 *    config. Worked around with Rundler's own `--unsafe` flag (its equivalent of Alto's
 *    `--safe-mode false`), which skips tracer-based validation while still performing full
 *    EntryPoint signature/nonce/deposit validation via plain eth_call.
 *
 * CAVEAT, stated plainly: this PASS is with Rundler in `--unsafe` mode. Locally, that is the only
 * option — Hardhat cannot run the standard tracer either way. It means storage-access-rule
 * enforcement (the ERC-4337 anti-DoS rules about what a paymaster/account may read/write during
 * validation) is not exercised by this proof. A hosted bundler against a real chain (Base Sepolia
 * or mainnet) would run in safe mode by default, against a node that does support the tracer. This
 * proof establishes that Rundler correctly bundles and mines a UserOperation against this repo's
 * real, pinned, unmodified canonical EntryPoint/factory — not that storage-access rules are
 * enforced locally, which no local Hardhat-based setup (Alto or Rundler) can currently prove.
 *
 * Setup this script assumes:
 *   npx hardhat node --network hardhat --port 8546 &
 *   npx hardhat run scripts/deploy.ts --network arbitrumLocal
 *   <rundler binary> node \
 *     --chain_spec contracts/evm/scripts/rundler-chain-spec-local.toml \
 *     --node_http http://127.0.0.1:8546 \
 *     --signer.private_keys <funded key> \
 *     --rpc.port 4338 \
 *     --unsafe
 *   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/rundler-e2e-check.ts
 */
import { network } from "hardhat";
import { encodeFunctionData, encodePacked, parseEther, type Address, type Hex } from "viem";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const BUNDLER_URL = "http://127.0.0.1:4338";

const { viem } = await network.connect();
const publicClient = await viem.getPublicClient();
const [owner, beneficiary, recipient] = await viem.getWalletClients();

const entryPoint = manifest.addresses.entryPoint as Address;
const factory = manifest.addresses.accountFactory as Address;

const entryPointAbi = (await viem.deployContract("EntryPointFixture")).abi;
const factoryAbi = (await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint])).abi;

const SALT = BigInt(process.env.SIMPLE_ACCOUNT_E2E_SALT ?? "888888");
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

const factoryData = encodeFunctionData({
  abi: factoryAbi, functionName: "createAccount", args: [owner.account.address, SALT]
});

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

// v0.7 JSON-RPC schema: factory/factoryData split, unlike the packed on-chain struct below.
const unsignedOp = {
  sender, nonce: `0x${nonce.toString(16)}`,
  factory, factoryData,
  callData,
  callGasLimit: `0x${(1_000_000n).toString(16)}`,
  verificationGasLimit: `0x${(1_000_000n).toString(16)}`,
  preVerificationGas: `0x${(500_000n).toString(16)}`,
  maxFeePerGas: `0x${(2_000_000_000n).toString(16)}`,
  maxPriorityFeePerGas: `0x${(2_000_000_000n).toString(16)}`,
  signature: "0x" as Hex
};

console.log("estimating gas via the bundler (eth_estimateUserOperationGas)...");
const gasEstimate = await bundlerRpc("eth_estimateUserOperationGas", [
  { ...unsignedOp, signature: await owner.signMessage({ message: { raw: `0x${"00".repeat(32)}` as Hex } }) },
  entryPoint
]);
console.log("gas estimate from bundler:", gasEstimate);

// On-chain packed struct still concatenates factory+factoryData into one initCode bytes value.
const packedInitCode = (factory + factoryData.slice(2)) as Hex;
const op = {
  sender, nonce, initCode: packedInitCode, callData,
  accountGasLimits: packUint(BigInt(gasEstimate.verificationGasLimit), BigInt(gasEstimate.callGasLimit)),
  preVerificationGas: BigInt(gasEstimate.preVerificationGas),
  gasFees: packUint(2_000_000_000n, 2_000_000_000n),
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

console.log("PASS: a real bundler (Rundler) accepted, bundled, and got mined a UserOperation this");
console.log("      script never submitted directly to the EntryPoint.");
console.log("RUNDLER_E2E_RESULT=PASS");
