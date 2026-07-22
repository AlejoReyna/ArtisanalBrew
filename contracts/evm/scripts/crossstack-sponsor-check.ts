/**
 * Cross-stack proof: the .NET UserOperationSponsor (using the real UserOperationSimulator, not a
 * stub) measures real gas cost via the canonical EntryPoint's own simulation, prices it, asks the
 * sponsorship policy, and produces a paymaster signature that the canonical on-chain
 * VerifyingPaymaster must accept.
 *
 * This is deliberately not a self-consistent test. The gas cost is measured by the Solidity
 * EntryPoint's own simulateHandleOp, the sponsorship hash is computed by the Solidity paymaster,
 * the signature is produced by C#, and the EntryPoint decides whether to accept it. A divergence
 * anywhere in that chain shows up as a rejected operation rather than components agreeing with
 * each other.
 */
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { network } from "hardhat";
import { concat, encodeFunctionData, encodePacked, parseEther, type Address, type Hex } from "viem";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const SPONSOR_PROJECT = "../../tools/ThisCafeteria.CrossStackHarness";

// Must match hardhat.config.ts's networks exactly - this script's own viem/hardhat calls connect
// via HARDHAT_NETWORK, but the harness below is a separate OS process with no Hardhat context of
// its own, so its RPC target has to be derived and passed through explicitly rather than assumed.
const RPC_URLS_BY_NETWORK: Record<string, string> = {
  localhost: "http://127.0.0.1:8545",
  arbitrumLocal: "http://127.0.0.1:8546",
  baseLocal: "http://127.0.0.1:8547"
};
const rpcUrl = RPC_URLS_BY_NETWORK[process.env.HARDHAT_NETWORK ?? "localhost"] ?? RPC_URLS_BY_NETWORK.localhost;

const { viem } = await network.connect();
const publicClient = await viem.getPublicClient();
const [owner, beneficiary, recipient] = await viem.getWalletClients();

const entryPoint = manifest.addresses.entryPoint as Address;
const factory = manifest.addresses.accountFactory as Address;
const paymaster = manifest.addresses.verifyingPaymaster as Address;

const entryPointAbi = (await viem.deployContract("EntryPointFixture")).abi;
const factoryAbi = (await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint])).abi;

const SALT = 0n;
const packUint = (hi: bigint, lo: bigint): Hex => encodePacked(["uint128", "uint128"], [hi, lo]);

const sender = (await publicClient.readContract({
  address: factory, abi: factoryAbi, functionName: "getAddress", args: [owner.account.address, SALT]
})) as Address;

const initCode = concat([
  factory,
  encodeFunctionData({ abi: factoryAbi, functionName: "createAccount", args: [owner.account.address, SALT] })
]);

const executeAbi = [{
  type: "function", name: "execute",
  inputs: [{ name: "dest", type: "address" }, { name: "value", type: "uint256" }, { name: "func", type: "bytes" }],
  outputs: [], stateMutability: "nonpayable"
}] as const;

const target = recipient.account.address;
const callData = encodeFunctionData({ abi: executeAbi, functionName: "execute", args: [target, 0n, "0x"] });
const selector = callData.slice(0, 10) as Hex;

const nonce = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "getNonce", args: [sender, 0n]
})) as bigint;

const accountGasLimits = packUint(1_000_000n, 1_000_000n);
const gasFees = packUint(1_000_000_000n, 10_000_000_000n);
const preVerificationGas = 100_000n;

// Fund the paymaster's EntryPoint deposit so it can actually sponsor.
await beneficiary.writeContract({
  address: entryPoint, abi: entryPointAbi, functionName: "depositTo",
  args: [paymaster], value: parseEther("1")
});

const opDescription = {
  sender, nonce: nonce.toString(), initCode, callData,
  accountGasLimits, preVerificationGas: preVerificationGas.toString(), gasFees,
  // The inner call's target/selector are what the sponsorship policy checks. There is
  // deliberately no cost/gas-estimate field here: UserOperationSponsor derives it itself by
  // calling the real UserOperationSimulator against this same live node.
  target, selector,
  entryPoint, accountFactory: factory, paymaster
};

const opPath = "/tmp/crossstack-op.json";
writeFileSync(opPath, JSON.stringify(opDescription, null, 2));

function askDotnetSponsor(mode: string) {
  const out = execFileSync("dotnet", ["run", "--project", SPONSOR_PROJECT, "--", opPath, mode], {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    env: { ...process.env, CROSSSTACK_RPC_URL: rpcUrl }
  });
  const line = out.trim().split("\n").filter(l => l.startsWith("{")).pop()!;
  return JSON.parse(line);
}

// --- Case 1: policy denies (target not on allowlist) -> no signature at all ---
console.log("--- case 1: disallowed target ---");
const denied = askDotnetSponsor("wrongtarget");
console.log(`approved=${denied.approved} reason=${denied.reason} paymasterAndData='${denied.paymasterAndData}'`);
if (denied.approved) throw new Error("FAIL: policy approved a disallowed target");
if (denied.paymasterAndData !== "") throw new Error("FAIL: a denied request still produced paymasterAndData");
console.log("PASS: denial produced no signature");

// --- Case 2: policy approves -> the on-chain paymaster must accept the C# signature ---
console.log("--- case 2: approved sponsorship, submitted on-chain ---");
const approved = askDotnetSponsor("approve");
console.log(`approved=${approved.approved} costUsd=${approved.costUsd}`);
if (!approved.approved) throw new Error(`FAIL: policy denied a valid request: ${approved.reason} ${approved.detail}`);
// Sanity bound on a real measured cost, not proof of an exact value (real gas usage can shift
// with compiler/version changes) — this guards against a stub silently creeping back in place of
// genuine simulation, which would either produce 0 or an implausibly large number.
if (!(approved.costUsd > 0 && approved.costUsd < 1000)) {
  throw new Error(`FAIL: costUsd=${approved.costUsd} is not a plausible measured cost`);
}

const op = {
  sender, nonce, initCode, callData, accountGasLimits, preVerificationGas, gasFees,
  paymasterAndData: approved.paymasterAndData as Hex,
  signature: "0x" as Hex
};

const userOpHash = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "getUserOpHash", args: [op]
})) as Hex;
op.signature = await owner.signMessage({ message: { raw: userOpHash } });

const depositBefore = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [paymaster]
})) as bigint;

const hash = await beneficiary.writeContract({
  address: entryPoint, abi: entryPointAbi, functionName: "handleOps",
  args: [[op], beneficiary.account.address], gas: 3_000_000n
});
const receipt = await publicClient.waitForTransactionReceipt({ hash });
if (receipt.status !== "success") throw new Error("FAIL: handleOps reverted");

const code = await publicClient.getCode({ address: sender });
if (!code || code === "0x") throw new Error("FAIL: account was not deployed");

const senderDeposit = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [sender]
})) as bigint;
if (senderDeposit !== 0n) throw new Error("FAIL: the account paid; it should have been sponsored");

const depositAfter = (await publicClient.readContract({
  address: entryPoint, abi: entryPointAbi, functionName: "balanceOf", args: [paymaster]
})) as bigint;
if (depositAfter >= depositBefore) throw new Error("FAIL: paymaster deposit did not pay for gas");

console.log(`PASS: on-chain paymaster accepted the C#-produced signature`);
console.log(`  account deployed: ${sender}`);
console.log(`  account deposit spent: ${senderDeposit} (sponsored)`);
console.log(`  paymaster deposit delta: -${depositBefore - depositAfter} wei`);
console.log(`  tx: ${hash}`);
console.log("CROSSSTACK_RESULT=PASS");
