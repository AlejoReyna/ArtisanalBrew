/**
 * Isolated ERC-4337 v0.7 session-permission acceptance using the published,
 * unmodified MetaMask Delegation Framework v1.3.0 contracts and SDK.
 *
 * Security boundary: all account deployment, typed-data signing, delegation,
 * caveat, execution and UserOperation encoding comes from
 * @metamask/delegation-toolkit@0.13.0. This file only composes those published
 * modules and asserts their on-chain results.
 */
import assert from "node:assert/strict";
import { once } from "node:events";
import type { Server } from "node:http";
import { network } from "hardhat";
import {
  createPublicClient,
  encodeFunctionData,
  http,
  parseEther,
  toHex,
  zeroAddress,
  type Account,
  type Address,
  type Hex
} from "viem";
import { createBundlerClient } from "viem/account-abstraction";
import {
  ExecutionMode,
  Implementation,
  contracts as delegationContracts,
  createDelegation,
  createExecution,
  toMetaMaskSmartAccount,
  type DeleGatorEnvironment,
  type Delegation
} from "@metamask/delegation-toolkit";
import { deployDeleGatorEnvironment } from "@metamask/delegation-toolkit/utils";
import {
  createAgenticPaymentHttpApp,
  createAgenticPaymentRedeemer
} from "../../../src/ThisCafeteria.AgentGateway/src/agenticPaymentRedemption.js";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const BUNDLER_URL = process.env.BUNDLER_URL ?? "http://127.0.0.1:4338";
const RPC_URL = process.env.RPC_URL ?? "http://127.0.0.1:8546";
const EXPECTED_CHAIN_ID = 31337;
const PAYMENT = parseEther("10");

const { viem } = await network.connect();
const hardhatPublicClient = await viem.getPublicClient();
const [deployer, owner, agent, provider, evaluator, treasury, outsider] = await viem.getWalletClients();
const chain = hardhatPublicClient.chain;
if (!chain || chain.id !== EXPECTED_CHAIN_ID) throw new Error(`Expected local chain ${EXPECTED_CHAIN_ID}.`);

const entryPoint = manifest.addresses.entryPoint as Address;
const simpleFactory = manifest.addresses.accountFactory as Address;
const configuredEntryPointCode = await hardhatPublicClient.getCode({ address: entryPoint });
const configuredSimpleFactoryCode = await hardhatPublicClient.getCode({ address: simpleFactory });
assert.ok(configuredEntryPointCode && configuredEntryPointCode !== "0x", "configured EntryPoint must be deployed");
assert.ok(configuredSimpleFactoryCode && configuredSimpleFactoryCode !== "0x", "existing SimpleAccount factory must remain deployed");

const supportedResponse = await fetch(BUNDLER_URL, {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "eth_supportedEntryPoints", params: [] })
});
const supportedBody = await supportedResponse.json() as { result?: string[]; error?: unknown };
if (supportedBody.error || !supportedBody.result?.some((value) => value.toLowerCase() === entryPoint.toLowerCase())) {
  throw new Error(`Bundler does not support configured EntryPoint ${entryPoint}: ${JSON.stringify(supportedBody)}`);
}

// The SDK deploys exact ABI-package bytecode. Supplying EntryPoint reuses the
// repository's pinned v0.7 instance and binds both implementations to it.
const deployed = { EntryPoint: entryPoint } as Record<string, Address>;
const environment = await deployDeleGatorEnvironment(
  deployer as never,
  hardhatPublicClient as never,
  chain,
  deployed
) as DeleGatorEnvironment;
assert.equal(environment.EntryPoint.toLowerCase(), entryPoint.toLowerCase());

