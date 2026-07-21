/**
 * Proves the eth_call state-override recipe for ERC-4337 gas simulation works against Hardhat's
 * node, before porting the same recipe to C#. Not part of the permanent test suite.
 */
import { network } from "hardhat";
import { concat, encodeFunctionData, encodePacked, parseEther, type Address, type Hex } from "viem";
import manifest from "../deployments/evm-local.json" with { type: "json" };
import simulationsArtifact from "../artifacts/contracts/AccountAbstractionCanonical.sol/CanonicalEntryPointSimulations.json" with { type: "json" };

const { viem } = await network.connect();
const publicClient = await viem.getPublicClient();
const [owner, beneficiary, recipient] = await viem.getWalletClients();

const entryPoint = manifest.addresses.entryPoint as Address;
const factory = manifest.addresses.accountFactory as Address;

const entryPointAbi = (await viem.deployContract("EntryPointFixture")).abi;
const factoryAbi = (await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint])).abi;
const simulationsAbi = simulationsArtifact.abi;
const simulationsDeployedBytecode = simulationsArtifact.deployedBytecode as Hex;

const SALT = 1234n; // distinct from other scripts sharing this node
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

const callData = encodeFunctionData({ abi: executeAbi, functionName: "execute", args: [recipient.account.address, 0n, "0x"] });

// Fund the sender's deposit so simulateHandleOp doesn't fail on "didn't pay prefund".
await beneficiary.writeContract({
  address: entryPoint, abi: entryPointAbi, functionName: "depositTo", args: [sender], value: parseEther("1")
});

// simulateHandleOp tolerates a signature that FAILS validation (it "ignores signature error"),
// but only when that failure is a returned SIG_VALIDATION_FAILED, not a revert. An all-zero
// signature makes ecrecover return address(0), which OpenZeppelin's ECDSA library treats as a
// revert (ECDSAInvalidSignature), not a return value - so "AA23 reverted" still surfaces. A
// genuinely valid ECDSA signature over ANY message recovers to a real (non-zero) address; since it
// won't match the account owner, validateUserOp returns SIG_VALIDATION_FAILED instead of reverting.
const dummySignature = await beneficiary.signMessage({ message: "gas-estimation-placeholder" });

const op = {
  sender, nonce: 0n, initCode, callData,
  accountGasLimits: packUint(1_000_000n, 1_000_000n),
  preVerificationGas: 100_000n,
  gasFees: packUint(1_000_000_000n, 10_000_000_000n),
  paymasterAndData: "0x" as Hex,
  signature: dummySignature
};

const simulateCalldata = encodeFunctionData({
  abi: simulationsAbi, functionName: "simulateHandleOp", args: [op, "0x0000000000000000000000000000000000000000", "0x"]
});

console.log("Calling eth_call with state override (real EntryPoint address, simulations bytecode)...");
const raw = await publicClient.transport.request({
  method: "eth_call",
  params: [
    { to: entryPoint, data: simulateCalldata },
    "latest",
    { [entryPoint]: { code: simulationsDeployedBytecode } }
  ]
});
console.log("raw return length:", (raw as string).length);

const { decodeFunctionResult } = await import("viem");
const result = decodeFunctionResult({ abi: simulationsAbi, functionName: "simulateHandleOp", data: raw as Hex }) as any;
console.log("preOpGas:", result.preOpGas.toString());
console.log("paid (wei):", result.paid.toString());
console.log("targetSuccess:", result.targetSuccess);

if (result.preOpGas <= 0n || result.paid <= 0n) {
  throw new Error("FAIL: simulateHandleOp returned a non-positive gas/cost figure");
}
console.log("SIMULATION_RECIPE_RESULT=PASS");
