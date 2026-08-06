import assert from "node:assert/strict";
import test from "node:test";
import type { Server } from "node:http";
import { once } from "node:events";
import { createDelegation, type DeleGatorEnvironment } from "@metamask/delegation-toolkit";
import { keccak256, toHex, type Address, type Hex } from "viem";
import {
  ENTRY_POINT_VERSION,
  FRAMEWORK_REVISION,
  MODULAR_ACCOUNT_TYPE,
  assertExactOneShotPermission,
  encodeRedemption,
  requireCompatibleBundler,
} from "../src/agenticPayments.js";
import {
  createAgenticPaymentHttpApp,
  createAgenticPaymentRedeemer,
  type AgenticRedemptionRequest,
  type VerifiedContractName,
  verifyRuntimeBytecode,
} from "../src/agenticPaymentRedemption.js";

const entryPoint = "0x0000000071727De22E5E9d8BAf0edAc6f37da032" as Address;
const otherEntryPoint = "0x0000000000000000000000000000000000000001" as Address;
const environment = {
  EntryPoint: entryPoint,
  DelegationManager: "0x0000000000000000000000000000000000000002",
  SimpleFactory: "0x0000000000000000000000000000000000000004",
  implementations: { HybridDeleGatorImpl: "0x0000000000000000000000000000000000000005" },
  caveatEnforcers: {
    AllowedTargetsEnforcer: "0x0000000000000000000000000000000000000011",
    AllowedMethodsEnforcer: "0x0000000000000000000000000000000000000012",
    ExactCalldataEnforcer: "0x0000000000000000000000000000000000000013",
    NonceEnforcer: "0x0000000000000000000000000000000000000014",
    TimestampEnforcer: "0x0000000000000000000000000000000000000015",
    LimitedCallsEnforcer: "0x0000000000000000000000000000000000000016",
  },
} as unknown as DeleGatorEnvironment;
const delegator = "0x0000000000000000000000000000000000000021" as Address;
const agent = "0x0000000000000000000000000000000000000022" as Address;
const target = "0x0000000000000000000000000000000000000023" as Address;
const calldata = "0x1234567800000000" as Hex;
const validAfter = 1_700_000_000;
const validBefore = 1_700_003_600;
const epoch = 7n;
const salt = toHex(99n);

const signedDelegation = {
  ...createDelegation({
    environment,
    to: agent,
    from: delegator,
    salt,
    scope: {
      type: "functionCall",
      targets: [target],
      selectors: [calldata.slice(0, 10) as Hex],
      exactCalldata: { calldata },
    },
    caveats: [
      { type: "nonce", nonce: toHex(epoch, { size: 32 }) },
      { type: "timestamp", afterThreshold: validAfter, beforeThreshold: validBefore },
      { type: "limitedCalls", limit: 1 },
    ],
  }),
  signature: "0x1234" as Hex,
};

test("modular client rejects an account bound to a different EntryPoint before network use", async () => {
  await assert.rejects(requireCompatibleBundler({
    accountType: MODULAR_ACCOUNT_TYPE,
    frameworkRevision: FRAMEWORK_REVISION,
    entryPointVersion: ENTRY_POINT_VERSION,
    entryPoint: otherEntryPoint,
    bundlerUrl: "http://127.0.0.1:1",
    environment,
  }), /different EntryPoint/);
});

test("modular client fails closed for an unknown account type", async () => {
  await assert.rejects(requireCompatibleBundler({
    accountType: "unknown-account" as typeof MODULAR_ACCOUNT_TYPE,
    frameworkRevision: FRAMEWORK_REVISION,
    entryPointVersion: ENTRY_POINT_VERSION,
    entryPoint,
    bundlerUrl: "http://127.0.0.1:1",
    environment,
  }), /Unknown modular account type/);
});

test("redemption builder rejects an empty permission set", () => {
  assert.throws(() => encodeRedemption(environment, []), /At least one exact permission/);
});

test("exact permission validator accepts only the signed one-shot caveat set", () => {
  const permission = {
    delegator,
    agent,
    target,
    calldata,
    epoch,
    validAfter,
    validBefore,
    salt,
  };
  assert.doesNotThrow(() => assertExactOneShotPermission(environment, permission, signedDelegation));
  assert.throws(
    () => assertExactOneShotPermission(environment, { ...permission, calldata: "0x1234567801" }, signedDelegation),
    /does not match/,
  );
  assert.throws(
    () => assertExactOneShotPermission(
      environment,
      permission,
      { ...signedDelegation, signature: "0x" },
    ),
    /not signed/,
  );
});