const publicClient = createPublicClient({ chain, transport: http(RPC_URL) });
const ownerAccount = await toMetaMaskSmartAccount({
  client: publicClient,
  implementation: Implementation.Hybrid,
  deployParams: [owner.account.address, [], [], []],
  deploySalt: toHex(1001n),
  signer: { walletClient: owner },
  environment
});
const agentAccount = await toMetaMaskSmartAccount({
  client: publicClient,
  implementation: Implementation.Hybrid,
  deployParams: [agent.account.address, [], [], []],
  deploySalt: toHex(2002n),
  signer: { walletClient: agent },
  environment
});
const ownerAddress = await ownerAccount.getAddress();
const agentAddress = await agentAccount.getAddress();
assert.notEqual(ownerAddress.toLowerCase(), agentAddress.toLowerCase());

await deployer.sendTransaction({ to: ownerAddress, value: parseEther("5") });
await deployer.sendTransaction({ to: agentAddress, value: parseEther("5") });

const bundler = createBundlerClient({ client: publicClient, transport: http(BUNDLER_URL) });
async function sendUserOperation(account: typeof ownerAccount, calls: readonly { to: Address; data?: Hex; value?: bigint }[]) {
  const userOpHash = await bundler.sendUserOperation({ account, calls });
  const receipt = await bundler.waitForUserOperationReceipt({ hash: userOpHash, timeout: 60_000 });
  assert.equal(receipt.success, true, `UserOperation ${userOpHash} must succeed`);
  assert.equal(receipt.sender.toLowerCase(), (await account.getAddress()).toLowerCase());
  const transaction = await publicClient.getTransactionReceipt({ hash: receipt.receipt.transactionHash });
  assert.equal(transaction.status, "success", "bundled transaction must settle on-chain");
  return { userOpHash, receipt, transaction };
}

async function expectBundlerRejection(label: string, calls: readonly { to: Address; data?: Hex; value?: bigint }[]) {
  await assert.rejects(
    bundler.prepareUserOperation({ account: agentAccount, calls }),
    () => true,
    `${label} must be rejected by live EntryPoint/bundler simulation`
  );
  console.log(`ONCHAIN_REJECTION=${label}`);
}

const token = await viem.deployContract("TestCafeToken", [deployer.account.address, parseEther("1000000")]);
const wrongToken = await viem.deployContract("TestCoffeeToken", [deployer.account.address, parseEther("1000000")]);
const escrow = await viem.deployContract("AgenticCommerceEscrow", [
  token.address,
  treasury.account.address,
  100n,
  zeroAddress
]);
await deployer.writeContract({
  address: token.address,
  abi: token.abi,
  functionName: "mint",
  args: [ownerAddress, parseEther("100")]
});

const escrowAbi = escrow.abi;
const tokenAbi = token.abi;
const block = await publicClient.getBlock();
const expiry = block.timestamp + 3600n;
const createJobData = encodeFunctionData({
  abi: escrowAbi,
  functionName: "createJob",
  args: [provider.account.address, evaluator.account.address, expiry, "agentic-session-payment"]
});

// First owner UserOperation deterministically deploys the modular account and
// proves root-owner execution remains available.
const rootDeployment = await sendUserOperation(ownerAccount, [{ to: escrow.address, data: createJobData }]);
const ownerCode = await publicClient.getCode({ address: ownerAddress });
assert.ok(ownerCode && ownerCode !== "0x", "owner modular account must deploy through factoryData");
const createdLogs = await publicClient.getContractEvents({
  address: escrow.address,
  abi: escrowAbi,
  eventName: "JobCreated",
  fromBlock: rootDeployment.transaction.blockNumber,
  toBlock: rootDeployment.transaction.blockNumber
}) as any[];
const jobId = createdLogs[0]?.args?.jobId as bigint | undefined;
assert.ok(jobId, "root-owner UserOperation must create the escrow job");
await provider.writeContract({ address: escrow.address, abi: escrowAbi, functionName: "setBudget", args: [jobId, PAYMENT, "0x"] });

const nonceEnforcerAbi = (await import("@metamask/delegation-abis")).NonceEnforcer.abi;
const previousEpoch = await publicClient.readContract({
  address: environment.caveatEnforcers.NonceEnforcer as Address,
  abi: nonceEnforcerAbi,
  functionName: "currentNonce",
  args: [environment.DelegationManager as Address, ownerAddress]
});
const permissionEpoch = previousEpoch + 1n;

