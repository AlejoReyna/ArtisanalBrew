/**
 * Cross-stack proof: the .NET UserOperationSponsor produces a paymaster signature, and the
 * canonical on-chain VerifyingPaymaster must accept it.
 *
 * This is deliberately not a self-consistent test. The hash is computed by the Solidity paymaster,
 * the signature is produced by C#, and the EntryPoint decides. A divergence between the two stacks
 * shows up as a rejected operation rather than two components agreeing with each other.
 */
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { network } from "hardhat";
import { concat, encodeFunctionData, encodePacked, parseEther, type Address, type Hex } from "viem";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const SPONSOR_PROJECT =
  "/private/tmp/claude-501/-Users-alexis-dev-monSite/3a229c88-22fd-4e32-98c0-89516adaa68c/scratchpad/sponsorcheck";

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
  // The inner call's target/selector are what the sponsorship policy checks.
  target, selector,
  estimatedGas: "2000000", gasPriceWei: "10000000000",
  entryPoint, accountFactory: factory, paymaster
};

const opPath = "/tmp/crossstack-op.json";
writeFileSync(opPath, JSON.stringify(opDescription, null, 2));

function askDotnetSponsor(mode: string) {
  const out = execFileSync("dotnet", ["run", "--project", SPONSOR_PROJECT, "--", opPath, mode], {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"]
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