test("runtime bytecode verification checks every trusted modular contract hash", async () => {
  const runtimeCode = "0x60006000" as Hex;
  const codeHash = keccak256(runtimeCode);
  const contractNames: VerifiedContractName[] = [
    "EntryPoint",
    "SimpleFactory",
    "HybridDeleGatorImpl",
    "DelegationManager",
    "AllowedTargetsEnforcer",
    "AllowedMethodsEnforcer",
    "ExactCalldataEnforcer",
    "LimitedCallsEnforcer",
    "NonceEnforcer",
    "TimestampEnforcer",
  ];
  const expectedCodeHashes = Object.fromEntries(
    contractNames.map((name) => [name, codeHash]),
  );
  const chain = {
    chainKey: "ethereum-sepolia" as const,
    chainId: 11_155_111,
    displayName: "Sepolia",
    nativeCurrency: { name: "Ether", symbol: "ETH", decimals: 18 },
    rpcUrl: "https://rpc.invalid",
    bundlerUrl: "https://bundler.invalid",
    bundlerMode: "safe" as const,
    environment,
    expectedCodeHashes,
  };
  let reads = 0;
  await verifyRuntimeBytecode(chain, async () => {
    reads += 1;
    return runtimeCode;
  });
  assert.equal(reads, contractNames.length);

  await assert.rejects(
    verifyRuntimeBytecode({
      ...chain,
      expectedCodeHashes: { ...expectedCodeHashes, NonceEnforcer: `0x${"00".repeat(32)}` },
    }, async () => runtimeCode),
    /NonceEnforcer runtime bytecode hash mismatch/,
  );
});

test("public redemption preflight refuses an unsafe bundler before network use", async () => {
  const redeemer = createAgenticPaymentRedeemer({
    chains: [{
      chainKey: "ethereum-sepolia",
      chainId: 11_155_111,
      displayName: "Sepolia",
      nativeCurrency: { name: "Ether", symbol: "ETH", decimals: 18 },
      rpcUrl: "http://127.0.0.1:1",
      bundlerUrl: "http://127.0.0.1:1",
      bundlerMode: "unsafe-local",
      environment,
    }],
    signer: { address: agent, type: "json-rpc" },
    deploySalt: toHex(1n),
  });
  await assert.rejects(redeemer.preflight(), /safe-mode bundler/);
});

test("agent redemption route authenticates and submits the exact granted permission", async () => {
  const expectedResult = {
    chainKey: "ethereum-sepolia",
    agentAddress: agent,
    userOperationHash: `0x${"11".repeat(32)}` as Hex,
    transactionHash: `0x${"22".repeat(32)}` as Hex,
    blockNumber: "123",
  };
  let received: AgenticRedemptionRequest | undefined;
  let redemptionCalls = 0;
  const app = createAgenticPaymentHttpApp({
    apiToken: "route-test-token",
    redeemer: {
      async preflight() {},
      async redeem(request) {
        redemptionCalls += 1;
        received = request;
        return expectedResult;
      },
    },
  });
  const server = app.listen(0, "127.0.0.1") as Server;
  await once(server, "listening");

  try {
    const address = server.address();
    assert.ok(address && typeof address === "object");
    const request = {
      chainKey: "ethereum-sepolia",
      delegatorAddress: delegator,
      agentAddress: agent,
      epoch: epoch.toString(),
      validAfterUnix: validAfter,
      validBeforeUnix: validBefore,
      permissions: [{ delegation: signedDelegation, targetAddress: target, calldata }],
    };

    const unauthorized = await fetch(`http://127.0.0.1:${address.port}/agentic-payments/redeem`, {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "idempotency-key": "agentic-route-test-0001",
      },
      body: JSON.stringify(request),
    });
    assert.equal(unauthorized.status, 401);

    const response = await fetch(`http://127.0.0.1:${address.port}/agentic-payments/redeem`, {
      method: "POST",
      headers: {
        authorization: "Bearer route-test-token",
        "content-type": "application/json",
        "idempotency-key": "agentic-route-test-0001",
      },
      body: JSON.stringify(request),
    });
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { ...expectedResult, replay: false });
    assert.equal(received?.permissions[0]?.delegation.signature, signedDelegation.signature);
    assert.equal(received?.permissions[0]?.calldata, calldata);

    const replay = await fetch(`http://127.0.0.1:${address.port}/agentic-payments/redeem`, {
      method: "POST",
      headers: {
        authorization: "Bearer route-test-token",
        "content-type": "application/json",
        "idempotency-key": "agentic-route-test-0001",
      },
      body: JSON.stringify(request),
    });
    assert.equal(replay.status, 200);
    assert.deepEqual(await replay.json(), { ...expectedResult, replay: true });
    assert.equal(redemptionCalls, 1, "a replay must not submit another UserOperation");

    const conflicting = await fetch(`http://127.0.0.1:${address.port}/agentic-payments/redeem`, {
      method: "POST",
      headers: {
        authorization: "Bearer route-test-token",
        "content-type": "application/json",
        "idempotency-key": "agentic-route-test-0001",
      },
      body: JSON.stringify({ ...request, epoch: "8" }),
    });
    assert.equal(conflicting.status, 409);
    assert.equal(redemptionCalls, 1);
  } finally {
    if (server.listening) {
      await new Promise<void>((resolve, reject) => {
        server.close((error) => error ? reject(error) : resolve());
      });
    }
  }
});