const approveData = encodeFunctionData({ abi: tokenAbi, functionName: "approve", args: [escrow.address, PAYMENT] });
const fundData = encodeFunctionData({ abi: escrowAbi, functionName: "fund", args: [jobId, PAYMENT, "0x"] });
const approveExecution = createExecution({ target: token.address, callData: approveData });
const fundExecution = createExecution({ target: escrow.address, callData: fundData });

function unsignedPermission(target: Address, callData: Hex, salt: bigint, beforeThreshold = Number(block.timestamp - 1n), beforeExpiry = Number(expiry)) {
  return createDelegation({
    environment,
    to: agentAddress,
    from: ownerAddress,
    salt: toHex(salt),
    scope: {
      type: "functionCall",
      targets: [target],
      selectors: [callData.slice(0, 10) as Hex],
      exactCalldata: { calldata: callData }
    },
    caveats: [
      { type: "nonce", nonce: toHex(permissionEpoch, { size: 32 }) },
      { type: "timestamp", afterThreshold: beforeThreshold, beforeThreshold: beforeExpiry },
      { type: "limitedCalls", limit: 1 }
    ]
  });
}

async function signPermission(permission: Delegation): Promise<Delegation> {
  const { signature: _, ...signable } = permission;
  const signature = await ownerAccount.signDelegation({ delegation: signable });
  return { ...permission, signature };
}

const approvePermission = await signPermission(unsignedPermission(token.address, approveData, 11n));
const fundPermission = await signPermission(unsignedPermission(escrow.address, fundData, 12n));

function redemptionCall(
  permissionChains: Delegation[][],
  executions: ReturnType<typeof createExecution>[][]
) {
  const data = delegationContracts.DelegationManager.encode.redeemDelegations({
    delegations: permissionChains,
    modes: permissionChains.map(() => ExecutionMode.SingleDefault),
    executions
  });
  return { to: environment.DelegationManager as Address, data };
}

const allowedRedemption = redemptionCall([[approvePermission], [fundPermission]], [[approveExecution], [fundExecution]]);

// A permission signed for the next epoch is unusable until the owner performs
// the on-chain installation/activation transaction.
await expectBundlerRejection("not-installed", [allowedRedemption]);

const incrementNonceData = delegationContracts.NonceEnforcer.encode.incrementNonce(environment.DelegationManager as Address);
await sendUserOperation(ownerAccount, [{ to: environment.caveatEnforcers.NonceEnforcer as Address, data: incrementNonceData }]);
const activeNonce = await publicClient.readContract({
  address: environment.caveatEnforcers.NonceEnforcer as Address,
  abi: nonceEnforcerAbi,
  functionName: "currentNonce",
  args: [environment.DelegationManager as Address, ownerAddress]
});
assert.equal(activeNonce, permissionEpoch, "owner UserOperation must activate the signed nonce epoch on-chain");

const wrongTargetExecution = createExecution({ target: outsider.account.address, callData: approveData });
await expectBundlerRejection("wrong-target", [redemptionCall([[approvePermission]], [[wrongTargetExecution]])]);
const wrongTokenExecution = createExecution({ target: wrongToken.address, callData: approveData });
await expectBundlerRejection("wrong-token", [redemptionCall([[approvePermission]], [[wrongTokenExecution]])]);
const wrongSelectorData = encodeFunctionData({ abi: tokenAbi, functionName: "transfer", args: [escrow.address, PAYMENT] });
await expectBundlerRejection("wrong-selector", [
  redemptionCall([[approvePermission]], [[createExecution({ target: token.address, callData: wrongSelectorData })]])
]);
const wrongAmountData = encodeFunctionData({ abi: tokenAbi, functionName: "approve", args: [escrow.address, PAYMENT + 1n] });
await expectBundlerRejection("wrong-amount", [
  redemptionCall([[approvePermission]], [[createExecution({ target: token.address, callData: wrongAmountData })]])
]);
const nonDefaultModeData = delegationContracts.DelegationManager.encode.redeemDelegations({
  delegations: [[approvePermission]],
  modes: [ExecutionMode.SingleTry],
  executions: [[approveExecution]]
});
await expectBundlerRejection("non-default-execution-mode", [{ to: environment.DelegationManager as Address, data: nonDefaultModeData }]);
const batchModeData = delegationContracts.DelegationManager.encode.redeemDelegations({
  delegations: [[approvePermission]],
  modes: [ExecutionMode.BatchDefault],
  executions: [[approveExecution]]
});
await expectBundlerRejection("batch-execution-mode", [{ to: environment.DelegationManager as Address, data: batchModeData }]);
// Published ERC-7579 ModeLib CALLTYPE_DELEGATECALL is 0xff. This is an
// adversarial test payload only; production construction exposes exclusively
// the toolkit's SingleDefault enum.
const delegateCallMode = (`0xff${"00".repeat(31)}` as Hex) as unknown as ExecutionMode;
const delegateCallModeData = delegationContracts.DelegationManager.encode.redeemDelegations({
  delegations: [[approvePermission]],
  modes: [delegateCallMode],
  executions: [[approveExecution]]
});
await expectBundlerRejection("delegatecall-execution-mode", [{ to: environment.DelegationManager as Address, data: delegateCallModeData }]);

// The agent submits the allowed payment through the live AgentGateway HTTP
// route. The route reconstructs and checks every one-shot caveat, confirms the
// active epoch, signs the agent account's UserOperation and sends it to the
// real bundler. There is no owner/human signing step in this path.
const redemptionApp = createAgenticPaymentHttpApp({
  apiToken: "local-e2e-redemption-token",
  redeemer: createAgenticPaymentRedeemer({
    chains: [{
      chainKey: "ethereum-sepolia",
      chainId: EXPECTED_CHAIN_ID,
      displayName: "Local Agentic E2E",
      nativeCurrency: { name: "Local Ether", symbol: "ETH", decimals: 18 },
      rpcUrl: RPC_URL,
      bundlerUrl: BUNDLER_URL,
      bundlerMode: "unsafe-local",
      environment
    }],
    signer: agent.account as Account,
    deploySalt: toHex(2002n),
    now: () => Number(block.timestamp),
    allowUnsafeBundlerForLocalE2e: true,
    skipBytecodeVerificationForLocalE2e: true
  })
});
const redemptionServer = redemptionApp.listen(0, "127.0.0.1") as Server;
await once(redemptionServer, "listening");
const redemptionAddress = redemptionServer.address();
assert.ok(redemptionAddress && typeof redemptionAddress === "object");
let routeResult: {
  userOperationHash: Hex;
  transactionHash: Hex;
  agentAddress: Address;
  blockNumber: string;
} | undefined;
try {
  const response = await fetch(
    `http://127.0.0.1:${redemptionAddress.port}/agentic-payments/redeem`,
    {
      method: "POST",
      headers: {
        authorization: "Bearer local-e2e-redemption-token",
        "content-type": "application/json",
        "idempotency-key": "local-session-payment-e2e-0001"
      },
      body: JSON.stringify({
        chainKey: "ethereum-sepolia",
        delegatorAddress: ownerAddress,
        agentAddress,
        epoch: permissionEpoch.toString(),
        validAfterUnix: Number(block.timestamp - 1n),
        validBeforeUnix: Number(expiry),
        permissions: [
          { delegation: approvePermission, targetAddress: token.address, calldata: approveData },
          { delegation: fundPermission, targetAddress: escrow.address, calldata: fundData }
        ]
      })
    }
  );
  const body = await response.json() as typeof routeResult & { error?: string; message?: string };
  assert.equal(response.status, 200, `gateway redemption failed: ${JSON.stringify(body)}`);
  routeResult = body;
} finally {
  await new Promise<void>((resolve, reject) => {
    redemptionServer.close((error) => error ? reject(error) : resolve());
  });
}
assert.ok(routeResult, "gateway route must return a redemption receipt");
assert.equal(routeResult.agentAddress.toLowerCase(), agentAddress.toLowerCase());
const settledTransaction = await publicClient.getTransactionReceipt({ hash: routeResult.transactionHash });
console.log(`AGENT_GATEWAY_USER_OPERATION_HASH=${routeResult.userOperationHash}`);
console.log(`AGENT_GATEWAY_TRANSACTION_HASH=${routeResult.transactionHash}`);
const agentCode = await publicClient.getCode({ address: agentAddress });
assert.ok(agentCode && agentCode !== "0x", "agent modular account must deploy through factoryData");
const fundedLogs = await publicClient.getContractEvents({
  address: escrow.address,
  abi: escrowAbi,
  eventName: "JobFunded",
  fromBlock: settledTransaction.blockNumber,
  toBlock: settledTransaction.blockNumber
}) as any[];
assert.equal(fundedLogs.length, 1, "receipt reconciliation must find exactly one JobFunded event");
assert.equal(fundedLogs[0].args.jobId, jobId);
assert.equal(fundedLogs[0].args.client?.toLowerCase(), ownerAddress.toLowerCase());
assert.equal(fundedLogs[0].args.amount, PAYMENT);
console.log(`RECONCILIATION_JOB_FUNDED=${jobId.toString()}`);

// Reusing either permission exceeds the on-chain LimitedCalls quota.
await expectBundlerRejection("cumulative-quota-exhausted", [allowedRedemption]);

const expiryApproveData = encodeFunctionData({ abi: tokenAbi, functionName: "approve", args: [escrow.address, 1n] });
const expiryPermission = await signPermission(unsignedPermission(
  token.address,
  expiryApproveData,
  21n,
  Number(block.timestamp - 1n),
  Number(block.timestamp + 2n)
));
await (hardhatPublicClient as never as { request(args: { method: string; params: unknown[] }): Promise<unknown> }).request({
  method: "evm_increaseTime",
  params: [3]
});
await (hardhatPublicClient as never as { request(args: { method: string; params: unknown[] }): Promise<unknown> }).request({
  method: "evm_mine",
  params: []
});
await expectBundlerRejection("expired", [redemptionCall(
  [[expiryPermission]],
  [[createExecution({ target: token.address, callData: expiryApproveData })]]
)]);

// Prove a distinct epoch-1 permission simulates successfully, revoke the whole
// epoch on-chain, then prove that same previously-valid operation is rejected.
const revocableData = encodeFunctionData({ abi: tokenAbi, functionName: "approve", args: [escrow.address, 2n] });
const revocablePermission = await signPermission(unsignedPermission(
  token.address,
  revocableData,
  31n,
  Number(block.timestamp - 1n),
  Number(block.timestamp + 1800n)
));
const revocableCall = redemptionCall(
  [[revocablePermission]],
  [[createExecution({ target: token.address, callData: revocableData })]]
);
await bundler.prepareUserOperation({ account: agentAccount, calls: [revocableCall] });
console.log("PRE_REVOCATION_SIMULATION=PASS");
await sendUserOperation(ownerAccount, [{ to: environment.caveatEnforcers.NonceEnforcer as Address, data: incrementNonceData }]);
await expectBundlerRejection("revoked", [revocableCall]);

// Root owner operations remain valid after the agent epoch is revoked.
const rootAfterRevokeData = encodeFunctionData({ abi: tokenAbi, functionName: "approve", args: [treasury.account.address, 3n] });
await sendUserOperation(ownerAccount, [{ to: token.address, data: rootAfterRevokeData }]);
assert.equal(
  await publicClient.readContract({ address: token.address, abi: tokenAbi, functionName: "allowance", args: [ownerAddress, treasury.account.address] }),
  3n
);

// The legacy factory code hash remains exactly what was observed before the
// modular path ran. Existing SimpleAccount discovery/deployment is untouched.
assert.equal(await publicClient.getCode({ address: simpleFactory }), configuredSimpleFactoryCode);
console.log("SIMPLE_ACCOUNT_PATH=UNCHANGED");
console.log("METAMASK_SESSION_KEY_E2E=PASS");
